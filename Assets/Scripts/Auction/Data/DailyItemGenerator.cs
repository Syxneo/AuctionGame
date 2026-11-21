using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame.Auction
{
    internal static class DailyItemGenerator
    {
        internal const int MinLotCount = 4;

        internal static DailyRunPlan Generate(IReadOnlyList<AuctionItem> possibleItems, int dailyValueCap,
            int seed, IReadOnlyList<NewspaperEntry> newspaperEntries, int minLots = MinLotCount, int maxLots = 0)
        {
            if (dailyValueCap <= 0)
                throw new InvalidOperationException("Daily Value Cap must be positive.");
            if (minLots < 1 || maxLots < 0 || (maxLots != 0 && maxLots < minLots))
                throw new InvalidOperationException("Invalid daily lot limits.");
            if (possibleItems == null || possibleItems.Count < minLots)
                throw new InvalidOperationException($"Possible Items must contain at least {minLots} unique variants.");

            var articles = ValidateCatalog(possibleItems, newspaperEntries);

            var remaining = possibleItems.OrderBy(item => item.ItemId, StringComparer.Ordinal).ToList();
            var byValue = remaining.OrderBy(item => item.TrueValue).ToList();
            if (byValue.Take(minLots).Sum(item => (long)item.TrueValue) > dailyValueCap)
                throw new InvalidOperationException($"The {minLots} cheapest variants exceed Daily Value Cap ${dailyValueCap}.");

            var random = new Random(seed);
            int lotLimit = maxLots == 0 ? possibleItems.Count :
                random.Next(minLots, Math.Min(maxLots, possibleItems.Count) + 1);
            var selected = new List<AuctionItem>();
            int budget = dailyValueCap;
            while (selected.Count < lotLimit)
            {
                int neededAfterPick = Math.Max(0, minLots - selected.Count - 1);
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

            return BuildPlan(seed, selected, dailyValueCap, articles);
        }

        internal static DailyRunPlan Restore(IReadOnlyList<AuctionItem> catalog, CampaignDay day, int seed,
            IReadOnlyList<string> lotIds, IReadOnlyList<NewspaperEntry> newspaperEntries)
        {
            var articles = ValidateCatalog(catalog, newspaperEntries);
            var byId = catalog.ToDictionary(item => item.ItemId, StringComparer.Ordinal);
            var lots = new List<AuctionItem>(lotIds.Count);
            foreach (string id in lotIds)
            {
                if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out AuctionItem item))
                    throw new InvalidOperationException($"Saved daily variant '{id}' is unavailable.");
                lots.Add(item);
            }

            if (lots.Sum(item => (long)item.TrueValue) > day.DailyValueLimit)
                throw new InvalidOperationException("The saved daily plan exceeds the daily value limit.");
            var plan = BuildPlan(seed, lots, day.DailyValueLimit, articles);
            if (!day.Accepts(plan, catalog))
                throw new InvalidOperationException("The saved daily plan is invalid for this campaign day.");
            return plan;
        }

        private static Dictionary<AuctionItemType, NewspaperEntry> ValidateCatalog(
            IReadOnlyList<AuctionItem> possibleItems, IReadOnlyList<NewspaperEntry> newspaperEntries)
        {
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

            return articles;
        }

        private static DailyRunPlan BuildPlan(int seed, IReadOnlyList<AuctionItem> selected, int dailyValueCap,
            IReadOnlyDictionary<AuctionItemType, NewspaperEntry> articles)
        {
            var seenFamilies = new HashSet<AuctionItemType>();
            var edition = selected.Where(item => seenFamilies.Add(item.ItemType)).Select(item => articles[item.ItemType]);
            return new DailyRunPlan(seed, selected, dailyValueCap, edition);
        }
    }
}
