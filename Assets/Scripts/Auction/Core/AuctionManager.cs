using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuctionGame.Auction
{
    [RequireComponent(typeof(PlayerEconomy))]
    public sealed class AuctionManager : MonoBehaviour
    {
        public const float RevealDuration = 2f;
        public const float InitialBiddingTime = 15f;
        public const float AntiSnipingTime = 3f;
        public const float MaximumBiddingDuration = 30f;
        [SerializeField] private int randomSeed = 1729;
        [SerializeField] private bool enableAutomaticAi = true;

        private const double ResultDuration = 2;

        [SerializeField] private AuctionItem[] items = Array.Empty<AuctionItem>();

        public AuctionState State { get; private set; }
        public AuctionItem CurrentItem { get; private set; }
        public int CurrentPrice { get; private set; }
        public string HighestBidderId { get; private set; } = "";
        public PlayerEconomy Economy => economy ? economy : (economy = GetComponent<PlayerEconomy>());
        public float RemainingTime => State == AuctionState.Bidding
            ? (float)Math.Min(bidDeadline - elapsed, MaximumBiddingDuration - elapsed)
            : 0f;
        public double BiddingElapsed => State == AuctionState.Bidding ? elapsed : 0;
        public IReadOnlyList<AuctionResult> Results => results.AsReadOnly();
        public IReadOnlyList<BidRecord> BidHistory => bidHistory.AsReadOnly();

        private readonly List<AuctionResult> results = new();
        private readonly List<BidRecord> bidHistory = new();
        private int itemIndex = -1;
        private PlayerEconomy economy;
        private double elapsed;
        private double bidDeadline;
        private bool processingTime;
        private AIBidder[] opponents;

        public event Action<AuctionItem> OnItemChanged;
        public event Action<BidRecord> OnBidPlaced;
        public event Action<AuctionResult> OnAuctionEnded;
        public event Action<AuctionState> OnStateChanged;
        public event Action OnAuctionPhaseFinished;
        public event Action OnClockChanged;

        public bool StartAuction()
        {
            if (State != AuctionState.Idle)
                return false;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (AuctionItem item in items)
            {
                if (!item || string.IsNullOrWhiteSpace(item.ItemId) || !ids.Add(item.ItemId) ||
                    item.StartingPrice <= 0 || item.TrueValue < 0)
                    return false;
            }

            var random = new System.Random(randomSeed);
            opponents = new[]
            {
                new AIBidder(BidderIds.Collector, random.Next()),
                new AIBidder(BidderIds.Trickster, random.Next()),
                new AIBidder(BidderIds.Analyst, random.Next())
            };

            processingTime = true;
            try
            {
                AdvanceItem();
                return true;
            }
            finally
            {
                processingTime = false;
            }
        }

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (processingTime || State == AuctionState.Idle || State == AuctionState.Finished || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;

            processingTime = true;
            try
            {
                double remainingStep = deltaTime;
                while (State != AuctionState.Finished)
                {
                    double boundary = State switch
                    {
                        AuctionState.ItemReveal => RevealDuration,
                        AuctionState.Bidding => Math.Min(bidDeadline, MaximumBiddingDuration),
                        _ => ResultDuration
                    };

                    // Expiration wins ties. Equal AI decision times retain opponent order.
                    AIBidder nextOpponent = null;
                    if (State == AuctionState.Bidding && enableAutomaticAi)
                    {
                        foreach (AIBidder opponent in opponents)
                        {
                            if (opponent.NextDecision >= boundary)
                                continue;
                            boundary = opponent.NextDecision;
                            nextOpponent = opponent;
                        }
                    }

                    double untilBoundary = boundary - elapsed;
                    if (remainingStep < untilBoundary)
                    {
                        elapsed += remainingStep;
                        break;
                    }

                    remainingStep -= untilBoundary;
                    elapsed = boundary;
                    if (nextOpponent != null)
                    {
                        nextOpponent.Decide(this);
                        continue;
                    }

                    switch (State)
                    {
                        case AuctionState.ItemReveal:
                            bidDeadline = InitialBiddingTime;
                            foreach (AIBidder opponent in opponents)
                                opponent.BeginItem(CurrentItem);
                            SetState(AuctionState.Bidding);
                            break;
                        case AuctionState.Bidding:
                            CloseItem();
                            break;
                        default:
                            AdvanceItem();
                            break;
                    }
                }

                OnClockChanged?.Invoke();
            }
            finally
            {
                processingTime = false;
            }
        }

        public bool TryPlaceBid(string bidderId, int amount)
        {
            if (!CanPlaceBid(bidderId, amount))
                return false;

            CurrentPrice = amount;
            HighestBidderId = bidderId;
            if (bidDeadline - elapsed <= AntiSnipingTime)
                bidDeadline = elapsed + AntiSnipingTime;

            var bid = new BidRecord(CurrentItem.ItemId, bidderId, amount, elapsed);
            bidHistory.Add(bid);
            OnBidPlaced?.Invoke(bid);
            return true;
        }

        public bool CanPlaceBid(string bidderId, int amount)
        {
            if (State != AuctionState.Bidding || elapsed >= bidDeadline ||
                elapsed >= MaximumBiddingDuration || string.IsNullOrEmpty(bidderId) ||
                bidderId == HighestBidderId || !IsLegalAmount(amount))
                return false;

            if (bidderId == BidderIds.Player)
                return Economy.CanAfford(amount);

            return TryGetMaxBid(bidderId, out int maxBid) && amount <= maxBid;
        }

        public bool TryGetMaxBid(string bidderId, out int maxBid)
        {
            if (State == AuctionState.Bidding)
            {
                foreach (AIBidder opponent in opponents)
                {
                    if (opponent.Id != bidderId)
                        continue;

                    maxBid = opponent.MaxBid;
                    return true;
                }
            }

            maxBid = 0;
            return false;
        }

        private bool IsLegalAmount(int amount)
        {
            if (HighestBidderId.Length == 0)
                return amount == CurrentItem.StartingPrice;

            long increment = (long)amount - CurrentPrice;
            return increment is 10 or 25 or 50;
        }

        private void CloseItem()
        {
            if (State != AuctionState.Bidding)
                return;

            var result = new AuctionResult(CurrentItem.ItemId, HighestBidderId, CurrentPrice);
            if (result.winnerId == BidderIds.Player)
                Economy.RecordPurchase(CurrentItem, result.finalPrice);

            results.Add(result);
            SetState(result.sold ? AuctionState.Sold : AuctionState.Unsold);
            OnAuctionEnded?.Invoke(result);
        }

        private void AdvanceItem()
        {
            itemIndex++;
            bidHistory.Clear();
            CurrentPrice = 0;
            HighestBidderId = "";

            if (itemIndex >= items.Length)
            {
                CurrentItem = null;
                SetState(AuctionState.Finished);
                OnAuctionPhaseFinished?.Invoke();
                return;
            }

            CurrentItem = items[itemIndex];
            SetState(AuctionState.ItemReveal);
            OnItemChanged?.Invoke(CurrentItem);
        }

        private void SetState(AuctionState state)
        {
            elapsed = 0;
            State = state;
            OnStateChanged?.Invoke(state);
        }
    }
}

