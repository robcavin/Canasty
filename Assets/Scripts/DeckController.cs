using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeckController : MonoBehaviour
{
    public CanastyController canastyController = null;

    private List<byte> cards = new List<byte>(52 * 2);
    private byte cardIndex = 0;
    private System.Random rng = new System.Random();

    public void Shuffle<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    public void updateDeck(List<byte> cards, byte cardIndex)
    {
        this.cards = cards;
        this.cardIndex = cardIndex;
    }

    public static GameObject instantiateCard(int index)
    {
        int suit = (index / 13) % 4;
        int suit_index = index % 13 + 1;

        string suit_name =
            (suit == 0) ? "Club" :
            (suit == 1) ? "Diamond" :
            (suit == 2) ? "Heart" :
            "Spade";

        string prefabPath = "Cards/Blue_PlayingCards_" + suit_name + suit_index.ToString("D2") + "_00";
        var cardObj = Instantiate(Resources.Load<GameObject>(prefabPath));
        cardObj.name = "Card" + index.ToString();

        // Physics
        var rigidBody = cardObj.AddComponent<Rigidbody>();
        rigidBody.isKinematic = true;

        var collider = cardObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(5.7f, 0.5f, 8.9f);
        collider.center = new Vector3(0, 0, 0);

        // Controllers
        var grabbable = cardObj.AddComponent<SimpleGrabbable>();
        var controller = cardObj.AddComponent<CardController>();
        controller.grabbable = grabbable;

        return cardObj;
    }

    public GameObject getNextCard(Vector3 position, Quaternion rotation)
    {
        int index = cards[cardIndex++];
        canastyController.OnDeckUpdate(cards, cardIndex);

        var cardObj = instantiateCard(index);

        cardObj.transform.position = position;
        cardObj.transform.rotation = rotation;
        cardObj.transform.Rotate(0, 0, 180);  // Turn card face down
        cardObj.transform.Translate(0, -0.0134f, 0);
        return cardObj;
    }

    private void Awake()
    {
        for (byte i = 0; i < 52 * 2; i++)
            cards.Add(i);
        Shuffle(cards);
        cardIndex = 0;

    }

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
