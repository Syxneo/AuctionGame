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
        [SerializeField] private AuctionItem[] evaluationCatalog = Array.Empty<AuctionItem>();
        [SerializeField] private AIBidderProfile collector = new(0.65f, 0.90f, "Historical", "Luxury", "Collectible");
        [SerializeField] private AIBidderProfile trickster = AIBidderProfile.CreateTrickster();
        [SerializeField] private AIBidderProfile analyst = new(0.90f, 0.40f, "Technology", "Mechanical", "Coins");

        public AuctionState State { get; private set; }
        public AuctionItem CurrentItem => currentLot?.Definition;
        public AuctionItemInstance CurrentItemVariant => currentLot?.Variant;
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
        private AIBidder[] opponents;
        private IReadOnlyList<AIEvaluationItem> lots;
        private IReadOnlyList<AIEvaluationItem> catalog;
        private AIEvaluationItem currentLot;

        public event Action<AuctionItem> OnItemChanged;
        public event Action<BidRecord> OnBidPlaced;
        public event Action<AuctionResult> OnAuctionEnded;
        public event Action<AuctionState> OnStateChanged;
        public event Action OnAuctionPhaseFinished;
        public event Action OnClockChanged;

        public bool StartAuction()
        {
            if (processingTime || State != AuctionState.Idle)
                return false;

            var run = new List<AIEvaluationItem>(items.Length);
            foreach (AuctionItem item in items)
            {
                if (!item)
                    return false;
                run.Add(new AIEvaluationItem(item));
            }

            return StartRun(run, evaluationCatalog);
        }

        public bool StartAuction(IReadOnlyList<AuctionItemInstance> run, IReadOnlyList<AuctionItem> fullCatalog)
        {
            if (processingTime || State != AuctionState.Idle || run == null || fullCatalog == null)
                return false;

            var adapted = new List<AIEvaluationItem>(run.Count);
            foreach (AuctionItemInstance item in run)
            {
                if (item == null || !item.Definition)
                    return false;
                adapted.Add(new AIEvaluationItem(item.Definition, item));
            }

            return StartRun(adapted, fullCatalog);
        }

        public bool ResetLevel()
        {
            if (processingTime || State != AuctionState.Finished)
                return false;

            processingTime = true;
            try
            {
                itemIndex = -1;
                results.Clear();
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

        private bool StartRun(IReadOnlyList<AIEvaluationItem> run, IReadOnlyList<AuctionItem> fullCatalog)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (AIEvaluationItem item in run)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) ||
                    item.Definition.StartingPrice <= 0 || item.TrueValue < 0)
                    return false;
            }

            var definitions = new SortedDictionary<string, AuctionItem>(StringComparer.Ordinal);
            foreach (AuctionItem definition in fullCatalog)
            {
                if (!definition || string.IsNullOrWhiteSpace(definition.ItemId) ||
                    (!definition.HasVariantPrices && definition.TrueValue < 0) ||
                    definitions.ContainsKey(definition.ItemId))
                    return false;
                definitions.Add(definition.ItemId, definition);
            }

            foreach (AIEvaluationItem item in run)
                definitions.TryAdd(item.Id, item.Definition);

            var variants = new List<AIEvaluationItem>();
            foreach (AuctionItem definition in definitions.Values)
                AIEvaluationItem.AddDefinitionVariants(variants, definition);
            lots = run;
            catalog = variants;

            var random = new System.Random(randomSeed);
            opponents = new[]
            {
                new AIBidder(BidderIds.Collector, AIPersonality.Collector, collector, random.Next()),
                new AIBidder(BidderIds.Trickster, AIPersonality.Trickster, trickster, random.Next()),
                new AIBidder(BidderIds.Analyst, AIPersonality.Analyst, analyst, random.Next())
            };
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
                                opponent.BeginItem(currentLot, catalog);
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
                Economy.RecordPurchase(CurrentItem, CurrentItemVariant, result.finalPrice);
            else if (result.sold)
                FindOpponent(result.winnerId).RecordWin(currentLot, result.finalPrice);

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
                currentLot = null;
                SetState(AuctionState.Finished);
                OnAuctionPhaseFinished?.Invoke();
                return;
            }

            currentLot = lots[itemIndex];
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

