using System.Collections.Generic;
using UnityEngine;

namespace AuctionGame.Auction
{
    public enum PurchaseOutcome { GoodBuy, BreakEven, Overpaid }

    public sealed class ReportPurchase
    {
        public string ItemName { get; }
        public Sprite Sprite { get; }
        public int PurchasePrice { get; }
        public int TrueValue { get; }
        public long Profit { get; }
        public PurchaseOutcome Outcome => Profit > 0 ? PurchaseOutcome.GoodBuy
            : Profit < 0 ? PurchaseOutcome.Overpaid : PurchaseOutcome.BreakEven;

        internal ReportPurchase(PurchasedItem purchase)
        {
            ItemName = purchase.item.ItemName;
            Sprite = purchase.item.Sprite;
            PurchasePrice = purchase.purchasePrice;
            TrueValue = purchase.item.TrueValue;
            Profit = purchase.Profit;
        }
    }

    public sealed class AuctionReport
    {
        public IReadOnlyList<ReportPurchase> Purchases { get; }
        public long TotalProfit { get; }
        public int TargetProfit { get; }
        public bool Succeeded => TotalProfit >= TargetProfit;

        public AuctionReport(IReadOnlyList<PurchasedItem> purchases, int targetProfit)
        {
            var rows = new ReportPurchase[purchases.Count];
            for (int index = 0; index < purchases.Count; index++)
            {
                rows[index] = new ReportPurchase(purchases[index]);
                TotalProfit += rows[index].Profit;
            }

            Purchases = System.Array.AsReadOnly(rows);
            TargetProfit = targetProfit;
        }
    }
}
