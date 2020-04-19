using AOT;
using Oculus.Platform;
using Oculus.Platform.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

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

    public OvrAvatar remoteAvatarPrefab = null;

    private string appID = "2850434391711779";
    private ulong roomID = 755036931694010;
    private User localUser = null;
    private Room room = null;
    private Dictionary<ulong, OvrAvatar> remoteAvatars = new Dictionary<ulong, OvrAvatar>();
    private List<ulong> pendingMouthAnchorAttach = new List<ulong>();
    private List<ulong> pendingLeftCardHandAttach = new List<ulong>();
    private uint avatarSequence = 0;
    private Dictionary<string, GameObject> trackedObjects = new Dictionary<string, GameObject>();
    private Dictionary<ulong, CardHandController> remoteCardHands = new Dictionary<ulong, CardHandController>();
    private bool userInRoom = false;

    public enum PacketType : byte
    {
        AVATAR_UPDATE = 1,
        TRACKED_OBJECT_UPDATE,
        CARD_HAND_UPDATE,
        DECK_UPDATE,
        RIGID_BODY_UPDATE
    }

    private bool ShouldOwnConnection(ulong userID)
    {
        return userID < localUser.ID;
    }

    // When someone joins or leaves, do add or remove
    private void OnRoomUpdateCallback(Message<Room> message)
    {
        if (message.IsError) Debug.LogError("Connection state error - " + message.GetError().Message);
        else
        {
            room = message.GetRoom();
            foreach (var user in room.UsersOptional)
            {
                if (user.ID == localUser.ID)
                {
                    if (!userInRoom)
                    {
                        userInRoom = true;
                        Net.AcceptForCurrentRoom();
                    }
                }
                else if (!Net.IsConnected(user.ID) && ShouldOwnConnection(user.ID))
                {
                    Net.Connect(user.ID);
                    Voip.Start(user.ID);
                }
            }
        }
    }

    OvrAvatar createAvatar(ulong userID)
    {
        var avatar = Instantiate(remoteAvatarPrefab);
        avatar.oculusUserID = userID.ToString();
        avatar.UseSDKPackets = true;
        avatar.CanOwnMicrophone = false;
        pendingLeftCardHandAttach.Add(userID);
        return avatar;
    }

    ulong destroyAvatar(ulong userID)
    {
        if (remoteAvatars[userID].MouthAnchor != null)
            Destroy(remoteAvatars[userID].MouthAnchor.GetComponent<VoipAudioSourceHiLevel>());

        if (remoteCardHands.ContainsKey(userID))
        {
            Destroy(remoteCardHands[userID]);
            remoteCardHands.Remove(userID);
        }

        Destroy(remoteAvatars[userID]);

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
        if (message.IsError) Debug.LogError("Connection state error - " + message);
        else
        {
            NetworkingPeer peer = message.GetNetworkingPeer();
            switch (peer.State)
            {
                case PeerConnectionState.Connected:
                   if (!remoteAvatars.ContainsKey(peer.ID))
                        remoteAvatars.Add(peer.ID, createAvatar(peer.ID));
                    break;
                case PeerConnectionState.Timeout:
                    if (IsUserInRoom(peer.ID) && ShouldOwnConnection(peer.ID))
                        Net.Connect(peer.ID);
                    break;
                case PeerConnectionState.Closed:
                    if (remoteAvatars.ContainsKey(peer.ID))
                        remoteAvatars.Remove(destroyAvatar(peer.ID));
                    break;
                default:
                    Debug.LogError("Unexpected connection state");
                    break;
            }
        }
    }

    void OnVoipStateChangedCallback(Message<NetworkingPeer> message)
    {
        if (message.IsError) Debug.LogError("Error on voip update " + message.ToString());
        else
        {
            NetworkingPeer peer = message.GetNetworkingPeer();
            switch (peer.State)
            {
                case PeerConnectionState.Connected:
                    pendingMouthAnchorAttach.Add(peer.ID); // Annoyingly mouth anchor isn't populated until first update
                    break;
                case PeerConnectionState.Timeout:
                    if (IsUserInRoom(peer.ID) && ShouldOwnConnection(peer.ID))
                        Voip.Start(peer.ID);
                    break;
                case PeerConnectionState.Closed:
                    if (remoteAvatars.ContainsKey(peer.ID) &&
                        (remoteAvatars[peer.ID].MouthAnchor != null))
                        Destroy(remoteAvatars[peer.ID].MouthAnchor.GetComponent<VoipAudioSourceHiLevel>());
                    break;
                default:
                    Debug.LogError("Unexpected connection state");
                    break;
            }
        }
    }


    // No auto accept for VOIP?
    void OnVoipConnectRequestCallback(Message<NetworkingPeer> msg)
    {
        if (msg.IsError) Debug.LogError("Error on voip connect requeest " + msg.ToString());
        else
        {
            var peer = msg.GetNetworkingPeer();
            Voip.Accept(peer.ID);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        rightHandAnchor = GameObject.Find("RightHandAnchor");
        leftHandAnchor = GameObject.Find("LeftHandAnchor");

        rightHandGrabber = rightHandAnchor.GetComponent<SimpleGrabber>();

        var mainDeck = GameObject.Find("MainDeck");
        deckController = mainDeck.GetComponent<DeckController>();
        deckCollider = mainDeck.GetComponent<Collider>();

        cardHandController = leftHandAnchor.GetComponent<CardHandController>();

        Rooms.SetUpdateNotificationCallback(OnRoomUpdateCallback);
        Net.SetConnectionStateChangedCallback(OnConnectionStateChangedCallback);
        Voip.SetVoipConnectRequestCallback(OnVoipConnectRequestCallback);
        Voip.SetVoipStateChangeCallback(OnVoipStateChangedCallback);
        Voip.SetMicrophoneFilterCallback(MicrophoneFilterCallback);

        localAvatar = GameObject.Find("LocalAvatar").GetComponent<OvrAvatar>();
        localAvatar.CanOwnMicrophone = false;
        localAvatar.UseSDKPackets = true;
        localAvatar.RecordPackets = true;
        localAvatar.PacketRecorded += OnLocalAvatarPacketRecorded;

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
                                Rooms.Join(roomID, true).OnComplete(OnRoomUpdateCallback);
                            }
                        });
                    }
                });
            }
        });
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
            Net.SendPacketToCurrentRoom(((MemoryStream)binaryWriter.BaseStream).ToArray(), SendPolicy.Unreliable);
        }
    }

    public void OnDeckUpdate(List<byte> cards, int cardIndex)
    {
        if (!userInRoom)
        {
            Debug.LogError("Attempted to update deck while not in room");
            return;
        }

        using (BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(256)))
        {
            binaryWriter.Write((byte)PacketType.DECK_UPDATE);
            binaryWriter.Write(localUser.ID);
            binaryWriter.Write((byte)cards.Count);
            binaryWriter.Write((byte)cardIndex);
            binaryWriter.Write(cards.ToArray());

            Net.SendPacketToCurrentRoom(((MemoryStream)binaryWriter.BaseStream).ToArray(), SendPolicy.Reliable);
        }
    }


    public void OnTrackedObjectUpdate(GameObject obj)
    {
        if (!userInRoom) return;

        using (BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(256)))
        {
            binaryWriter.Write((byte)PacketType.TRACKED_OBJECT_UPDATE);
            binaryWriter.Write(localUser.ID);
            binaryWriter.Write(obj.name);
            binaryWriter.Write(obj.transform.position);
            binaryWriter.Write(obj.transform.rotation);

            Net.SendPacketToCurrentRoom(((MemoryStream)binaryWriter.BaseStream).ToArray(), SendPolicy.Unreliable);
        }
    }

    public void OnCardHandUpdate(LinkedList<GameObject> cards)
    {
        if (!userInRoom) return;

        List<byte> cardIds = new List<byte>(10);
        foreach (var card in cards)
            cardIds.Add(byte.Parse(card.name.Substring(4)));

        using (BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(256)))
        {
            binaryWriter.Write((byte)PacketType.CARD_HAND_UPDATE);
            binaryWriter.Write(localUser.ID);
            binaryWriter.Write((byte)cardIds.Count);
            binaryWriter.Write(cardIds.ToArray());

            Net.SendPacketToCurrentRoom(((MemoryStream)binaryWriter.BaseStream).ToArray(), SendPolicy.Unreliable);
        }
    }

    public void OnRigidBodyUpdate(Rigidbody rigidBody)
    {
        using (BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream(256)))
        {
            binaryWriter.Write((byte)PacketType.RIGID_BODY_UPDATE);
            binaryWriter.Write(localUser.ID);
            binaryWriter.Write(rigidBody.gameObject.name);
            binaryWriter.Write(rigidBody.position);
            binaryWriter.Write(rigidBody.rotation);
            Net.SendPacketToCurrentRoom(((MemoryStream)binaryWriter.BaseStream).ToArray(), SendPolicy.Unreliable);
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


    // Update is called once per frame
    void Update()
    {
        OVRInput.Update();

        //if (testFrame == 0)
        //{
        //    var card = deckController.getNextCard(new Vector3(-0.2f, 2, 0), new Quaternion());
        //    bool test = cardHandController.isCardInHand(card);
        //    cardHandController.AddCard(card);
        //}

        //testFrame -= 1;

        if (OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) > 0.55)
        {
            if ((rightHandHeld == null) && (rightHandGrabber.otherCollider != null))
            {
                if (rightHandGrabber.otherCollider == deckCollider)
                    rightHandHeld = deckController.getNextCard(
                        deckController.transform.position,
                        deckController.transform.rotation);
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

        if (OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) < 0.35)
        {
            if (rightHandHeld != null)
            {
                rightHandHeld.transform.parent = null;
                rightHandHeld.GetComponent<Rigidbody>().isKinematic = false;
                rightHandHeld.GetComponent<SimpleGrabbable>().isGrabbed = false;

                if ((rightHandHeld.GetComponent<CardController>() != null) && cardHandController.isHighlighting)
                    cardHandController.AddCard(rightHandHeld);

                OnRigidBodyUpdate(rightHandHeld.GetComponent<Rigidbody>());

                rightHandHeld = null;
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
                        deckController.updateDeck(new List<byte>(cards), cardIndex);
                        break;

                    case PacketType.CARD_HAND_UPDATE:
                        var cardHandCount = binaryReader.ReadByte();
                        var cardHand = binaryReader.ReadBytes(cardHandCount);
                        remoteCardHands[userID].updateCardHand(cardHand);
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
                }
            }
        }

        var mouthsAttached = new List<ulong>();
        foreach (var id in pendingMouthAnchorAttach)
            if (remoteAvatars.ContainsKey(id) && remoteAvatars[id].MouthAnchor != null)
            {
                var source = remoteAvatars[id].MouthAnchor.AddComponent<VoipAudioSourceHiLevel>();
                source.senderID = id;
                mouthsAttached.Add(id);
            }
        foreach (var id in mouthsAttached) pendingMouthAnchorAttach.Remove(id);

        var handsAttached = new List<ulong>();
        foreach (var id in pendingLeftCardHandAttach)
            if (remoteAvatars.ContainsKey(id) && remoteAvatars[id].HandLeft != null)
            {
                remoteCardHands.Add(id, remoteAvatars[id].HandLeft.gameObject.AddComponent<CardHandController>());
                handsAttached.Add(id);
            }
        foreach (var id in handsAttached) pendingLeftCardHandAttach.Remove(id);
    }

    private void FixedUpdate()
    {
        OVRInput.FixedUpdate();
    }

    [MonoPInvokeCallback(typeof(Oculus.Platform.CAPI.FilterCallback))]
    public static void MicrophoneFilterCallback(short[] pcmData, System.UIntPtr pcmDataLength, int frequency, int numChannels)
    {
        localAvatar.UpdateVoiceData(pcmData, numChannels);
    }

    public void CloseConnectionsAndLeaveRoom()
    {
        foreach (var key in remoteAvatars.Keys)
            destroyAvatar(key);
        remoteAvatars.Clear();

        if (room == null) return;
        foreach (var user in room.UsersOptional)
        {
            Net.Close(user.ID);
            Voip.Stop(user.ID);
        }
        Rooms.Leave(roomID);
        room = null;
    }

    public void OnApplicationPause(bool pause)
    {
        if (pause)
            CloseConnectionsAndLeaveRoom();
        else if (localUser != null)  // Make sure we've at least logged in once
            Rooms.Join(roomID, true).OnComplete(OnRoomUpdateCallback);
    }

    public void OnApplicationQuit()
    {
        CloseConnectionsAndLeaveRoom();
    }
}
