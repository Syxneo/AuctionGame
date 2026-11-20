using System;

namespace AuctionGame.Auction
{
    public enum AuctionState { Idle, ItemReveal, Bidding, Sold, Unsold, Finished }

    public static class BidderIds
    {
        public const string Player = "player";
        public const string Collector = "collector";
        public const string Trickster = "trickster";
        public const string Analyst = "analyst";

        public static string DisplayName(string id) => id switch
        {
            Player => "You",
            Collector => "Collector",
            Trickster => "Trickster",
            Analyst => "Analyst",
            _ => "—"
        };
    }

    [Serializable]
    public sealed class BidRecord
    {
        public readonly string itemId;
        public readonly string bidderId;
        public readonly int amount;
        public readonly double biddingElapsed;
        public readonly string comment;

        internal BidRecord(string itemId, string bidderId, int amount, double biddingElapsed, string comment)
        {
            this.itemId = itemId;
            this.bidderId = bidderId;
            this.amount = amount;
            this.biddingElapsed = biddingElapsed;
            this.comment = comment;
        }
    }

    [Serializable]
    public sealed class PurchasedItem
    {
        public readonly AuctionItem item;
        public readonly int purchasePrice;
        public AuctionItemInstance Variant { get; }

        internal PurchasedItem(AuctionItem item, int purchasePrice, AuctionItemInstance variant)
        {
            this.item = item;
            this.purchasePrice = purchasePrice;
            Variant = variant;
        }
    }

    [Serializable]
    public sealed class AuctionResult
    {
        public readonly string itemId;
        public readonly string winnerId;
        public readonly int finalPrice;
        public readonly bool sold;

        internal AuctionResult(string itemId, string winnerId, int finalPrice)
        {
            this.itemId = itemId;
            this.winnerId = winnerId;
            this.finalPrice = finalPrice;
            sold = winnerId.Length != 0;
        }
    }
}
