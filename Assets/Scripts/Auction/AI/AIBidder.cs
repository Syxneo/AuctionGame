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
        private readonly AIBidderProfile profile;
        private readonly List<AIPurchase> purchases = new();
        private double bidProbability;

        public string Id { get; }
        public AIPersonality Personality { get; }
        public int StartingBudget { get; }
        public int AvailableBudget { get; private set; }
        public double EvaluationAccuracy => profile.EvaluationAccuracy;
        public double Aggressiveness => profile.Aggressiveness;
        public IReadOnlyList<string> Interests => profile.Interests;
        public int EstimatedValue { get; private set; }
        public int MaxBid { get; private set; }
        public int EffectiveMaxBid => Math.Min(MaxBid, AvailableBudget);
        public IReadOnlyList<AIPurchase> Purchases { get; }
        public long TotalProfit { get; private set; }
        internal double NextDecision { get; private set; }

        internal AIBidder(string id, AIPersonality personality, AIBidderProfile profile, int seed)
        {
            Id = id;
            Personality = personality;
            this.profile = profile;
            StartingBudget = profile.StartingBudget;
            AvailableBudget = StartingBudget;
            Purchases = purchases.AsReadOnly();
            var seeds = new Random(seed);
            evaluationRandom = new Random(seeds.Next());
            random = new Random(seeds.Next());
            commentRandom = new Random(seeds.Next());
        }

        internal void BeginItem(AuctionItem item, ItemDatabase catalog)
        {
            EstimatedValue = AIEvaluation.Estimate(item, catalog, EvaluationAccuracy, profile.OverestimateProbability, evaluationRandom);
            bool preferred = AIEvaluation.HasInterest(Interests, item.Categories);
            double aggression = Math.Min(1, Aggressiveness + (preferred ? profile.InterestAggressivenessBonus : 0));
            double priceFactor = 0.6 + 0.5 * aggression + (preferred ? profile.InterestPriceBonus : 0);
            MaxBid = (int)Math.Min(int.MaxValue, Math.Round(EstimatedValue * priceFactor));
            bidProbability = Math.Clamp(0.35 + 0.6 * aggression + (preferred ? profile.InterestProbabilityBonus : 0), 0, 1);
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

            int[] increments = Personality == AIPersonality.Collector ? LargeFirst : SmallFirst;
            int start = Personality == AIPersonality.Trickster ? random.Next(increments.Length) : 0;
            for (int index = 0; index < increments.Length; index++)
            {
                long amount = (long)auction.CurrentPrice + increments[(start + index) % increments.Length];
                if (amount <= EffectiveMaxBid && auction.TryPlaceBid(Id, (int)amount))
                    return;
            }
        }

        internal string GetBidComment()
        {
            IReadOnlyList<string> comments = profile.BidComments;
            return comments.Count == 0 ? "" : comments[commentRandom.Next(comments.Count)];
        }

        internal void RecordWin(AuctionItem item, int price)
        {
            var purchase = new AIPurchase(item, price);
            purchases.Add(purchase);
            AvailableBudget -= price;
            TotalProfit += purchase.ItemProfit;
        }

        private double DecisionInterval() => Personality switch
        {
            AIPersonality.Collector => Between(0.8, 2.0),
            AIPersonality.Trickster => Between(0.5, 3.5),
            _ => Between(1.5, 3.0)
        };

        private double Between(double min, double max) => min + (max - min) * random.NextDouble();
    }
}
