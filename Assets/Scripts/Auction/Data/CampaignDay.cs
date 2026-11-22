using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AuctionGame.Auction
{
    [Serializable]
    public sealed class CampaignDay
    {
        [SerializeField] private string title;
        [SerializeField] private string focus;
        [SerializeField] private string narrativeTone;
        [SerializeField, Min(0)] private int startingBudget;
        [SerializeField, Min(1)] private int dailyValueLimit;
        [SerializeField, Min(0)] private int profitTarget;
        [SerializeField, Min(0)] private float aiBudgetMultiplier = 1;
        [SerializeField, Min(1)] private int minLots = 4;
        [SerializeField, Min(0), Tooltip("Zero allows as many lots as the catalog and value limit permit.")]
        private int maxLots;
        [SerializeField, TextArea] private string[] saleDialogue = Array.Empty<string>();
        [SerializeField, TextArea] private string[] saleSuccessEnding = Array.Empty<string>();
        [SerializeField, TextArea] private string[] saleFailureEnding = Array.Empty<string>();
        [SerializeField, TextArea] private string auctioneerOpening;
        [SerializeField, TextArea(2, 5)] private string newsPresenterGreeting;
        [SerializeField] private string worldNewsHeadline;
        [SerializeField, TextArea] private string worldNewsBody;
        [SerializeField] private Sprite worldNewsImage;
        [SerializeField, TextArea] private string reportNote;

        public string Title => title;
        public string Focus => focus;
        public string NarrativeTone => narrativeTone;
        public int StartingBudget => startingBudget;
        public int DailyValueLimit => dailyValueLimit;
        public int ProfitTarget => profitTarget;
        public float AiBudgetMultiplier => aiBudgetMultiplier;
        public int MinLots => minLots;
        public int MaxLots => maxLots;
        public IReadOnlyList<string> SaleDialogue => saleDialogue;
        internal string[] SaleEnding(bool succeeded) => succeeded ? saleSuccessEnding : saleFailureEnding;
        internal string AuctioneerOpening => auctioneerOpening;
        internal string NewsPresenterGreeting => newsPresenterGreeting;
        internal string WorldNewsHeadline => worldNewsHeadline;
        internal string WorldNewsBody => worldNewsBody;
        internal Sprite WorldNewsImage => worldNewsImage;
        internal string ReportNote => reportNote;

        internal int ScaleAiBudget(int baseBudget)
        {
            if (baseBudget == 0)
                return 0;
            if (aiBudgetMultiplier >= int.MaxValue)
                return int.MaxValue;

            decimal scaled = baseBudget * (decimal)aiBudgetMultiplier;
            return (int)Math.Min(int.MaxValue, Math.Round(scaled, MidpointRounding.AwayFromZero));
        }

        internal void Validate()
        {
            if (startingBudget < 0 || profitTarget < 0 || dailyValueLimit < 1 ||
                !float.IsFinite(aiBudgetMultiplier) || aiBudgetMultiplier < 0 ||
                minLots < 1 || maxLots < 0 || (maxLots != 0 && maxLots < minLots))
                throw new InvalidOperationException($"Invalid campaign economy or lot limits for '{Title}'.");
        }

        internal bool Accepts(DailyRunPlan plan, IReadOnlyList<AuctionItem> catalog)
        {
            if (plan.Lots.Count < minLots || (maxLots != 0 && plan.Lots.Count > maxLots) ||
                plan.TotalTrueValue > dailyValueLimit)
                return false;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in plan.Lots)
            {
                if (!item || !catalog.Contains(item) || !ids.Add(item.ItemId))
                    return false;
            }

            return MaximumProfit(plan.Lots, startingBudget) >= profitTarget;
        }

        internal static long MaximumProfit(IEnumerable<AuctionItem> lots, int budget)
        {
            // Every lot can first be won for the same legal opening price in the best case.
            int price = AuctionManager.MinimumOpeningBid;
            return lots.Select(item => (long)item.TrueValue - price)
                .Where(profit => profit > 0).OrderByDescending(profit => profit)
                .Take(budget / price).Sum();
        }
    }
}
