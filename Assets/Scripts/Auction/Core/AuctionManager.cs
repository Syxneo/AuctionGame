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
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private AIPersonality[] bidders;

        public AuctionState State { get; private set; }
        public AuctionItem CurrentItem { get; private set; }
        public IReadOnlyList<AIBidder> Opponents { get; private set; } = Array.Empty<AIBidder>();
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
        private AIBidder[] opponents = Array.Empty<AIBidder>();
        private IReadOnlyList<AuctionItem> lots;
        private ItemDatabase catalog;

        public event Action<AuctionItem> OnItemChanged;
        public event Action<BidRecord> OnBidPlaced;
        public event Action<AuctionResult> OnAuctionEnded;
        public event Action<AuctionState> OnStateChanged;
        public event Action OnAuctionPhaseFinished;
        public event Action OnClockChanged;

        public bool StartAuction() => StartRun(items, itemDatabase);

        public bool StartAuction(IReadOnlyList<AuctionItem> run, ItemDatabase fullCatalog) =>
            fullCatalog && StartRun(run, fullCatalog);

        public bool RestartAuction() => ResetLevel() && StartRun(lots, catalog);

        public bool ResetLevel()
        {
            if (processingTime || State != AuctionState.Finished)
                return false;

            processingTime = true;
            try
            {
                itemIndex = -1;
                results.Clear();
                opponents = Array.Empty<AIBidder>();
                Opponents = Array.Empty<AIBidder>();
                Economy.ResetLevel();
                SetState(AuctionState.Idle);
                return true;
            }
            finally
            {
                processingTime = false;
            }
        }

        private bool StartRun(IReadOnlyList<AuctionItem> run, ItemDatabase fullCatalog)
        {
            if (processingTime || State != AuctionState.Idle || run == null)
                return false;

            var snapshot = new List<AuctionItem>(run.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (AuctionItem item in run)
            {
                if (!item || string.IsNullOrWhiteSpace(item.ItemId) || !ids.Add(item.ItemId) ||
                    item.StartingPrice <= 0 || item.TrueValue < 0)
                    return false;

                snapshot.Add(item);
            }

            AIPersonality[] personalities = bidders ?? LoadDefaultBidders();
            foreach (AIPersonality personality in personalities)
            {
                if (!personality)
                    return false;
            }

            lots = snapshot;
            catalog = fullCatalog;

            var random = new System.Random(randomSeed);
            opponents = new AIBidder[personalities.Length];
            for (int index = 0; index < personalities.Length; index++)
                opponents[index] = new AIBidder(BidderIds.ForOpponentIndex(index), personalities[index], random.Next());
            Opponents = Array.AsReadOnly(opponents);

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

        private void Reset() => bidders = LoadDefaultBidders();

        private static AIPersonality[] LoadDefaultBidders() => new[]
        {
            Resources.Load<AIPersonality>("Auction/Personalities/Bidder1"),
            Resources.Load<AIPersonality>("Auction/Personalities/Bidder2"),
            Resources.Load<AIPersonality>("Auction/Personalities/Bidder3")
        };

        public string GetBidderDisplayName(string bidderId) =>
            FindOpponent(bidderId)?.Personality.Name ?? BidderIds.DisplayName(bidderId);

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
                                opponent.BeginItem(CurrentItem, catalog);
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

            string comment = bidderId == BidderIds.Player ? "" : FindOpponent(bidderId).GetBidComment();
            var bid = new BidRecord(CurrentItem.ItemId, bidderId, amount, elapsed, comment);
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

            AIBidder opponent = FindOpponent(bidderId);
            return opponent != null && amount <= opponent.EffectiveMaxBid;
        }

        public bool TryGetMaxBid(string bidderId, out int maxBid)
        {
            AIBidder opponent = State == AuctionState.Bidding ? FindOpponent(bidderId) : null;
            maxBid = opponent?.MaxBid ?? 0;
            return opponent != null;
        }

        private AIBidder FindOpponent(string id)
        {
            foreach (AIBidder opponent in opponents)
            {
                if (opponent.Id == id)
                    return opponent;
            }

            return null;
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
            else if (result.sold)
                FindOpponent(result.winnerId).RecordWin(CurrentItem, result.finalPrice);

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

            if (itemIndex >= lots.Count)
            {
                CurrentItem = null;
                SetState(AuctionState.Finished);
                OnAuctionPhaseFinished?.Invoke();
                return;
            }

            CurrentItem = lots[itemIndex];
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

