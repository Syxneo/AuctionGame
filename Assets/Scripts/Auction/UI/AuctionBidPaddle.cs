using System;
using UnityEngine;

namespace AuctionGame.Auction
{
    public sealed class AuctionBidPaddle : MonoBehaviour
    {
        [Serializable]
        private sealed class BidderHand
        {
            public string bidderId;
            public SpriteRenderer character;
            public SpriteRenderer Renderer { get; set; }
        }

        [SerializeField] private AuctionManager auction;
        [SerializeField] private BidderHand[] bidderHands = Array.Empty<BidderHand>();

        private void Awake()
        {
            foreach (BidderHand binding in bidderHands)
            {
                if (!binding.character)
                    continue;

                var hand = binding.character.transform.Find("Raised Bid Hand");
                if (hand)
                    binding.Renderer = hand.GetComponent<SpriteRenderer>();
            }
        }

        private void OnEnable()
        {
            if (auction)
            {
                auction.OnBidPlaced += BidPlaced;
                auction.OnStateChanged += StateChanged;
            }
            Refresh();
        }

        private void OnDisable()
        {
            if (auction)
            {
                auction.OnBidPlaced -= BidPlaced;
                auction.OnStateChanged -= StateChanged;
            }
            SetLeadingHand(null);
        }

        private void BidPlaced(BidRecord _) => Refresh();
        private void StateChanged(AuctionState _) => Refresh();

        private void Refresh() => SetLeadingHand(auction && auction.State == AuctionState.Bidding
            ? auction.HighestBidderId : null);

        private void SetLeadingHand(string bidderId)
        {
            foreach (BidderHand binding in bidderHands)
            {
                if (binding.Renderer)
                    binding.Renderer.enabled = binding.bidderId == bidderId;
            }
        }
    }
}
