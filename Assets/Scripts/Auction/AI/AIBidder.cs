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
        public bool IsActiveForLot { get; internal set; }
        public IReadOnlyList<AIPurchase> Purchases { get; }
        public long TotalProfit { get; private set; }
        internal double NextDecision { get; private set; }
        internal int BidLimit => !IsActiveForLot || HasUsedLastBid || HasChickenedOut ? 0 :
            limitToEstimate ? Math.Min(EstimatedValue, AvailableBudget) : AvailableBudget;
        private readonly bool limitToEstimate;

        internal AIBidder(string id, AIPersonality personality, int seed, bool limitToEstimate, int? dailyBudget = null)
        {
            Id = id;
            Personality = personality;
            this.limitToEstimate = limitToEstimate;
            StartingBudget = dailyBudget ?? personality.StartingBudget;
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

        internal void ScheduleBidReaction(double readyAt) => NextDecision = readyAt;

        internal void PrepareItem(AuctionItem item)
        {
            (EffectiveInterest, EffectiveAccuracy) = Personality.GetEffectiveProfile(item.Categories);
            IsActiveForLot = false;
            HasUsedLastBid = false;
            HasChickenedOut = false;
            lastHandledBidSequence = 0;
        }

        internal void BeginItem(AuctionItem item, double interval)
        {
            EstimatedValue = AIEvaluation.Estimate(item.TrueValue, EffectiveAccuracy, Personality.OverestimateBias, evaluationRandom);
            NextDecision = interval;
        }

        internal void Decide(AuctionManager auction)
        {
            NextDecision = auction.BiddingElapsed + auction.DecisionInterval;
            int amount = auction.GetNextBidAmount();
            if (amount == 0 || !auction.CanEvaluateBid(Id, amount))
            {
                if (auction.DebugLogEnabled)
                    auction.LogAuctionDebug($"{Personality.Name} skipped | Bid chance=0% (no legal bid; Interest={EffectiveInterest}%) | next bid={amount} | budget={AvailableBudget} | bid limit={BidLimit} | leading={auction.HighestBidderId == Id} | chickened out={HasChickenedOut}");
                return;
            }

            BidRecord opponentBid = auction.FindIntimidationBid(Id, lastHandledBidSequence);
            if (opponentBid != null)
                lastHandledBidSequence = opponentBid.sequence;

            if (auction.PacesAiInteractions && TryWithdraw(auction, opponentBid))
                return;

            double roll = interestRandom.NextDouble();
            if (roll < EffectiveInterest / 100d)
            {
                if (auction.DebugLogEnabled)
                    auction.LogAuctionDebug($"{Personality.Name} attempting {amount} | Bid chance={EffectiveInterest}% | Interest roll={roll:F4}");
                auction.RequestAiAction(this, amount);
                return;
            }

            if (auction.DebugLogEnabled)
                auction.LogAuctionDebug($"{Personality.Name} skipped | Bid chance={EffectiveInterest}% | Interest roll={roll:F4}");

            if (!auction.PacesAiInteractions)
                TryWithdraw(auction, opponentBid);
        }

        private bool TryWithdraw(AuctionManager auction, BidRecord opponentBid)
        {
            if (opponentBid == null)
                return false;

            double chance = auction.GetIntimidationChance(opponentBid.increment) * (100 - Personality.IntimidationResistance) / 100d;
            double intimidationRoll = intimidationRandom.NextDouble();
            bool withdraw = intimidationRoll < chance;
            if (withdraw)
                auction.RequestAiAction(this, 0);
            if (auction.DebugLogEnabled)
                auction.LogAuctionDebug($"{Personality.Name} intimidation bid #{opponentBid.sequence} (+{opponentBid.increment}) | roll={intimidationRoll:F4} | chance={chance:F4} | withdrawal requested={withdraw} | chickened out={HasChickenedOut}");
            return withdraw;
        }

        internal void Withdraw() => HasChickenedOut = true;

        internal void ResumeDecisions(double elapsed, double interval)
        {
            if (NextDecision <= elapsed)
                NextDecision = elapsed + interval;
        }

        internal void LogEvaluation(AuctionManager auction) =>
            auction.LogAuctionDebug($"{Personality.Name} evaluation | estimate={EstimatedValue} | Interest={EffectiveInterest}% | Accuracy={EffectiveAccuracy}% | budget={AvailableBudget}");

        internal void RecordAcceptedBid(int amount)
        {
            if (!limitToEstimate && amount > EstimatedValue)
                HasUsedLastBid = true;
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
    }
}
