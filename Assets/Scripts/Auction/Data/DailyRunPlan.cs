using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame.Auction
{
    public sealed class DailyRunPlan
    {
        public int Seed { get; }
        // The index in Lots identifies the lot within this run.
        public IReadOnlyList<AuctionItem> Lots { get; }
        public int TotalTrueValue { get; }
        public int UnusedCap { get; }
        public IReadOnlyList<NewspaperEntry> NewspaperEntries { get; }

        internal DailyRunPlan(int seed, IEnumerable<AuctionItem> lots, int dailyValueCap,
            IEnumerable<NewspaperEntry> newspaperEntries)
        {
            Seed = seed;
            Lots = Array.AsReadOnly(lots.ToArray());
            TotalTrueValue = Lots.Sum(item => item.TrueValue);
            UnusedCap = dailyValueCap - TotalTrueValue;
            NewspaperEntries = Array.AsReadOnly(newspaperEntries.ToArray());
        }
    }
}
