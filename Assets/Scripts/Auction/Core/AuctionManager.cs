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
        private const float MinimumAiInteractionInterval = 2f;
        private const double NormalBidReactionDelay = 2d;
        private const double FinalBidReactionDelay = 0.5d;
        [SerializeField] private int randomSeed = 1729;
        [SerializeField] private bool enableAutomaticAi = true;
        [SerializeField] private bool paceAiInteractions;
        [SerializeField, Min(MinimumAiInteractionInterval)] private float aiInteractionInterval = MinimumAiInteractionInterval;
        [SerializeField] private bool enableAuctionDebugLog;
        [SerializeField] private bool enableAiDialogueLog = true;
        [SerializeField, Min(0)] private float aiDialogueCooldown = 4;
        [SerializeField] private TextAsset aiDialogueLibrary;
        [SerializeField, Min(0.01f)] private float aiDecisionInterval = 3;
        [SerializeField, Min(0.01f)] private float aiPlayerBidMinReactionDelay = 0.5f;
        [SerializeField, Min(0.01f)] private float aiPlayerBidMaxReactionDelay = 2f;
        [SerializeField, Range(0, 100)] private int smallBidIntimidation = 15;
        [SerializeField, Range(0, 100)] private int mediumBidIntimidation = 35;
        [SerializeField, Range(0, 100)] private int highBidIntimidation = 65;

        public const int MinimumOpeningBid = 10;
        internal const int MediumIncrement = 25;
        internal const int HighIncrement = 50;

        private const double ResultDuration = 2;

        [SerializeField] private AuctionItem[] items = Array.Empty<AuctionItem>();
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private AIPersonality[] bidders;
        [SerializeField, Range(0, 3)] private int activeBidderLimit;

        public AuctionState State { get; private set; }
        public AuctionItem CurrentItem { get; private set; }
        public DailyRunPlan DailyPlan { get; private set; }
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
        private double stateStartedAt;
        private double nextAiInteraction;
        private int nextOpening;
        private AIBidder pendingWinner;
        private double resultEndsAt;
        private readonly List<(AIBidder bidder, int amount, long sequence)> pendingActions = new();
        private double bidDeadline;
        private double bidReactionAt;
        private bool processingTime;
        private long auctionTick;
        private long bidSequence;
        private bool publishingBid;
        private AuctionResult presentedResult;
        private bool resultPresentationComplete;
        internal bool DebugLogEnabled => enableAuctionDebugLog;
        public bool PacesAiInteractions => paceAiInteractions;
        public float AiInteractionInterval => float.IsFinite(aiInteractionInterval)
            ? Math.Max(MinimumAiInteractionInterval, aiInteractionInterval) : MinimumAiInteractionInterval;
        internal double AuctionElapsed => stateStartedAt + elapsed;
        private bool AiSlotAvailable => AuctionElapsed + 1e-8 >= nextAiInteraction;
        private bool BidReactionReady => elapsed >= bidReactionAt;
        internal double DecisionInterval => aiDecisionInterval > 0 && !float.IsInfinity(aiDecisionInterval)
            ? aiDecisionInterval : 3;
        private AIBidder[] opponents = Array.Empty<AIBidder>();
        private readonly List<AIBidder> activeOpponents = new();
        private readonly List<AIBidder> eligibleOpponents = new();
        private System.Random participationRandom;
        private IReadOnlyList<AuctionItem> lots;
        private AuctionDialogue dialogue;
        private ItemDatabase catalog;

        public event Action<AuctionItem> OnItemChanged;
        public event Action<BidRecord> OnBidPlaced;
        internal event Action OnPlayerBidRejected;
        public event Action<AuctionResult> OnAuctionEnded;
        public event Action<AuctionState> OnStateChanged;
        public event Action OnAuctionPhaseFinished;
        public event Action OnClockChanged;
        public event Action<string, string, string> OnAiDialogue;
        public event Action<AIBidder> OnBidderChickenedOut;
        internal bool DialogueLogEnabled => enableAiDialogueLog;

        internal void PublishAiDialogue(string bidderId, string category, string line) =>
            OnAiDialogue?.Invoke(bidderId, category, line);

        public bool StartAuction() => StartRun(items, itemDatabase);

        public bool StartAuction(IReadOnlyList<AuctionItem> run, ItemDatabase fullCatalog) =>
            fullCatalog && StartRun(run, fullCatalog);

        public bool StartDailyAuction(DailyRunPlan plan, ItemDatabase fullCatalog) =>
            plan != null && fullCatalog && StartRun(plan.Lots, fullCatalog, plan);

        public bool RestartAuction() => ResetLevel() && StartRun(lots, catalog, DailyPlan);

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
                activeOpponents.Clear();
                Economy.ResetLevel();
                SetState(AuctionState.Idle);
                return true;
            }
            finally
            {
                processingTime = false;
            }
        }

        private bool StartRun(IReadOnlyList<AuctionItem> run, ItemDatabase fullCatalog, DailyRunPlan plan = null)
        {
            if (processingTime || State != AuctionState.Idle || run == null)
                return false;

            var snapshot = new List<AuctionItem>(run.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (AuctionItem item in run)
            {
                if (!item || string.IsNullOrWhiteSpace(item.ItemId) || !ids.Add(item.ItemId) ||
                    item.TrueValue < 0)
                    return false;

                snapshot.Add(item);
            }

            AIPersonality[] personalities = bidders ?? LoadDefaultBidders();
            var bidderIds = new HashSet<string>(StringComparer.Ordinal) { BidderIds.Player };
            foreach (AIPersonality personality in personalities)
            {
                if (!personality || (activeBidderLimit > 0 &&
                    (string.IsNullOrWhiteSpace(personality.DialogueProfileId) || !bidderIds.Add(personality.DialogueProfileId))))
                    return false;
            }

            auctionTick = 0;
            bidSequence = 0;
            dialogue = new AuctionDialogue(this, aiDialogueLibrary ? aiDialogueLibrary :
                Resources.Load<TextAsset>("Auction/AiDialogue"), randomSeed, aiDialogueCooldown);
            if (paceAiInteractions)
            {
                foreach (AIPersonality personality in personalities)
                {
                    if (dialogue.HasInteractionLines(personality))
                        continue;
                    Debug.LogError($"Missing required AI interaction dialogue for {personality.Name}.", this);
                    return false;
                }
            }
            stateStartedAt = 0;
            nextAiInteraction = 0;
            pendingWinner = null;
            pendingActions.Clear();
            resultEndsAt = ResultDuration;

            var random = new System.Random(randomSeed);
            opponents = new AIBidder[personalities.Length];
            for (int index = 0; index < personalities.Length; index++)
            {
                AIPersonality personality = personalities[index];
                string id = activeBidderLimit > 0 ? personality.DialogueProfileId : BidderIds.ForOpponentIndex(index);
                opponents[index] = new AIBidder(id, personality, random.Next(), activeBidderLimit > 0);
            }
            participationRandom = new System.Random(random.Next());
            Opponents = Array.AsReadOnly(opponents);
            lots = plan?.Lots ?? snapshot;
            catalog = fullCatalog;
            DailyPlan = plan;

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

        internal IReadOnlyList<AIPersonality> NewsBidderPool => bidders ?? LoadDefaultBidders();

        private void Reset() => bidders = LoadDefaultBidders();

        private static AIPersonality[] LoadDefaultBidders() => new[]
        {
            Resources.Load<AIPersonality>("Auction/Personalities/Bidder1"),
            Resources.Load<AIPersonality>("Auction/Personalities/Bidder2"),
            Resources.Load<AIPersonality>("Auction/Personalities/Bidder3")
        };

        public string GetBidderDisplayName(string bidderId) =>
            FindOpponent(bidderId)?.Personality.Name ?? BidderIds.DisplayName(bidderId);

        internal bool HoldResultForPresentation(AuctionResult result)
        {
            if (presentedResult != null || State is not (AuctionState.Sold or AuctionState.Unsold) ||
                results[^1] != result)
                return false;

            presentedResult = result;
            resultPresentationComplete = false;
            return true;
        }

        internal bool HasPendingWinnerDialogue => pendingWinner != null;

        internal void CompleteResultPresentation(AuctionResult result)
        {
            if (presentedResult == result)
                resultPresentationComplete = true;
        }

        private void Update() => Tick(Time.deltaTime);

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
                    if (presentedResult != null && pendingWinner == null)
                    {
                        if (resultPresentationComplete)
                        {
                            presentedResult = null;
                            AdvanceItem();
                        }
                        // Never carry time from the result into the newly revealed lot.
                        break;
                    }

                    double boundary = State switch
                    {
                        AuctionState.ItemReveal => RevealDuration,
                        AuctionState.Bidding => Math.Min(bidDeadline, MaximumBiddingDuration),
                        _ => resultEndsAt
                    };

                    bool openingDue = paceAiInteractions && State == AuctionState.ItemReveal &&
                        nextOpening < activeOpponents.Count;
                    bool winnerDue = State == AuctionState.Sold && pendingWinner != null;
                    double dialogueDueAt = Math.Max(elapsed, nextAiInteraction - stateStartedAt);
                    if (openingDue || winnerDue)
                        boundary = dialogueDueAt;
                    bool actionDue = State == AuctionState.Bidding && pendingActions.Count > 0 &&
                        Math.Max(dialogueDueAt, bidReactionAt) < boundary;
                    if (actionDue)
                        boundary = Math.Max(dialogueDueAt, bidReactionAt);

                    // Expiration wins ties. Equal AI decision times retain opponent order.
                    AIBidder nextOpponent = null;
                    if (State == AuctionState.Bidding && enableAutomaticAi)
                    {
                        foreach (AIBidder opponent in activeOpponents)
                        {
                            double readyAt = Math.Max(elapsed, Math.Max(bidReactionAt, opponent.NextDecision));
                            if ((paceAiInteractions && (opponent.HasChickenedOut ||
                                pendingActions.Exists(action => action.bidder == opponent))) || readyAt >= boundary)
                                continue;
                            actionDue = false;
                            boundary = readyAt;
                            nextOpponent = opponent;
                        }
                    }
                    else if (State == AuctionState.ItemReveal && paceAiInteractions && !openingDue)
                    {
                        boundary = Math.Max(elapsed, RevealDuration);
                    }

                    double untilBoundary = boundary - elapsed;
                    if (remainingStep < untilBoundary)
                    {
                        elapsed += remainingStep;
                        break;
                    }

                    remainingStep -= untilBoundary;
                    elapsed = boundary;
                    if (openingDue)
                    {
                        SpeakOpening();
                        continue;
                    }
                    if (winnerDue)
                    {
                        SpeakWinner();
                        continue;
                    }
                    if (actionDue)
                    {
                        ExecutePendingAction();
                        continue;
                    }
                    if (nextOpponent != null)
                    {
                        nextOpponent.Decide(this);
                        continue;
                    }

                    switch (State)
                    {
                        case AuctionState.ItemReveal:
                            bidDeadline = InitialBiddingTime;
                            bidReactionAt = 0;
                            if (enableAuctionDebugLog)
                                LogAuctionDebug($"Bidding opened | true value=${CurrentItem.TrueValue} | opening=${CurrentPrice}");
                            foreach (AIBidder opponent in opponents)
                            {
                                opponent.BeginItem(CurrentItem, DecisionInterval);
                                if (enableAuctionDebugLog)
                                    opponent.LogEvaluation(this);
                            }
                            if (!paceAiInteractions && activeBidderLimit <= 0)
                                dialogue?.OpenLot(itemIndex + 1, activeOpponents);
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
            {
                if (bidderId == BidderIds.Player)
                    OnPlayerBidRejected?.Invoke();
                return false;
            }

            int previousPrice = CurrentPrice;
            // Only raises contribute to intimidation. An opening bid establishes the first price.
            int increment = HighestBidderId.Length == 0 ? 0 : amount - previousPrice;
            CurrentPrice = amount;
            HighestBidderId = bidderId;
            if (bidDeadline - elapsed <= AntiSnipingTime)
                bidDeadline = elapsed + AntiSnipingTime;

            AIBidder bidder = FindOpponent(bidderId);
            bidder?.RecordAcceptedBid(amount);
            var bid = new BidRecord(CurrentItem.ItemId, bidderId, amount, elapsed,
                bidder?.GetBidComment() ?? "", ++bidSequence, increment);
            bidHistory.Add(bid);
            if (paceAiInteractions)
                ScheduleBidReactions(bidderId == BidderIds.Player);
            if (enableAuctionDebugLog)
                LogAuctionDebug($"Bid accepted | {GetBidderDisplayName(bidderId)} | ${previousPrice} → ${amount} | remaining={RemainingTime:F3}s");
            bool wasProcessing = processingTime;
            processingTime = true;
            publishingBid = true;
            try
            {
                if (!paceAiInteractions)
                    dialogue?.AcceptedBid(bid, activeOpponents);
                else if (bidder != null)
                {
                    ReserveAiInteraction();
                    dialogue?.Bid(bidder, amount, elapsed);
                }
                OnBidPlaced?.Invoke(bid);
            }
            finally
            {
                publishingBid = false;
                processingTime = wasProcessing;
            }

            if (!paceAiInteractions && bidderId == BidderIds.Player && enableAutomaticAi)
            {
                double minDelay = aiPlayerBidMinReactionDelay > 0 && !float.IsInfinity(aiPlayerBidMinReactionDelay)
                    ? aiPlayerBidMinReactionDelay : 0.5;
                double maxDelay = aiPlayerBidMaxReactionDelay >= minDelay && !float.IsInfinity(aiPlayerBidMaxReactionDelay)
                    ? aiPlayerBidMaxReactionDelay : Math.Max(minDelay, 2);
                foreach (AIBidder opponent in activeOpponents)
                    opponent.SchedulePlayerBidReaction(elapsed, minDelay, maxDelay);
            }
            return true;
        }

        private void ScheduleBidReactions(bool playerBid)
        {
            double remaining = Math.Min(bidDeadline, MaximumBiddingDuration) - elapsed;
            bidReactionAt = elapsed + (playerBid && remaining <= AntiSnipingTime
                ? FinalBidReactionDelay : NormalBidReactionDelay);

            // Earlier AI minimum raises and withdrawals refer to the previous bid.
            pendingActions.Clear();
            foreach (AIBidder opponent in activeOpponents)
                opponent.ScheduleBidReaction(bidReactionAt);
        }

        internal void LogAuctionDebug(string message) =>
            Debug.Log($"[Auction Debug] lot {itemIndex + 1} ({CurrentItem.ItemId}) | tick {auctionTick} | frame {Time.frameCount} | t={BiddingElapsed:F3}s | {message}", this);

        internal void NotifyChickenOut(AIBidder bidder)
        {
            if (!bidder.IsActiveForLot || !bidder.HasChickenedOut)
                return;

            if (paceAiInteractions)
                ReserveAiInteraction();
            dialogue?.ChickenOut(bidder, BiddingElapsed);
            OnBidderChickenedOut?.Invoke(bidder);
        }

        public bool CanPlaceBid(string bidderId, int amount)
            => CanEvaluateBid(bidderId, amount) &&
                (!paceAiInteractions || bidderId == BidderIds.Player || (AiSlotAvailable && BidReactionReady));

        internal bool CanEvaluateBid(string bidderId, int amount)
        {
            if (State != AuctionState.Bidding || elapsed >= bidDeadline ||
                elapsed >= MaximumBiddingDuration || string.IsNullOrEmpty(bidderId) ||
                bidderId == HighestBidderId || !IsLegalAmount(bidderId, amount))
                return false;

            if (bidderId == BidderIds.Player)
                return Economy.CanAfford(amount);

            AIBidder opponent = FindOpponent(bidderId);
            return opponent != null && amount <= opponent.BidLimit;
        }

        internal void RequestAiAction(AIBidder bidder, int amount)
        {
            if (paceAiInteractions && (!AiSlotAvailable || !BidReactionReady || pendingActions.Count > 0))
                pendingActions.Add((bidder, amount, bidSequence));
            else
                ExecuteAiAction(bidder, amount, bidSequence);
        }

        private void ExecutePendingAction()
        {
            var (bidder, amount, sequence) = pendingActions[0];
            pendingActions.RemoveAt(0);
            bidder.ResumeDecisions(elapsed, DecisionInterval);
            ExecuteAiAction(bidder, amount, sequence);
        }

        private void ExecuteAiAction(AIBidder bidder, int amount, long sequence)
        {
            if (amount > 0)
                TryPlaceBid(bidder.Id, amount);
            else if (!bidder.HasChickenedOut && sequence == bidSequence &&
                CanEvaluateBid(bidder.Id, GetNextBidAmount()))
            {
                bidder.Withdraw();
                NotifyChickenOut(bidder);
            }
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

        private bool IsLegalAmount(string bidderId, int amount)
        {
            if (HighestBidderId.Length == 0 && bidderId != BidderIds.Player)
                return amount == MinimumOpeningBid;

            long increment = (long)amount - CurrentPrice;
            return increment is MinimumOpeningBid or MediumIncrement or HighIncrement;
        }

        internal int GetNextBidAmount()
        {
            return CurrentPrice <= int.MaxValue - MinimumOpeningBid ? CurrentPrice + MinimumOpeningBid : 0;
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
            MinimumOpeningBid => smallBidIntimidation,
            MediumIncrement => mediumBidIntimidation,
            HighIncrement => highBidIntimidation,
            _ => 0
        }, 0, 100) / 100d;

        private void CloseItem()
        {
            if (State != AuctionState.Bidding)
                return;

            var result = new AuctionResult(CurrentItem.ItemId, HighestBidderId, CurrentPrice);
            AIBidder winner = FindOpponent(result.winnerId);
            if (result.winnerId == BidderIds.Player)
                Economy.RecordPurchase(CurrentItem, result.finalPrice);
            else
                winner?.RecordWin(CurrentItem, result.finalPrice);

            results.Add(result);
            pendingActions.Clear();
            double settledAt = elapsed;
            resultEndsAt = ResultDuration;
            SetState(result.sold ? AuctionState.Sold : AuctionState.Unsold);
            if (paceAiInteractions && winner != null)
            {
                pendingWinner = winner;
                if (AiSlotAvailable)
                    SpeakWinner();
            }
            else
                dialogue?.EndLot(result, winner, settledAt);
            OnAuctionEnded?.Invoke(result);
        }

        private void ReserveAiInteraction() => nextAiInteraction = AuctionElapsed + AiInteractionInterval;

        private void SpeakOpening()
        {
            ReserveAiInteraction();
            dialogue?.Opening(activeOpponents[nextOpening++], elapsed);
        }

        private void SpeakWinner()
        {
            AIBidder winner = pendingWinner;
            pendingWinner = null;
            ReserveAiInteraction();
            resultEndsAt = Math.Max(ResultDuration, elapsed + AiInteractionInterval);
            dialogue?.EndLot(results[^1], winner, elapsed);
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
            SelectParticipants();
            SetState(AuctionState.ItemReveal);
            OnItemChanged?.Invoke(CurrentItem);
            if (paceAiInteractions)
            {
                nextOpening = 0;
                dialogue?.BeginLot(itemIndex + 1, activeOpponents);
                if (activeOpponents.Count > 0 && AiSlotAvailable)
                    SpeakOpening();
            }
            else if (activeBidderLimit > 0)
                dialogue?.OpenLot(itemIndex + 1, activeOpponents);
        }

        internal static int ParticipationWeight(AIBidder bidder) => 10 + bidder.EffectiveInterest;

        private void SelectParticipants()
        {
            activeOpponents.Clear();
            eligibleOpponents.Clear();
            int openingBid = GetNextBidAmount();
            foreach (AIBidder opponent in opponents)
            {
                opponent.PrepareItem(CurrentItem);
                if (activeBidderLimit <= 0)
                    opponent.IsActiveForLot = true;
                else if (opponent.AvailableBudget >= openingBid && IsLegalAmount(opponent.Id, openingBid))
                    eligibleOpponents.Add(opponent);
            }

            int count = Math.Min(Math.Clamp(activeBidderLimit, 0, 3), eligibleOpponents.Count);
            for (int draw = 0; draw < count; draw++)
            {
                int totalWeight = 0;
                foreach (AIBidder opponent in eligibleOpponents)
                    totalWeight += ParticipationWeight(opponent);

                int roll = participationRandom.Next(totalWeight);
                int selected = 0;
                while (roll >= ParticipationWeight(eligibleOpponents[selected]))
                    roll -= ParticipationWeight(eligibleOpponents[selected++]);

                eligibleOpponents[selected].IsActiveForLot = true;
                eligibleOpponents.RemoveAt(selected);
            }

            // Retain the existing order for simultaneous AI decisions and dialogue priority.
            foreach (AIBidder opponent in opponents)
            {
                if (opponent.IsActiveForLot)
                    activeOpponents.Add(opponent);
            }
        }

        private void SetState(AuctionState state)
        {
            stateStartedAt += elapsed;
            elapsed = 0;
            State = state;
            OnStateChanged?.Invoke(state);
        }
    }
}

