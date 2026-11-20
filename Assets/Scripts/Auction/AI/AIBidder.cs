using System;

namespace AuctionGame.Auction
{
    internal sealed class AIBidder
    {
        private static readonly int[] LargeFirst = { 50, 25, 10 };
        private static readonly int[] SmallFirst = { 10, 25, 50 };
        private readonly Random random;
        private double bidProbability;

        public string Id { get; }
        public int EstimatedValue { get; private set; }
        public int MaxBid { get; private set; }
        public double NextDecision { get; private set; }

        public AIBidder(string id, int seed)
        {
            Id = id;
            random = new Random(seed);
        }

        public void BeginItem(AuctionItem item)
        {
            var profile = Id switch
            {
                BidderIds.Collector => (0.80, 1.40, 0.90, 1.20,
                    item.Category is ItemCategory.Historical or ItemCategory.Rare ? 0.95 : 0.85),
                BidderIds.Trickster => (0.70, 1.30, 0.60, 1.00, 0.55),
                _ => (0.90, 1.10, 0.70, 0.85, 0.60)
            };

            var (estimateMin, estimateMax, bidMin, bidMax, probability) = profile;
            EstimatedValue = Scale(item.TrueValue, estimateMin, estimateMax);
            MaxBid = Scale(EstimatedValue, bidMin, bidMax);
            bidProbability = probability;
            NextDecision = DecisionInterval();
        }

        public void Decide(AuctionManager auction)
        {
            NextDecision += DecisionInterval();
            if (auction.HighestBidderId == Id || random.NextDouble() >= bidProbability)
                return;

            if (auction.HighestBidderId.Length == 0)
            {
                auction.TryPlaceBid(Id, auction.CurrentItem.StartingPrice);
                return;
            }

            int[] increments = Id == BidderIds.Collector ? LargeFirst : SmallFirst;
            int start = Id == BidderIds.Trickster ? random.Next(increments.Length) : 0;
            for (int index = 0; index < increments.Length; index++)
            {
                long amount = (long)auction.CurrentPrice + increments[(start + index) % increments.Length];
                if (amount <= MaxBid && auction.TryPlaceBid(Id, (int)amount))
                    return;
            }
        }

        private double DecisionInterval() => Id switch
        {
            BidderIds.Collector => Between(0.8, 2.0),
            BidderIds.Trickster => Between(0.5, 3.5),
            _ => Between(1.5, 3.0)
        };

        private int Scale(int value, double min, double max) =>
            (int)Math.Min(int.MaxValue, Math.Round(value * Between(min, max)));

        private double Between(double min, double max) => min + (max - min) * random.NextDouble();
    }
}
