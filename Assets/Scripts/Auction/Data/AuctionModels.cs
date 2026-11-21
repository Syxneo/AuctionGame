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

        internal static string ForOpponentIndex(int index) => index switch
        {
            0 => Collector,
            1 => Trickster,
            2 => Analyst,
            _ => $"bidder-{index + 1}"
        };

        public static string DisplayName(string id) => id switch
        {
            Player => "Ty",
            Collector => "Zberateľ",
            Trickster => "Vtipkár",
            Analyst => "Analytik",
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
        public readonly long sequence;
        public readonly int increment;

        internal BidRecord(string itemId, string bidderId, int amount, double biddingElapsed, string comment, long sequence, int increment)
        {
            this.itemId = itemId;
            this.bidderId = bidderId;
            this.amount = amount;
            this.biddingElapsed = biddingElapsed;
            this.comment = comment;
            this.sequence = sequence;
            this.increment = increment;
        }
    }

    [Serializable]
    public sealed class PurchasedItem
    {
        public readonly AuctionItem item;
        public readonly int purchasePrice;
        public AuctionItem Variant => item;
        public long Profit => (long)item.TrueValue - purchasePrice;

        internal PurchasedItem(AuctionItem item, int purchasePrice)
        {
            this.item = item;
            this.purchasePrice = purchasePrice;
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
