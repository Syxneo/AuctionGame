using System;
using System.Collections.Generic;

namespace AuctionGame.Auction
{
    internal static class AIEvaluation
    {
        public static bool HasInterest(IReadOnlyList<string> interests, IReadOnlyList<ItemCategory> categories)
        {
            foreach (ItemCategory category in categories)
            {
                string name = category.ToString();
                foreach (string interest in interests)
                {
                    if (string.Equals(name, interest, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        public static int Estimate(AuctionItem current, ItemDatabase database, double accuracy,
            double overestimateProbability, Random random)
        {
            if (random.NextDouble() < accuracy)
                return current.TrueValue;

            if (database)
            {
                var candidates = new List<AuctionItem>();
                foreach (AuctionItem candidate in database.GetItemsSharingCategories(current))
                {
                    // Negative values mark variants whose prices are still being authored.
                    if (candidate.TrueValue >= 0)
                        candidates.Add(candidate);
                }

                if (candidates.Count > 0)
                    return candidates[random.Next(candidates.Count)].TrueValue;
            }

            bool overestimate = random.NextDouble() < overestimateProbability;
            double roll = random.NextDouble();
            double multiplier = overestimate ? 2.0 + 0.5 * roll : 0.3 + 0.2 * roll;
            double rounded = Math.Round(current.TrueValue * multiplier, MidpointRounding.AwayFromZero);
            return (int)Math.Min(int.MaxValue, rounded);
        }
    }
}
