using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeckController : MonoBehaviour
{
    private List<int> cards = new List<int>(52 * 2);
    private int cardIndex = 0;
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

    public GameObject getNextCard(Vector3 position, Quaternion rotation)
    {
        int card = cards[cardIndex++];
        int suit = (card / 13) % 4;
        int suit_index = card % 13 + 1;

        string suit_name =
            (suit == 0) ? "Club" :
            (suit == 1) ? "Diamond" :
            (suit == 2) ? "Heart" :
            "Spade";

        string prefabPath = "Cards/Blue_PlayingCards_" + suit_name + suit_index.ToString("D2") + "_00";
        var cardObj = Instantiate(Resources.Load<GameObject>(prefabPath));
        cardObj.transform.position = position;
        cardObj.transform.rotation = rotation;
        cardObj.AddComponent<Rigidbody>();
        var collider = cardObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(5.7f, 1.0f, 8.9f);
        collider.center = new Vector3(0, 0, 0);
        cardObj.AddComponent<SimpleGrabbable>();
        cardObj.AddComponent<CardController>();
        return cardObj;
    }

    private void Awake()
    {
        for (int i = 0; i < 52 * 2; i++)
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
