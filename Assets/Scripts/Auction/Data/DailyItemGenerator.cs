using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame.Auction
{
    internal static class DailyItemGenerator
    {
        internal const int MinLotCount = 4;

        internal static DailyRunPlan Generate(IReadOnlyList<AuctionItem> possibleItems, int dailyValueCap,
            int seed, IReadOnlyList<NewspaperEntry> newspaperEntries)
        {
            if (dailyValueCap <= 0)
                throw new InvalidOperationException("Daily Value Cap must be positive.");
            if (possibleItems == null || possibleItems.Count < MinLotCount)
                throw new InvalidOperationException($"Possible Items must contain at least {MinLotCount} unique variants.");

            var articles = new Dictionary<AuctionItemType, NewspaperEntry>();
            foreach (NewspaperEntry entry in newspaperEntries)
            {
                if (!articles.TryAdd(entry.ItemType, entry))
                    throw new InvalidOperationException($"Duplicate newspaper article for '{entry.ItemType.TypeId}'.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var familyTypes = new Dictionary<string, AuctionItemType>(StringComparer.Ordinal);
            foreach (AuctionItem item in possibleItems)
            {
                if (!item || string.IsNullOrWhiteSpace(item.ItemId))
                    throw new InvalidOperationException("Possible Items contains a missing item reference or ID.");
                if (!ids.Add(item.ItemId))
                    throw new InvalidOperationException($"Duplicate Possible Items ID '{item.ItemId}'.");
                if (item.TrueValue <= 0)
                    throw new InvalidOperationException($"'{item.ItemId}' must have a positive True Value.");
                if (!item.ItemType || string.IsNullOrWhiteSpace(item.ItemType.TypeId) || !item.Sprite)
                    throw new InvalidOperationException($"'{item.ItemId}' requires a valid Item Type and sprite.");
                string typeId = item.ItemType.TypeId;
                if (familyTypes.TryGetValue(typeId, out AuctionItemType type) && type != item.ItemType)
                    throw new InvalidOperationException($"Multiple Item Type assets use family ID '{typeId}'.");
                familyTypes[typeId] = item.ItemType;
                if (!articles.ContainsKey(item.ItemType))
                    throw new InvalidOperationException($"Missing newspaper article for '{typeId}'.");
            }

            var remaining = possibleItems.OrderBy(item => item.ItemId, StringComparer.Ordinal).ToList();
            var byValue = remaining.OrderBy(item => item.TrueValue).ToList();
            if (byValue.Take(MinLotCount).Sum(item => (long)item.TrueValue) > dailyValueCap)
                throw new InvalidOperationException($"The {MinLotCount} cheapest variants exceed Daily Value Cap ${dailyValueCap}.");

            var random = new Random(seed);
            var selected = new List<AuctionItem>();
            int budget = dailyValueCap;
            while (true)
            {
                int neededAfterPick = Math.Max(0, MinLotCount - selected.Count - 1);
                var fitting = remaining.Where(item => item.TrueValue <= budget && (neededAfterPick == 0 ||
                    byValue.Where(other => other != item).Take(neededAfterPick)
                        .Sum(other => (long)other.TrueValue) <= budget - item.TrueValue)).ToArray();
                if (fitting.Length == 0)
                    break;

                AuctionItem pick = fitting[random.Next(fitting.Length)];
                selected.Add(pick);
                remaining.Remove(pick);
                byValue.Remove(pick);
                budget -= pick.TrueValue;
            }

            for (int index = selected.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                (selected[index], selected[other]) = (selected[other], selected[index]);
            }

            var seenFamilies = new HashSet<AuctionItemType>();
            var edition = selected.Where(item => seenFamilies.Add(item.ItemType)).Select(item => articles[item.ItemType]);
            return new DailyRunPlan(seed, selected, dailyValueCap, edition);
        }
    }
}
