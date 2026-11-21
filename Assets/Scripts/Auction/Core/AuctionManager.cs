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
        private const float DefaultReactionDelay = 1.5f;
        [SerializeField] private int randomSeed = 1729;
        [SerializeField] private bool enableAutomaticAi = true;
        [SerializeField, Min(0.01f)] private float aiReactionDelay = DefaultReactionDelay;
        [SerializeField] private bool enableAuctionDebugLog;
        [SerializeField] private bool enableAiDialogueLog = true;
        [SerializeField, Min(0)] private float aiDialogueCooldown = 4;
        [SerializeField] private TextAsset aiDialogueLibrary;
        [SerializeField, Range(0, 100)] private int smallBidIntimidation = 15;
        [SerializeField, Range(0, 100)] private int mediumBidIntimidation = 35;
        [SerializeField, Range(0, 100)] private int highBidIntimidation = 65;

        private const int SmallIncrement = 10;
        private const int MediumIncrement = 25;
        private const int HighIncrement = 50;

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
            ? (float)Math.Max(0, Math.Min(bidDeadline, MaximumBiddingDuration) - elapsed)
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
        private long auctionTick;
        private long bidSequence;
        private bool publishingBid;
        internal bool DebugLogEnabled => enableAuctionDebugLog;
        private AIBidder[] opponents = Array.Empty<AIBidder>();
        private AIBidder[] reactionOrder = Array.Empty<AIBidder>();
        private System.Random reactionOrderRandom;
        private readonly List<AIBidder> reactionQueue = new();
        private int reactionIndex;
        private double nextReactionAt;
        private IReadOnlyList<AuctionItem> lots;
        private AuctionDialogue dialogue;
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
                ClearReactions();
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
            auctionTick = 0;
            bidSequence = 0;
            dialogue = enableAiDialogueLog
                ? new AuctionDialogue(this, aiDialogueLibrary ? aiDialogueLibrary :
                    Resources.Load<TextAsset>("Auction/AiDialogue"), randomSeed, aiDialogueCooldown)
                : null;

            var random = new System.Random(randomSeed);
            opponents = new AIBidder[personalities.Length];
            for (int index = 0; index < personalities.Length; index++)
                opponents[index] = new AIBidder(BidderIds.ForOpponentIndex(index), personalities[index], random.Next());
            reactionOrder = new AIBidder[opponents.Length];
            reactionOrderRandom = new System.Random(random.Next());
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

        // UI clicks are dispatched in Update. Advance the AI clock after those inputs.
        private void LateUpdate() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (processingTime || State == AuctionState.Idle || State == AuctionState.Finished || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return;

            auctionTick++;
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

                    // A bid or reaction due exactly at expiration does not execute.
                    bool runReaction = State == AuctionState.Bidding && HasPendingReaction && nextReactionAt < boundary;
                    if (runReaction)
                        boundary = nextReactionAt;

                    double untilBoundary = boundary - elapsed;
                    if (remainingStep < untilBoundary)
                    {
                        elapsed += remainingStep;
                        break;
                    }

                    remainingStep -= untilBoundary;
                    elapsed = boundary;
                    if (runReaction)
                    {
                        AIBidder candidate = reactionQueue[reactionIndex++];
                        nextReactionAt = elapsed + ReactionDelay;
                        candidate.Decide(this);
                        continue;
                    }

                    switch (State)
                    {
                        case AuctionState.ItemReveal:
                            bidDeadline = InitialBiddingTime;
                            if (enableAuctionDebugLog)
                                LogAuctionDebug($"Bidding opened | true value=${CurrentItem.TrueValue} | opening=${CurrentItem.StartingPrice}");
                            foreach (AIBidder opponent in opponents)
                            {
                                opponent.BeginItem(CurrentItem);
                                if (enableAuctionDebugLog)
                                    opponent.LogEvaluation(this);
                            }
                            dialogue?.OpenLot(itemIndex + 1, opponents);
                            ShuffleReactionOrder();
                            StartReactionQueue("", 0);
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
            if (publishingBid || !CanPlaceBid(bidderId, amount))
                return false;

            int previousPrice = CurrentPrice;
            int increment = HighestBidderId.Length == 0 ? 0 : amount - previousPrice;
            CurrentPrice = amount;
            HighestBidderId = bidderId;
            if (bidDeadline - elapsed <= AntiSnipingTime)
                bidDeadline = elapsed + AntiSnipingTime;

            var bid = new BidRecord(CurrentItem.ItemId, bidderId, amount, elapsed, ++bidSequence, increment);
            bidHistory.Add(bid);
            StartReactionQueue(bidderId, elapsed);
            if (enableAuctionDebugLog)
                LogAuctionDebug($"Bid accepted | {GetBidderDisplayName(bidderId)} | ${previousPrice} → ${amount} | remaining={RemainingTime:F3}s");
            bool wasProcessing = processingTime;
            processingTime = true;
            publishingBid = true;
            try
            {
                dialogue?.AcceptedBid(bid, opponents);
                OnBidPlaced?.Invoke(bid);
            }
            finally
            {
                publishingBid = false;
                processingTime = wasProcessing;
            }

            return true;
        }

        internal void LogAuctionDebug(string message) =>
            Debug.Log($"[Auction Debug] lot {itemIndex + 1} ({CurrentItem.ItemId}) | tick {auctionTick} | frame {Time.frameCount} | t={BiddingElapsed:F3}s | {message}", this);

        internal void NotifyChickenOut(AIBidder bidder) => dialogue?.ChickenOut(bidder, BiddingElapsed);

        public bool CanPlaceBid(string bidderId, int amount)
        {
            if (State != AuctionState.Bidding || elapsed >= bidDeadline || elapsed >= MaximumBiddingDuration ||
                string.IsNullOrEmpty(bidderId) ||
                bidderId == HighestBidderId || !IsLegalAmount(amount))
                return false;

            if (bidderId == BidderIds.Player)
                return Economy.CanAfford(amount);

            AIBidder opponent = FindOpponent(bidderId);
            return opponent != null && amount <= opponent.BidLimit;
        }

        public bool TryGetMaxBid(string bidderId, out int maxBid)
        {
            AIBidder opponent = State == AuctionState.Bidding ? FindOpponent(bidderId) : null;
            maxBid = opponent?.BidLimit ?? 0;
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
            return increment is SmallIncrement or MediumIncrement or HighIncrement;
        }

        internal int GetNextBidAmount()
        {
            if (HighestBidderId.Length == 0)
                return CurrentItem.StartingPrice;

            return CurrentPrice <= int.MaxValue - SmallIncrement ? CurrentPrice + SmallIncrement : 0;
        }

        internal BidRecord FindIntimidationBid(string bidderId, long afterSequence)
        {
            for (int index = bidHistory.Count - 1; index >= 0; index--)
            {
                BidRecord bid = bidHistory[index];
                if (bid.sequence <= afterSequence)
                    break;
                if (bid.bidderId != bidderId && bid.increment != 0)
                    return bid;
            }

            return null;
        }

        internal double GetIntimidationChance(int increment) => Math.Clamp(increment switch
        {
            SmallIncrement => smallBidIntimidation,
            MediumIncrement => mediumBidIntimidation,
            HighIncrement => highBidIntimidation,
            _ => 0
        }, 0, 100) / 100d;

        private bool HasPendingReaction => enableAutomaticAi && reactionIndex < reactionQueue.Count;
        private double ReactionDelay => aiReactionDelay > 0 && !float.IsInfinity(aiReactionDelay)
            ? aiReactionDelay : DefaultReactionDelay;

        private void ShuffleReactionOrder()
        {
            Array.Copy(opponents, reactionOrder, opponents.Length);
            for (int index = reactionOrder.Length - 1; index > 0; index--)
            {
                int swap = reactionOrderRandom.Next(index + 1);
                (reactionOrder[index], reactionOrder[swap]) = (reactionOrder[swap], reactionOrder[index]);
            }
        }

        private void StartReactionQueue(string author, double at)
        {
            // Replacing the only queue invalidates all earlier entries; there are no pending callbacks.
            reactionQueue.Clear();
            reactionIndex = 0;
            nextReactionAt = at + ReactionDelay;
            if (!enableAutomaticAi)
                return;

            foreach (AIBidder bidder in reactionOrder)
            {
                if (bidder.Id == author || bidder.HasChickenedOut)
                    continue;
                if (author.Length == 0 && bidder.BidLimit < CurrentItem.StartingPrice)
                    continue;
                reactionQueue.Add(bidder);
            }
        }

        private void ClearReactions()
        {
            reactionQueue.Clear();
            reactionIndex = 0;
            nextReactionAt = 0;
        }

        private void CloseItem()
        {
            if (State != AuctionState.Bidding)
                return;

            ClearReactions();
            var result = new AuctionResult(CurrentItem.ItemId, HighestBidderId, CurrentPrice);
            AIBidder winner = result.sold ? FindOpponent(result.winnerId) : null;
            if (result.winnerId == BidderIds.Player)
                Economy.RecordPurchase(CurrentItem, result.finalPrice);
            else
                winner?.RecordWin(CurrentItem, result.finalPrice);

            results.Add(result);
            double settledAt = elapsed;
            SetState(result.sold ? AuctionState.Sold : AuctionState.Unsold);
            dialogue?.EndLot(result, winner, settledAt);
            OnAuctionEnded?.Invoke(result);
        }

        private void AdvanceItem()
        {
            ClearReactions();
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

