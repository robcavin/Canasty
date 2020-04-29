using AOT;
using Oculus.Platform;
using Oculus.Platform.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Cloud.UserReporting;
using Unity.Cloud.UserReporting.Client;
using Unity.Cloud.UserReporting.Plugin;
using UnityEngine;
using UnityEngine.Android;

public class CanastyController : MonoBehaviour
{
    private static OvrAvatar localAvatar = null;

    GameObject mainDeck;
    GameObject rightHandAnchor;
    GameObject leftHandAnchor;

    SimpleGrabber rightHandGrabber;

    DeckController deckController;
    Collider deckCollider;

    CardHandController cardHandController;

    GameObject rightHandHeld;

    UnityUserReportingUpdater reportingUpdater = new UnityUserReportingUpdater();

    public OvrAvatar localAvatarPrefab = null;
    public OvrAvatar remoteAvatarPrefab = null;

    private string appID = "2850434391711779";
    private ulong roomID = 755036931694010;
    private User localUser = null;

    public enum ConnectionState {
        Disconnected,
        Connecting,
        Reconnecting,
        Connected,
        Disconnecting,
    }

    public class ConnectionStates
    {
        public string username;
        public bool netState;
        public int netTimeouts;
        public int netReconnects;
        public int voipTimeouts;
        public int voipReconnects;
        public ulong ping;
        public int pingTimeout;
        public ConnectionState networkState;
        public ConnectionState voipState;
    }

    // These need to be cleared on leaving the room
    private bool userInRoom = false;
    private Room room = null;
    public Dictionary<ulong, ConnectionStates> remoteConnectionStates = new Dictionary<ulong, ConnectionStates>();

    private Dictionary<ulong, OvrAvatar> remoteAvatars = new Dictionary<ulong, OvrAvatar>();
    private Dictionary<ulong, CardHandController> remoteCardHands = new Dictionary<ulong, CardHandController>();
    private List<ulong> pendingMouthAnchorAttach = new List<ulong>();
    private List<ulong> pendingLeftCardHandAttach = new List<ulong>();
    private Dictionary<ulong, byte[]> pendingCardHandUpdates = new Dictionary<ulong, byte[]>();
    private bool pendingStateUpdateRequest = false;

    private uint avatarSequence = 0;
    private Dictionary<string, GameObject> trackedObjects = new Dictionary<string, GameObject>();

    private float lastPingTime = 0;

    public enum PacketType : byte
    {
        AVATAR_UPDATE = 1,
        TRACKED_OBJECT_UPDATE,
        CARD_HAND_UPDATE,
        DECK_UPDATE,
        RIGID_BODY_UPDATE,
        RESET,
        STATE_UPDATE_REQUEST
    }

    private bool ShouldOwnConnection(ulong userID)
    {
        return userID < localUser.ID;
    }

    public string GetUsernameForID(ulong userID)
    {
        string username = null;
        if (room != null)
            foreach (var user in room.UsersOptional)
                if (user.ID == userID)
                    username = user.OculusID;

        return username;
    }

    // When someone joins or leaves, do add or remove
    private void OnRoomUpdateCallback(Message<Room> message)
    {
        if (message.IsError)
            Debug.LogError("Room connecton error - " + message.GetError().Message);
        else
        {
            room = message.GetRoom();

            StringBuilder roomMessage = new StringBuilder("Room update - ");

            // If we are just joining the room and someone else is already there, we 
            //   need to update state
            bool userNeedsStateUpdate = !userInRoom && 
                (room.UsersOptional.Count > 1);

            foreach (var user in room.UsersOptional)
            {
                roomMessage.Append(user.OculusID + " ");

                if (user.ID == localUser.ID)
                {
                    if (!userInRoom)
                    {
                        userInRoom = true;
                        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "User joined room");
                    }
                }
                else
                {
                    if (!remoteConnectionStates.ContainsKey(user.ID))
                        remoteConnectionStates.Add(user.ID, new ConnectionStates() { username = user.OculusID });

                    if (ShouldOwnConnection(user.ID))
                    {
                        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Initiating net and voip to " + user.ID.ToString());

                        // Network state
                        if (remoteConnectionStates[user.ID].networkState == ConnectionState.Disconnected)
                        {
                            Net.Connect(user.ID);
                            remoteConnectionStates[user.ID].networkState = ConnectionState.Connecting;
                            remoteConnectionStates[user.ID].netTimeouts = 0;
                        }
                        else
                            UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Skipping net connection since already connected to " + user.ID.ToString());

                        // VOIP state
                        if (remoteConnectionStates[user.ID].voipState == ConnectionState.Disconnected)
                        {
                            Voip.Start(user.ID);
                            remoteConnectionStates[user.ID].voipState = ConnectionState.Connecting;
                            remoteConnectionStates[user.ID].voipTimeouts = 0;
                        }
                        else
                            UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Skipping voip connection since already connected to " + user.ID.ToString());
                    }
                    else
                    {
                        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Skipping since don't own connection to " + user.ID.ToString());
                    }

                    // If someone was in the room before us, ask for the state
                    // once we connect.
                    if (userNeedsStateUpdate &&
                        (remoteConnectionStates[user.ID].networkState == ConnectionState.Connected))
                    { 
                        OnRequestStateUpate(user.ID);
                        userNeedsStateUpdate = false;
                    }
                }
            }

            UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, roomMessage.ToString());

