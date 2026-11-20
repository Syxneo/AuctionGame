using System;
using System.Collections.Generic;

namespace AuctionGame.Auction
{
    public sealed class AIPurchase
    {
        public AuctionItem Item { get; }
        public AuctionItem Variant => Item;
        public string ItemId { get; }
        public int PurchasePrice { get; }
        public long ItemProfit { get; }

        internal AIPurchase(AuctionItem item, int price)
        {
            Item = item;
            ItemId = item.ItemId;
            PurchasePrice = price;
            ItemProfit = (long)item.TrueValue - price;
        }
    }

    public sealed class AIBidder
    {
        private static readonly int[] LargeFirst = { 50, 25, 10 };
        private static readonly int[] SmallFirst = { 10, 25, 50 };
        private readonly Random random;
        private readonly Random evaluationRandom;
        private readonly Random commentRandom;
        private readonly List<AIPurchase> purchases = new();
        private double bidProbability;

        public string Id { get; }
        public AIPersonality Personality { get; }
        public int StartingBudget { get; }
        public int AvailableBudget { get; private set; }
        public double EvaluationAccuracy => Personality.EvaluationAccuracy;
        public double Aggressiveness => Personality.Aggressiveness;
        public IReadOnlyList<string> Interests => Personality.Interests;
        public int EstimatedValue { get; private set; }
        public int MaxBid { get; private set; }
        public int EffectiveMaxBid => Math.Min(MaxBid, AvailableBudget);
        public IReadOnlyList<AIPurchase> Purchases { get; }
        public long TotalProfit { get; private set; }
        internal double NextDecision { get; private set; }

        internal AIBidder(string id, AIPersonality personality, int seed)
        {
            Id = id;
            Personality = personality;
            StartingBudget = personality.StartingBudget;
            AvailableBudget = StartingBudget;
            Purchases = purchases.AsReadOnly();
            var seeds = new Random(seed);
            evaluationRandom = new Random(seeds.Next());
            random = new Random(seeds.Next());
            commentRandom = new Random(seeds.Next());
        }

        internal void BeginItem(AuctionItem item, ItemDatabase catalog)
        {
            EstimatedValue = AIEvaluation.Estimate(item, catalog, EvaluationAccuracy, Personality.OverestimateProbability, evaluationRandom);
            bool preferred = AIEvaluation.HasInterest(Interests, item.Categories);
            double aggression = Math.Min(1, Aggressiveness + (preferred ? Personality.InterestAggressivenessBonus : 0));
            double priceFactor = 0.6 + 0.5 * aggression + (preferred ? Personality.InterestPriceBonus : 0);
            MaxBid = (int)Math.Min(int.MaxValue, Math.Round(EstimatedValue * priceFactor));
            bidProbability = Math.Clamp(0.35 + 0.6 * aggression + (preferred ? Personality.InterestProbabilityBonus : 0), 0, 1);
            NextDecision = DecisionInterval();
        }

        internal void Decide(AuctionManager auction)
        {
            NextDecision += DecisionInterval();
            if (auction.HighestBidderId == Id || random.NextDouble() >= bidProbability)
                return;

            if (auction.HighestBidderId.Length == 0)
            {
                auction.TryPlaceBid(Id, auction.CurrentItem.StartingPrice);
                return;
            }

            AIBidIncrementOrder order = Personality.IncrementOrder;
            int[] increments = order == AIBidIncrementOrder.LargeFirst ? LargeFirst : SmallFirst;
            int start = order == AIBidIncrementOrder.RandomRotation ? random.Next(increments.Length) : 0;
            for (int index = 0; index < increments.Length; index++)
            {
                long amount = (long)auction.CurrentPrice + increments[(start + index) % increments.Length];
                if (amount <= EffectiveMaxBid && auction.TryPlaceBid(Id, (int)amount))
                    return;
            }
        }

        internal string GetBidComment()
        {
            IReadOnlyList<string> comments = Personality.BidComments;
            return comments.Count == 0 ? "" : comments[commentRandom.Next(comments.Count)];
        }

        internal void RecordWin(AuctionItem item, int price)
        {
            var purchase = new AIPurchase(item, price);
            purchases.Add(purchase);
            AvailableBudget -= price;
            TotalProfit += purchase.ItemProfit;
        }

        private double DecisionInterval() =>
            Between(Personality.MinimumDecisionInterval, Personality.MaximumDecisionInterval);

        private double Between(double min, double max) => min + (max - min) * random.NextDouble();
    }
}
