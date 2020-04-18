using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CardHandController : MonoBehaviour
{
    public GameObject highlighter = null;
    public bool isHighlighting { get; private set; } = false;

    private float highlightStrength = 0;
    private float highlightAngle = 0;

    private LinkedList<GameObject> cards = new LinkedList<GameObject>();

    public bool isCardInHand(GameObject card)
    {
        return cards.Contains(card);
    }

    bool first = true;

    public float spreadFunc(float x, float mean, float sigma)
    {
        var diff = x - mean;
        return diff * Mathf.Exp(-0.5f * Mathf.Pow(((x - mean) / sigma), 2));
    }

    public float hightlightFunc(float x, float mean, float sigma)
    {
        return Mathf.Exp(-0.5f * Mathf.Pow(((x - mean) / sigma), 2));
    }


    public void updateCardRotations(float highlight)
    {
        if (cards.Count == 0) return;

        float angularSeparation = Mathf.Min(10.0f, 60.0f / (cards.Count - 1));
        float angularRange = angularSeparation * (cards.Count - 1);

        float cardAngle = -angularRange / 2;
        var z_delta = new Vector3(0.0f, 0.0f, 0.001f);
        var z_offset = new Vector3(0.0f, 0.0f, 0.0f);

        foreach (var card in cards)
        {
            var angleSpread = highlightStrength * spreadFunc(cardAngle, highlight, 10);
            var accentHeight = 0.015f * highlightStrength * hightlightFunc(cardAngle, highlight, 10);

            var cardAngleWithSpread = cardAngle + Mathf.Clamp(angleSpread,-10.0f,10.0f);
            card.transform.localRotation = Quaternion.Euler(-90, 0, 0) * Quaternion.Euler(0, -cardAngleWithSpread, 0);
            var position = Quaternion.Euler(0, 0, cardAngleWithSpread) * new Vector3(0, 0.01f + 0.3f * accentHeight, 0);
            card.transform.localPosition = position * 10.0f - new Vector3(0, 0.05f, 0) + z_offset;

            z_offset += z_delta;
            cardAngle += angularSeparation;
        }
    }

    public void AddCard(GameObject card)
    {
        card.GetComponent<Rigidbody>().isKinematic = true;
        card.transform.position = transform.position;
        card.transform.rotation = transform.rotation;
        card.transform.parent = transform;

        if (isHighlighting && (cards.Count > 0))
        {
            float angularSeparation = Mathf.Min(10.0f, 60.0f / (cards.Count-1));
            float angularRange = angularSeparation * (cards.Count - 1);
            float cardAngle = -angularRange / 2;

            var cardIter = cards.First;
            while ((cardIter.Next != null) &&
                ((cardAngle + angularSeparation) < highlightAngle))
            {
                cardAngle += angularSeparation;
                cardIter = cardIter.Next;
            }
            cards.AddAfter(cardIter, card);
        }
        else 
            cards.AddLast(card);

        updateCardRotations(highlightAngle);
    }

    public void ReleaseCard(GameObject card)
    {
        card.GetComponent<Rigidbody>().isKinematic = false;
        card.transform.parent = null;
        cards.Remove(card);

        updateCardRotations(highlightAngle);
    }


    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        if (first)
        {
            var deckController = GameObject.Find("MainDeck").GetComponent<DeckController>();
            for (int i = 0; i < 10; i++)
                AddCard(deckController.getNextCard(transform.position, transform.rotation));
            first = false;
        }

        highlightStrength = 0;
        highlightAngle = 0;
        isHighlighting = false;

        if (highlighter != null)
        {
            var delta = highlighter.transform.position - transform.position;
            if (delta.magnitude < 0.2)
            {
                var x = Vector3.Dot(delta, transform.right);
                var y = Vector3.Dot(delta, transform.up);

                var angle = Mathf.Atan2(y + 0.05f, x) * 180 / Mathf.PI - 90;
                var length = new Vector2(x, y).magnitude;

                if (angle > -30 && angle < 30)
                {
                    highlightAngle = angle;
                    highlightStrength = Mathf.Max(0, 1 - Mathf.Abs(length - 0.1f));
                    isHighlighting = true;
                }
            }
        }

        updateCardRotations(highlightAngle);
    }
}
