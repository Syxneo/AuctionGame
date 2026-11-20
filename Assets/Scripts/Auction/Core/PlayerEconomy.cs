using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuctionGame.Auction
{
    public sealed class PlayerEconomy : MonoBehaviour
    {
        [SerializeField, Min(0)] private int startingBudget = 600;
        private readonly List<PurchasedItem> purchases = new();
        private readonly HashSet<string> purchasedIds = new(StringComparer.Ordinal);
        private int spent;

        public int AvailableBudget => startingBudget - spent;
        public IReadOnlyList<PurchasedItem> Purchases => purchases.AsReadOnly();

        public bool CanAfford(int amount) => amount > 0 && amount <= AvailableBudget;

        public bool RecordPurchase(AuctionItem item, int purchasePrice)
        {
            if (!item || string.IsNullOrWhiteSpace(item.ItemId) ||
                !CanAfford(purchasePrice) || !purchasedIds.Add(item.ItemId))
                return false;

            spent += purchasePrice;
            purchases.Add(new PurchasedItem(item, purchasePrice));
            return true;
        }

        internal void ResetLevel()
        {
            spent = 0;
            purchases.Clear();
            purchasedIds.Clear();
        }

        public int CalculateProfit() => (int)Math.Min(int.MaxValue, TotalProfit());

        public bool HasReachedTarget(int targetProfit) => TotalProfit() >= targetProfit;

        private long TotalProfit()
        {
            long profit = 0;
            foreach (PurchasedItem purchase in purchases)
                profit += purchase.Profit;
            return profit;
        }
    }
}
