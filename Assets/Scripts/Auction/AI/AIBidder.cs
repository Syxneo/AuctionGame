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
        private readonly Random interestRandom;
        private readonly Random evaluationRandom;
        private readonly Random intimidationRandom;
        private readonly List<AIPurchase> purchases = new();
        private long lastHandledBidSequence;

        public string Id { get; }
        public AIPersonality Personality { get; }
        public int StartingBudget { get; }
        public int AvailableBudget { get; private set; }
        public int EstimatedValue { get; private set; }
        public int EffectiveInterest { get; private set; }
        public int EffectiveAccuracy { get; private set; }
        public bool HasChickenedOut { get; private set; }
        public IReadOnlyList<AIPurchase> Purchases { get; }
        public long TotalProfit { get; private set; }
        internal int BidLimit => HasChickenedOut ? 0 : Math.Min(EstimatedValue, AvailableBudget);

        internal AIBidder(string id, AIPersonality personality, int seed)
        {
            Id = id;
            Personality = personality;
            StartingBudget = personality.StartingBudget;
            AvailableBudget = StartingBudget;
            Purchases = purchases.AsReadOnly();
            var seeds = new Random(seed);
            evaluationRandom = new Random(seeds.Next());
            interestRandom = new Random(seeds.Next());
            // Retain the former comment seed slot so seeded intimidation stays reproducible.
            seeds.Next();
            intimidationRandom = new Random(seeds.Next());
        }

        internal void BeginItem(AuctionItem item)
        {
            (EffectiveInterest, EffectiveAccuracy) = Personality.GetEffectiveProfile(item.Categories);
            EstimatedValue = AIEvaluation.Estimate(item.TrueValue, EffectiveAccuracy, Personality.OverestimateBias, evaluationRandom);
            HasChickenedOut = false;
            lastHandledBidSequence = 0;
        }

        internal void Decide(AuctionManager auction)
        {
            int amount = auction.GetNextBidAmount();
            if (amount == 0 || !auction.CanPlaceBid(Id, amount))
            {
                if (auction.DebugLogEnabled)
                    auction.LogAuctionDebug($"{Personality.Name} skipped | Bid chance=0% (no legal bid; Interest={EffectiveInterest}%) | next bid={amount} | budget={AvailableBudget} | bid limit={BidLimit} | leading={auction.HighestBidderId == Id} | chickened out={HasChickenedOut}");
                return;
            }

            BidRecord opponentBid = auction.FindIntimidationBid(Id, lastHandledBidSequence);
            if (opponentBid != null)
                lastHandledBidSequence = opponentBid.sequence;

            double roll = interestRandom.NextDouble();
            if (roll < EffectiveInterest / 100d)
            {
                if (auction.DebugLogEnabled)
                    auction.LogAuctionDebug($"{Personality.Name} attempting {amount} | Bid chance={EffectiveInterest}% | Interest roll={roll:F4}");
                auction.TryPlaceBid(Id, amount);
                return;
            }

            if (opponentBid == null)
            {
                if (auction.DebugLogEnabled)
                auction.LogAuctionDebug($"{Personality.Name} skipped | Bid chance={EffectiveInterest}% | Interest roll={roll:F4} | no new intimidation bid");
                return;
            }

            double chance = auction.GetIntimidationChance(opponentBid.increment) * (100 - Personality.IntimidationResistance) / 100d;
            double intimidationRoll = intimidationRandom.NextDouble();
            HasChickenedOut = intimidationRoll < chance;
            if (HasChickenedOut)
                auction.NotifyChickenOut(this);
            if (auction.DebugLogEnabled)
                auction.LogAuctionDebug($"{Personality.Name} skipped | Bid chance={EffectiveInterest}% | Interest roll={roll:F4} | intimidation bid #{opponentBid.sequence} (+{opponentBid.increment}) | roll={intimidationRoll:F4} | chance={chance:F4} | chickened out={HasChickenedOut}");
        }

        internal void LogEvaluation(AuctionManager auction) =>
            auction.LogAuctionDebug($"{Personality.Name} evaluation | estimate={EstimatedValue} | Interest={EffectiveInterest}% | Accuracy={EffectiveAccuracy}% | budget={AvailableBudget}");

        internal void RecordWin(AuctionItem item, int price)
        {
            var purchase = new AIPurchase(item, price);
            purchases.Add(purchase);
            AvailableBudget -= price;
            TotalProfit += purchase.ItemProfit;
        }
    }
}
