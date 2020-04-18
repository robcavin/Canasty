using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CanastyController : MonoBehaviour
{
    GameObject mainDeck;
    GameObject rightHandAnchor;
    GameObject leftHandAnchor;

    SimpleGrabber rightHandGrabber;

    DeckController deckController;
    Collider deckCollider;

    CardHandController cardHandController;

    GameObject rightHandHeld;

    int testFrame = 10;

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
    }

    // Update is called once per frame
    void Update()
    {
        OVRInput.Update();

        if (testFrame == 0)
        {
            var card = deckController.getNextCard(new Vector3(-0.2f, 2, 0), new Quaternion());
            bool test = cardHandController.isCardInHand(card);
            cardHandController.AddCard(card);
        }

        testFrame -= 1;

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
            }
        }

        if (OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) < 0.35)
        {
            if (rightHandHeld != null)
            {
                rightHandHeld.transform.parent = null;
                rightHandHeld.GetComponent<Rigidbody>().isKinematic = false;

                if ((rightHandHeld.GetComponent<CardController>() != null) && cardHandController.isHighlighting)
                    cardHandController.AddCard(rightHandHeld);
               
                rightHandHeld = null;
            }
        }
    }

    private void FixedUpdate()
    {
        OVRInput.FixedUpdate();
    }
}
