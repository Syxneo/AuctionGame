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
        private readonly Random commentRandom;
        private readonly Random intimidationRandom;
        private readonly Random reactionRandom;
        private readonly List<AIPurchase> purchases = new();
        private long lastHandledBidSequence;

        public string Id { get; }
        public AIPersonality Personality { get; }
        public int StartingBudget { get; }
        public int AvailableBudget { get; private set; }
        public int EstimatedValue { get; private set; }
        public int EffectiveInterest { get; private set; }
        public int EffectiveAccuracy { get; private set; }
        public bool HasUsedLastBid { get; private set; }
        public bool HasChickenedOut { get; private set; }
        public IReadOnlyList<AIPurchase> Purchases { get; }
        public long TotalProfit { get; private set; }
        internal double NextDecision { get; private set; }
        internal int BidLimit => HasUsedLastBid || HasChickenedOut ? 0 : AvailableBudget;

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
            commentRandom = new Random(seeds.Next());
            intimidationRandom = new Random(seeds.Next());
            reactionRandom = new Random(seeds.Next());
        }

        internal void SchedulePlayerBidReaction(double elapsed, double minDelay, double maxDelay) =>
            NextDecision = elapsed + minDelay + ((maxDelay - minDelay) * reactionRandom.NextDouble());

        internal void BeginItem(AuctionItem item, double interval)
        {
            (EffectiveInterest, EffectiveAccuracy) = Personality.GetEffectiveProfile(item.Categories);
            EstimatedValue = AIEvaluation.Estimate(item.TrueValue, EffectiveAccuracy, Personality.OverestimateBias, evaluationRandom);
            HasUsedLastBid = false;
            HasChickenedOut = false;
            lastHandledBidSequence = 0;
            NextDecision = interval;
        }

        internal void Decide(AuctionManager auction)
        {
            NextDecision = auction.BiddingElapsed + auction.DecisionInterval;
            int amount = auction.GetNextBidAmount();
            if (amount == 0 || !auction.CanPlaceBid(Id, amount))
            {
                if (auction.DebugLogEnabled)
                    auction.LogAuctionDebug($"{Personality.Name} skipped | next bid={amount} | budget={AvailableBudget} | leading={auction.HighestBidderId == Id} | last bid used={HasUsedLastBid} | chickened out={HasChickenedOut}");
                return;
            }

            BidRecord opponentBid = auction.FindIntimidationBid(Id, lastHandledBidSequence);
            if (opponentBid != null)
                lastHandledBidSequence = opponentBid.sequence;

            double roll = interestRandom.NextDouble();
            if (roll < EffectiveInterest / 100d)
            {
                if (auction.DebugLogEnabled)
                    auction.LogAuctionDebug($"{Personality.Name} attempting {amount} | Interest roll={roll:F4} | Interest={EffectiveInterest}% | one last bid={amount > EstimatedValue}");
                auction.TryPlaceBid(Id, amount);
                return;
            }

            if (opponentBid == null)
            {
                if (auction.DebugLogEnabled)
                    auction.LogAuctionDebug($"{Personality.Name} skipped | Interest roll={roll:F4} | Interest={EffectiveInterest}% | no new intimidation bid");
                return;
            }

            double chance = auction.GetIntimidationChance(opponentBid.increment) * (100 - Personality.IntimidationResistance) / 100d;
            double intimidationRoll = intimidationRandom.NextDouble();
            HasChickenedOut = intimidationRoll < chance;
            if (auction.DebugLogEnabled)
                auction.LogAuctionDebug($"{Personality.Name} skipped | Interest roll={roll:F4} | Interest={EffectiveInterest}% | intimidation bid #{opponentBid.sequence} (+{opponentBid.increment}) | roll={intimidationRoll:F4} | chance={chance:F4} | chickened out={HasChickenedOut}");
        }

        internal void RecordAcceptedBid(int amount)
        {
            if (amount > EstimatedValue)
                HasUsedLastBid = true;
        }

        internal void LogEvaluation(AuctionManager auction) =>
            auction.LogAuctionDebug($"{Personality.Name} evaluation | estimate={EstimatedValue} | Interest={EffectiveInterest}% | Accuracy={EffectiveAccuracy}% | budget={AvailableBudget} | first decision={NextDecision:F3}s");

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
    }
}