            if (userNeedsStateUpdate)
            {
                UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "State update request pending");
                pendingStateUpdateRequest = true;
            }
        }
    }

    OvrAvatar createAvatar(ulong userID)
    {
        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Creating avatar for " + userID.ToString());
        var avatar = Instantiate(remoteAvatarPrefab);
        avatar.oculusUserID = userID.ToString();
        avatar.UseSDKPackets = true;
        avatar.CanOwnMicrophone = false;
        if (!pendingLeftCardHandAttach.Contains(userID))
            pendingLeftCardHandAttach.Add(userID);
        return avatar;
    }

    ulong destroyAvatar(ulong userID)
    {
        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Destroying avatar for " + userID.ToString());

        if (remoteAvatars[userID].MouthAnchor != null)
            Destroy(remoteAvatars[userID].MouthAnchor.GetComponent<VoipAudioSourceHiLevel>());

        if (remoteCardHands.ContainsKey(userID))
        {
            Destroy(remoteCardHands[userID]);
            remoteCardHands.Remove(userID);
        }

        Destroy(remoteAvatars[userID].gameObject);

        return userID;
    }

    private bool IsUserInRoom(ulong userID)
    {
        bool userInRoom = false;
        if (room != null)
            foreach (var user in room.UsersOptional)
                if (user.ID == userID) userInRoom = true;
        return userInRoom;
    }

    void OnConnectionStateChangedCallback(Message<NetworkingPeer> message)
    {
        if (message.IsError)
            Debug.LogError("Net connecton error - " + message.GetError().Message);
        else
        {
            NetworkingPeer peer = message.GetNetworkingPeer();

            if (!remoteConnectionStates.ContainsKey(peer.ID))
                remoteConnectionStates.Add(peer.ID, new ConnectionStates() { username = peer.ID.ToString() });

            switch (peer.State)
            {
                case PeerConnectionState.Connected:
                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Net connected to " + peer.ID.ToString() + 
                        ". State was " + remoteConnectionStates[peer.ID].networkState.ToString());

                    remoteConnectionStates[peer.ID].networkState = ConnectionState.Connected;

                    if (!remoteAvatars.ContainsKey(peer.ID))
                        remoteAvatars.Add(peer.ID, createAvatar(peer.ID));

                    if (pendingStateUpdateRequest)
                        OnRequestStateUpate(peer.ID);

                    break;

                case PeerConnectionState.Timeout:

                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Net timeout for " + peer.ID.ToString() + 
                        ". State was " + remoteConnectionStates[peer.ID].networkState.ToString());

                    if (IsUserInRoom(peer.ID) && ShouldOwnConnection(peer.ID) &&
                        (remoteConnectionStates[peer.ID].networkState == ConnectionState.Connecting))
                        {
                            UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, 
                                "Net attempting reconnect to " + peer.ID.ToString() +
                                ". Attempt # " + remoteConnectionStates[peer.ID].netTimeouts + 
                                ". State was " + remoteConnectionStates[peer.ID].networkState.ToString());
                            Net.Connect(peer.ID);
                            remoteConnectionStates[peer.ID].networkState = ConnectionState.Connecting;
                            remoteConnectionStates[peer.ID].netTimeouts += 1;
                        }
                    else
                        remoteConnectionStates[peer.ID].networkState = ConnectionState.Disconnected;

                    if (remoteAvatars.ContainsKey(peer.ID))
                        remoteAvatars.Remove(destroyAvatar(peer.ID));

                    break;

                case PeerConnectionState.Closed:

                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Net disconnect from " + peer.ID.ToString()+
                        ". State was " + remoteConnectionStates[peer.ID].networkState.ToString());

                    if (IsUserInRoom(peer.ID) && ShouldOwnConnection(peer.ID) &&
                        (remoteConnectionStates[peer.ID].networkState != ConnectionState.Disconnecting))
                        {
                            UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, 
                                "Net attempting reconnect to " + peer.ID.ToString() +
                                ". Reconnect # " + remoteConnectionStates[peer.ID].netReconnects +
                                ". State was " + remoteConnectionStates[peer.ID].networkState.ToString());
                            Net.Connect(peer.ID);
                            remoteConnectionStates[peer.ID].networkState = ConnectionState.Reconnecting;
                            remoteConnectionStates[peer.ID].netReconnects += 1;
                        }
                    else
                        remoteConnectionStates[peer.ID].networkState = ConnectionState.Disconnected;

                    if (remoteAvatars.ContainsKey(peer.ID))
                        remoteAvatars.Remove(destroyAvatar(peer.ID));

                    break;

                default:
                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Warning, "Net unexpected state from " + peer.ID.ToString());
                    Debug.LogError("Unexpected connection state");
                    break;
            }
        }
    }

    void OnVoipStateChangedCallback(Message<NetworkingPeer> message)
    {
        if (message.IsError)
            Debug.LogError("Voip connecton error - " + message.GetError().Message);
        else
        {
            NetworkingPeer peer = message.GetNetworkingPeer();

            if (!remoteConnectionStates.ContainsKey(peer.ID))
                remoteConnectionStates.Add(peer.ID, new ConnectionStates());

            switch (peer.State)
            {
                case PeerConnectionState.Connected:
                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Voip connected to " + peer.ID.ToString() +
                        ". State was " + remoteConnectionStates[peer.ID].voipState.ToString());

                    remoteConnectionStates[peer.ID].voipState = ConnectionState.Connected;

                    if (!pendingMouthAnchorAttach.Contains(peer.ID))
                        pendingMouthAnchorAttach.Add(peer.ID); // Annoyingly mouth anchor isn't populated until first update

                    break;

                case PeerConnectionState.Timeout:

                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Voip timeout for " + peer.ID.ToString() +
                        ". State was " + remoteConnectionStates[peer.ID].voipState.ToString());

                    if (IsUserInRoom(peer.ID) && ShouldOwnConnection(peer.ID) &&
                        (remoteConnectionStates[peer.ID].voipState == ConnectionState.Connecting))
                        {
                            UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, 
                                "Voip attempting reconnect to " + peer.ID.ToString() +
                                ". Attempt # " + remoteConnectionStates[peer.ID].voipTimeouts +
                                ". State was " + remoteConnectionStates[peer.ID].voipState.ToString());
                            Voip.Start(peer.ID);
                            remoteConnectionStates[peer.ID].voipState = ConnectionState.Connecting;
                            remoteConnectionStates[peer.ID].voipTimeouts += 1;
                        }
                    else
                        remoteConnectionStates[peer.ID].voipState = ConnectionState.Disconnected;
                    break;

                case PeerConnectionState.Closed:
                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Voip disconnect from " + peer.ID.ToString() +
                        ". State was " + remoteConnectionStates[peer.ID].voipState.ToString());

                    if (IsUserInRoom(peer.ID) && ShouldOwnConnection(peer.ID) && 
                        (remoteConnectionStates[peer.ID].voipState != ConnectionState.Disconnecting))
                        {
                            UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, 
                                "Voip attempting reconnect to " + peer.ID.ToString() +
                                ". Reconnect # " + remoteConnectionStates[peer.ID].voipReconnects +
                                ". State was " + remoteConnectionStates[peer.ID].voipState.ToString());
                            Voip.Start(peer.ID);
                            remoteConnectionStates[peer.ID].voipState = ConnectionState.Reconnecting;
                            remoteConnectionStates[peer.ID].voipReconnects += 1;
                        }
                    else
                        remoteConnectionStates[peer.ID].voipState = ConnectionState.Disconnected;

                    if (remoteAvatars.ContainsKey(peer.ID) &&
                        (remoteAvatars[peer.ID].MouthAnchor != null))
                        Destroy(remoteAvatars[peer.ID].MouthAnchor.GetComponent<VoipAudioSourceHiLevel>());

                    pendingMouthAnchorAttach.Remove(peer.ID);

                    break;

                default:
                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Warning, "Voip unexpected state from " + peer.ID.ToString());
                    Debug.LogError("Unexpected connection state");
                    break;
            }
        }
    }

    void OnRequestStateUpate(ulong userID)
    {
        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Requesting state update from " + userID.ToString());
        pendingStateUpdateRequest = false;
        using (BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(64)))
        {
            binaryWriter.Write((byte)PacketType.STATE_UPDATE_REQUEST);
            binaryWriter.Write(localUser.ID);
            Net.SendPacket(userID, ((MemoryStream)binaryWriter.BaseStream).ToArray(), SendPolicy.Reliable);
        }
    }


    void OnConnectRequestCallback(Message<NetworkingPeer> msg)
    {
        if (msg.IsError)
            Debug.LogError("Net connecton error - " + msg.GetError().Message);
        else
        {
            var peer = msg.GetNetworkingPeer();

            if (!remoteConnectionStates.ContainsKey(peer.ID))
                remoteConnectionStates.Add(peer.ID, new ConnectionStates());

            UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Net accept of " + peer.ID.ToString() + 
                ".  State was " + remoteConnectionStates[peer.ID].networkState.ToString());

            Net.Accept(peer.ID);
            remoteConnectionStates[peer.ID].networkState = ConnectionState.Connecting;
            remoteConnectionStates[peer.ID].netTimeouts = 0;
        }
    }


    void OnVoipConnectRequestCallback(Message<NetworkingPeer> msg)
    {
        if (msg.IsError)
            Debug.LogError("Voip connecton error - " + msg.GetError().Message);
        else
        {
            var peer = msg.GetNetworkingPeer();

            if (!remoteConnectionStates.ContainsKey(peer.ID))
                remoteConnectionStates.Add(peer.ID, new ConnectionStates());

            UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Voip accept of " + peer.ID.ToString() +
                ".  State was " + remoteConnectionStates[peer.ID].voipState.ToString());

            Voip.Accept(peer.ID);
            remoteConnectionStates[peer.ID].voipState = ConnectionState.Connecting;
            remoteConnectionStates[peer.ID].voipTimeouts = 0;
        }
    }


    void OnPingResultCallback(Message<PingResult> msg)
    {
        if (msg.IsError)
            Debug.LogError("Ping error - " + msg.GetError().Message);
        else
        {
            var pingResult = msg.GetPingResult();

            if (!remoteConnectionStates.ContainsKey(pingResult.ID))
                remoteConnectionStates.Add(pingResult.ID, new ConnectionStates());

            if (pingResult.IsTimeout) remoteConnectionStates[pingResult.ID].pingTimeout += 1;
            else
            {
                remoteConnectionStates[pingResult.ID].ping = pingResult.PingTimeUsec;
                remoteConnectionStates[pingResult.ID].pingTimeout = 0;
            }
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        UserReportingClientConfiguration config = new UserReportingClientConfiguration(500, 300, 60, 10);
        UnityUserReporting.Configure(config);

        rightHandAnchor = GameObject.Find("RightHandAnchor");
        leftHandAnchor = GameObject.Find("LeftHandAnchor");

        rightHandGrabber = rightHandAnchor.GetComponent<SimpleGrabber>();

        var mainDeck = GameObject.Find("MainDeck");
        deckController = mainDeck.GetComponent<DeckController>();
        deckCollider = mainDeck.GetComponent<Collider>();

        cardHandController = leftHandAnchor.GetComponent<CardHandController>();

        Core.AsyncInitialize(appID.ToString()).OnComplete((Message<Oculus.Platform.Models.PlatformInitialize> init_message) =>
        {
            if (init_message.IsError) Debug.LogError("Failed to initialize - " + init_message);
            else
            {
                Entitlements.IsUserEntitledToApplication().OnComplete((entitlemnets_message) =>
                {
                    if (entitlemnets_message.IsError) Debug.LogError("Entitlements failed - " + entitlemnets_message);
                    else
                    {
                        Users.GetLoggedInUser().OnComplete((Message<Oculus.Platform.Models.User> logged_in_user_message) =>
                        {
                            if (logged_in_user_message.IsError) Debug.LogError("Could not retrieve logged in user - " + logged_in_user_message);
                            else
                            {
                                localUser = logged_in_user_message.GetUser();
                                UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "User logged in");

                                UnityUserReporting.CurrentClient.AddDeviceMetadata("userID", localUser.ID.ToString());
                                UnityUserReporting.CurrentClient.AddDeviceMetadata("username", localUser.OculusID);

                                localAvatar = Instantiate(localAvatarPrefab);
                                localAvatar.CanOwnMicrophone = false;
                                localAvatar.UseSDKPackets = true;
                                localAvatar.RecordPackets = true;
                                localAvatar.PacketRecorded += OnLocalAvatarPacketRecorded;
                                localAvatar.oculusUserID = localUser.ID.ToString();

                                var trackingSpace = GameObject.Find("TrackingSpace");
                                localAvatar.transform.position = trackingSpace.transform.position;
                                localAvatar.transform.rotation = trackingSpace.transform.rotation;
                                localAvatar.transform.parent = trackingSpace.transform;

                                Rooms.SetUpdateNotificationCallback(OnRoomUpdateCallback);
                                Net.SetConnectionStateChangedCallback(OnConnectionStateChangedCallback);
                                Net.SetPeerConnectRequestCallback(OnConnectRequestCallback);
                                Net.SetPingResultNotificationCallback(OnPingResultCallback);
                                Voip.SetVoipConnectRequestCallback(OnVoipConnectRequestCallback);
                                Voip.SetVoipStateChangeCallback(OnVoipStateChangedCallback);

                                // NOTE - Setting this before the platform is initialized does NOT WORK!!
                                Voip.SetMicrophoneFilterCallback(MicrophoneFilterCallback);

                                Rooms.Join(roomID, true).OnComplete(OnRoomUpdateCallback);

#if PLATFORM_ANDROID
                                if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                                {
                                    Permission.RequestUserPermission(Permission.Microphone);
                                }

                                UnityUserReporting.CurrentClient.AddDeviceMetadata("Microphone Enabled", 
                                    Permission.HasUserAuthorizedPermission(Permission.Microphone).ToString());
#endif
                            }
                        });
                    }
                });
            }
        });
    }

    void resetGame()
    {
        deckController.reset();
        cardHandController.reset();
        foreach (var hand in remoteCardHands.Values)
            hand.reset();

        trackedObjects.Clear();
        rightHandHeld = null;

        var cards = GameObject.FindGameObjectsWithTag("PlayingCard");
        foreach (var card in cards)
            Destroy(card);
    }


    private void SendPacketToConnectedUsers(byte[] packet, SendPolicy policy)
    {
        foreach (var user in remoteConnectionStates)
            if (user.Value.networkState == ConnectionState.Connected)
                Net.SendPacket(user.Key, packet, policy);
    }


    // Handle object updates
    //
    public void OnLocalAvatarPacketRecorded(object sender, OvrAvatar.PacketEventArgs args)
    {
        if (!userInRoom) return;

        using (var binaryWriter = new BinaryWriter(new MemoryStream(64)))
        {
            // Create the packet header
            binaryWriter.Write((byte)PacketType.AVATAR_UPDATE);
            binaryWriter.Write(localUser.ID);

            binaryWriter.Write(localAvatar.transform.position);
            binaryWriter.Write(localAvatar.transform.rotation);

            // Append the actual avatar data
            binaryWriter.Write(avatarSequence++);

            var size = Oculus.Avatar.CAPI.ovrAvatarPacket_GetSize(args.Packet.ovrNativePacket);
            byte[] data = new byte[size];
            Oculus.Avatar.CAPI.ovrAvatarPacket_Write(args.Packet.ovrNativePacket, size, data);
            binaryWriter.Write(size);
            binaryWriter.Write(data);

            // Send that sucker
            SendPacketToConnectedUsers(((MemoryStream)binaryWriter.BaseStream).ToArray(), SendPolicy.Unreliable);
        }
    }

    public void OnDeckUpdate(List<byte> cards, int cardIndex, SendPolicy policy = SendPolicy.Reliable)
    {
        if (!userInRoom)
        {
            UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Warning, "Attempted to update deck while not in room");
            Debug.LogError("Attempted to update deck while not in room");
            return;
        }

        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Broadcast - deck update - Card index " + cardIndex);
        using (BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(256)))
        {
            binaryWriter.Write((byte)PacketType.DECK_UPDATE);
            binaryWriter.Write(localUser.ID);
            binaryWriter.Write((byte)cards.Count);
            binaryWriter.Write((byte)cardIndex);
            binaryWriter.Write(cards.ToArray());

            SendPacketToConnectedUsers(((MemoryStream)binaryWriter.BaseStream).ToArray(), policy);
        }
    }


    public void OnTrackedObjectUpdate(GameObject obj, SendPolicy policy = SendPolicy.Unreliable)
    {
        if (!userInRoom) return;

        using (BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(64)))
        {
            binaryWriter.Write((byte)PacketType.TRACKED_OBJECT_UPDATE);
            binaryWriter.Write(localUser.ID);
            binaryWriter.Write(obj.name);
            binaryWriter.Write(obj.transform.position);
            binaryWriter.Write(obj.transform.rotation);

            SendPacketToConnectedUsers(((MemoryStream)binaryWriter.BaseStream).ToArray(), policy);
        }
    }

    public void OnCardHandUpdate(LinkedList<GameObject> cards, SendPolicy policy = SendPolicy.Unreliable)
    {
        if (!userInRoom) return;

        List<byte> cardIds = new List<byte>(10);
        foreach (var card in cards)
            if (card != null)
                cardIds.Add(byte.Parse(card.name.Substring(4)));

        using (BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(128)))
        {
            binaryWriter.Write((byte)PacketType.CARD_HAND_UPDATE);
            binaryWriter.Write(localUser.ID);
            binaryWriter.Write((byte)cardIds.Count);
            binaryWriter.Write(cardIds.ToArray());

            SendPacketToConnectedUsers(((MemoryStream)binaryWriter.BaseStream).ToArray(), policy);
        }
    }

    public void OnRigidBodyUpdate(Rigidbody rigidBody, SendPolicy policy = SendPolicy.Unreliable)
    {
        using (BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(64)))
        {
            binaryWriter.Write((byte)PacketType.RIGID_BODY_UPDATE);
            binaryWriter.Write(localUser.ID);
            binaryWriter.Write(rigidBody.gameObject.name);
            binaryWriter.Write(rigidBody.position);
            binaryWriter.Write(rigidBody.rotation);
            SendPacketToConnectedUsers(((MemoryStream)binaryWriter.BaseStream).ToArray(), policy);
        }
    }

    public void OnReset(SendPolicy policy = SendPolicy.Reliable)
    {
        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Broadcast - game reset");
        using (BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(64)))
        {
            binaryWriter.Write((byte)PacketType.RESET);
            binaryWriter.Write(localUser.ID);
            SendPacketToConnectedUsers(((MemoryStream)binaryWriter.BaseStream).ToArray(), policy);
        }

        resetGame();

        if (room != null)
        {
            List<CardHandController> handControllers = new List<CardHandController>();

            foreach (var user in room.UsersOptional)
            {
                if (user.ID == localUser.ID) handControllers.Add(cardHandController);
                else
                    if (remoteCardHands.ContainsKey(user.ID)) handControllers.Add(remoteCardHands[user.ID]);
            }

            for (int i = 0; i < 13; i++)
            {
                foreach (var hand in handControllers)
                {
                    var card = deckController.getNextCard(Vector3.zero, Quaternion.identity);
                    hand.AddCard(card);
                }
            }

            foreach (var hand in handControllers)
                hand.SortCards();

            deckController.SendUpdate(SendPolicy.Reliable);
            cardHandController.SendUpdate(SendPolicy.Reliable);
            foreach (var remoteCardHand in remoteCardHands.Values)
                remoteCardHand.SendUpdate(SendPolicy.Reliable);
        }

    }

    private GameObject getTrackedObject(string name)
    {
        GameObject obj = null;
        if (trackedObjects.ContainsKey(name))
            obj = trackedObjects[name];
        else
        {
            obj = GameObject.Find(name);
            if ((obj == null) && name.StartsWith("Card"))
                obj = DeckController.instantiateCard(byte.Parse(name.Substring(4)));
            if (obj)
                trackedObjects[name] = obj;
        }

        return obj;
    }

    public bool ReadyToPlay()
    {
        if (room == null)
            return false;

        bool allUsersInRoomAreConnected = true;
        foreach (var user in room.UsersOptional)
        {
            if (user.ID == localUser.ID) continue;
            if (!remoteConnectionStates.ContainsKey(user.ID) ||
                remoteConnectionStates[user.ID].networkState != ConnectionState.Connected)
                allUsersInRoomAreConnected = false;
        }

        return allUsersInRoomAreConnected && !pendingStateUpdateRequest;
    }

    // Update is called once per frame
    void Update()
    {
        // Ping everyone every 5 seconds
        float now = Time.time;
        if((Time.time - lastPingTime) > 5)
        {
            foreach (var connection in remoteConnectionStates)
                if (connection.Value.networkState == ConnectionState.Connected)
                    Net.Ping(connection.Key);
            lastPingTime = now;
        }

        if (OVRInput.GetDown(OVRInput.RawButton.B) || Input.GetKeyDown(KeyCode.Space))
        {
            var caemraRig = GameObject.Find("OVRCameraRig");
            caemraRig.transform.RotateAround(new Vector3(0, 0, 0), new Vector3(0, 1, 0), 90);
        }

        if (ReadyToPlay())
        {
            if (OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.55)
            {
                if ((rightHandHeld == null) && (rightHandGrabber.otherCollider != null))
                {
                    if (rightHandGrabber.otherCollider == deckCollider)
                    {
                        rightHandHeld = deckController.getNextCard(
                            deckController.transform.position,
                            deckController.transform.rotation);
                    }

                    else
                        rightHandHeld = rightHandGrabber.otherCollider.gameObject;

                    // Might not be in the hand, but it's as costly to check and no harm in trying
                    if (cardHandController.isCardInHand(rightHandHeld))
                        cardHandController.ReleaseCard(rightHandHeld);

                    rightHandHeld.transform.parent = rightHandAnchor.transform;
                    rightHandHeld.GetComponent<Rigidbody>().isKinematic = true;

                    rightHandHeld.GetComponent<SimpleGrabbable>().isGrabbed = true;
                }
            }

            if (OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) < 0.35)
            {
                if (rightHandHeld != null)
                {
                    rightHandHeld.transform.parent = null;
                    rightHandHeld.GetComponent<Rigidbody>().isKinematic = false;
                    rightHandHeld.GetComponent<SimpleGrabbable>().isGrabbed = false;

                    if ((rightHandHeld.GetComponent<CardController>() != null) && cardHandController.isHighlighting)
                        cardHandController.AddCard(rightHandHeld);

                    else
                        // Notify remote users that we are dropping the card
                        OnRigidBodyUpdate(rightHandHeld.GetComponent<Rigidbody>());

                    rightHandHeld = null;
                }
            }
        }


        Packet packet;
        while ((packet = Net.ReadPacket()) != null)
        {
            byte[] packetBuf = new byte[packet.Size];
            packet.ReadBytes(packetBuf);
            packet.Dispose();

            using (BinaryReader binaryReader = new BinaryReader(new MemoryStream(packetBuf)))
            {
                var type = (PacketType)binaryReader.ReadByte();
                var userID = binaryReader.ReadUInt64();

                switch (type)
                {
                    case PacketType.AVATAR_UPDATE:
                        var position = binaryReader.ReadVector3();
                        var orientation = binaryReader.ReadQuaternion();
                        var avatarPacketSequence = binaryReader.ReadInt32();
                        var avatar = remoteAvatars.ContainsKey(userID) ? remoteAvatars[userID] : null;

                        if (avatar != null)
                        {
                            OvrAvatarPacket avatarPacket = null;
                            int size = binaryReader.ReadInt32();
                            byte[] sdkData = binaryReader.ReadBytes(size);

                            IntPtr tempPacket = Oculus.Avatar.CAPI.ovrAvatarPacket_Read((UInt32)sdkData.Length, sdkData);
                            avatarPacket = new OvrAvatarPacket { ovrNativePacket = tempPacket };

                            avatar.transform.position = position;
                            avatar.transform.rotation = orientation;
                            avatar.GetComponent<OvrAvatarRemoteDriver>().QueuePacket(avatarPacketSequence, avatarPacket);
                        }
                        break;

                    case PacketType.TRACKED_OBJECT_UPDATE:
                        var objName = binaryReader.ReadString();
                        var objPosition = binaryReader.ReadVector3();
                        var objOrientation = binaryReader.ReadQuaternion();
                        var obj = getTrackedObject(objName);

                        if (obj != null)
                        {
                            obj.transform.parent = null;
                            var rigidBody = obj.GetComponent<Rigidbody>();
                            if (rigidBody) rigidBody.isKinematic = true;
                            obj.transform.position = objPosition;
                            obj.transform.rotation = objOrientation;
                        }
                        break;

                    case PacketType.DECK_UPDATE:
                        var cardCount = binaryReader.ReadByte();
                        var cardIndex = binaryReader.ReadByte();
                        var cards = binaryReader.ReadBytes(cardCount);
                        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, 
                            "Received - deck update from " + userID.ToString() + " - " + cardIndex);
                        deckController.updateDeck(new List<byte>(cards), cardIndex);
                        break;

                    case PacketType.CARD_HAND_UPDATE:
                        var cardHandCount = binaryReader.ReadByte();
                        var cardHand = binaryReader.ReadBytes(cardHandCount);
                        if (remoteCardHands.ContainsKey(userID))
                            remoteCardHands[userID].updateCardHand(cardHand);
                        else if (userID != localUser.ID)  // Can happen on state updates
                            if (pendingCardHandUpdates.ContainsKey(userID))
                                pendingCardHandUpdates[userID] = cardHand;
                            else pendingCardHandUpdates.Add(userID, cardHand);
                        break;

                    case PacketType.RIGID_BODY_UPDATE:
                        var rigidObjName = binaryReader.ReadString();
                        var rigidBodyPostion = binaryReader.ReadVector3();
                        var rigidBodyRotation = binaryReader.ReadQuaternion();

                        // TO DO - This isn't really an exciting implementation
                        var rigidObj = getTrackedObject(rigidObjName);
                        if (rigidObj != null)
                        {
                            rigidObj.transform.position = rigidBodyPostion;
                            rigidObj.transform.rotation = rigidBodyRotation;
                            rigidObj.GetComponent<Rigidbody>().isKinematic = false;
                        }
                        break;

                    case PacketType.RESET:
                        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Received - reset from " + userID.ToString());
                        resetGame();
                        break;

                    case PacketType.STATE_UPDATE_REQUEST:
                        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Received - state update request from " + userID.ToString());

                        deckController.SendUpdate();
                        cardHandController.SendUpdate();
                        foreach (var remoteCardHand in remoteCardHands.Values)
                            remoteCardHand.SendUpdate();

                        // Cards without a parent won't be captured otherwise
                        var allCards = GameObject.FindGameObjectsWithTag("PlayingCard");
                        foreach (var card in allCards)
                            if (card.transform.parent == null)
                                OnRigidBodyUpdate(card.GetComponent<Rigidbody>());

                        break;

                    default:
                        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Error, "Unexpected packete received " + type.ToString());
                        Debug.LogError("Unexpected case");
                        break;
                }
            }
        }


        var mouthsAttached = new List<ulong>();
        foreach (var id in pendingMouthAnchorAttach)
            if (remoteAvatars.ContainsKey(id) && remoteAvatars[id].MouthAnchor != null)
            {
                if (remoteAvatars[id].MouthAnchor.GetComponent<VoipAudioSourceHiLevel>() != null)
                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Warning, "Attempted to add second voip audio source for " + id.ToString());
                else
                {
                    var source = remoteAvatars[id].MouthAnchor.AddComponent<VoipAudioSourceHiLevel>();
                    source.senderID = id;
                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Adding voip audio source for " + id.ToString());
                }
                mouthsAttached.Add(id);
            }
        foreach (var id in mouthsAttached) pendingMouthAnchorAttach.Remove(id);


        var handsAttached = new List<ulong>();
        foreach (var id in pendingLeftCardHandAttach)
            if (remoteAvatars.ContainsKey(id) && remoteAvatars[id].HandLeft != null)
            {
                if (remoteAvatars[id].HandLeft.gameObject.GetComponent<CardHandController>() != null)
                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Warning, "Attempted to add second card hand for " + id.ToString());
                else
                {
                    var newCardHand = remoteAvatars[id].HandLeft.gameObject.AddComponent<CardHandController>();
                    newCardHand.canastyController = this;
                    remoteCardHands.Add(id, newCardHand);
                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Adding remote hand object for " + id.ToString());
                }

                if (pendingCardHandUpdates.ContainsKey(id))
                {
                    UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Updating pending card hand for " + id.ToString());
                    remoteCardHands[id].updateCardHand(pendingCardHandUpdates[id]);
                }

                handsAttached.Add(id);
            }
        foreach (var id in handsAttached)
        {
            pendingLeftCardHandAttach.Remove(id);
            pendingCardHandUpdates.Remove(id);
        }


        // Update unity user reporting
        reportingUpdater.Reset();
        this.StartCoroutine(this.reportingUpdater);
    }

    [MonoPInvokeCallback(typeof(Oculus.Platform.CAPI.FilterCallback))]
    public static void MicrophoneFilterCallback(short[] pcmData, System.UIntPtr pcmDataLength, int frequency, int numChannels)
    {
        if (localAvatar != null)
            localAvatar.UpdateVoiceData(pcmData, numChannels);
    }


    public void CloseConnectionsAndLeaveRoom()
    {
        foreach (var key in remoteAvatars.Keys)
            destroyAvatar(key);
        remoteAvatars.Clear();

        pendingMouthAnchorAttach.Clear();
        pendingLeftCardHandAttach.Clear();
        pendingCardHandUpdates.Clear();
        pendingStateUpdateRequest = false;

        Rooms.Leave(roomID);
        userInRoom = false;
        room = null;
        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "User left room");

        foreach (var connection in remoteConnectionStates) { 
            if (!(connection.Value.networkState == ConnectionState.Disconnecting ||
                connection.Value.networkState == ConnectionState.Disconnected))
            {
                Net.Close(connection.Key);
                remoteConnectionStates[connection.Key].networkState = ConnectionState.Disconnecting;
            }

            if (!(connection.Value.voipState == ConnectionState.Disconnecting ||
                connection.Value.voipState == ConnectionState.Disconnected))
            {
                Voip.Stop(connection.Key);
                remoteConnectionStates[connection.Key].voipState = ConnectionState.Disconnecting;
            }
        }
    }

    public void OnApplicationPause(bool pause)
    {
        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Application pause " + pause.ToString());

        if (pause)
            CloseConnectionsAndLeaveRoom();
        else if (localUser != null)  // Make sure we've at least logged in once
            Rooms.Join(roomID, true).OnComplete(OnRoomUpdateCallback);
    }

    public void OnApplicationQuit()
    {
        UnityUserReporting.CurrentClient.LogEvent(UserReportEventLevel.Info, "Application quit");

        CloseConnectionsAndLeaveRoom();
    }

}
