using System;
using System.Collections.Generic;

namespace AuctionGame.Auction
{
    // Adapter from the current item API to the evaluator's variant and category inputs.
    internal sealed class AIEvaluationItem
    {
        public AuctionItem Definition { get; }
        public AuctionItemInstance Variant { get; }
        public string Id => Definition.ItemId;
        public int TrueValue => Variant?.ActualTrueValue ?? Definition.TrueValue;
        public IReadOnlyList<string> Categories { get; }

        public AIEvaluationItem(AuctionItem definition, AuctionItemInstance variant = null)
        {
            Definition = definition;
            Variant = variant;
            Categories = new[] { definition.Category.ToString() };
        }

        public static void AddDefinitionVariants(List<AIEvaluationItem> catalog, AuctionItem definition)
        {
            if (definition.HasVariantPrices)
            {
                catalog.Add(new AIEvaluationItem(definition, new AuctionItemInstance(definition, ItemAuthenticity.Genuine)));
                catalog.Add(new AIEvaluationItem(definition, new AuctionItemInstance(definition, ItemAuthenticity.Fake)));
            }
            else
            {
                catalog.Add(new AIEvaluationItem(definition));
            }
        }
    }

    internal static class AIEvaluation
    {
        public static bool SharesCategory(IReadOnlyList<string> first, IReadOnlyList<string> second)
        {
            foreach (string category in first)
            {
                foreach (string other in second)
                {
                    if (string.Equals(category, other, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        public static int Estimate(AIEvaluationItem current, IReadOnlyList<AIEvaluationItem> catalog,
            double accuracy, Random random)
        {
            if (random.NextDouble() < accuracy)
                return current.TrueValue;

            var candidates = new List<AIEvaluationItem>();
            foreach (AIEvaluationItem candidate in catalog)
            {
                if (candidate.Id != current.Id && SharesCategory(current.Categories, candidate.Categories))
                    candidates.Add(candidate);
            }

            return candidates.Count > 0
                ? candidates[random.Next(candidates.Count)].TrueValue
                : CategoryFallback(current.Categories);
        }

        // With no comparable variant, average the fixed category benchmarks.
        // These benchmarks are independent of the current item's actual value.
        private static int CategoryFallback(IReadOnlyList<string> categories)
        {
            if (categories.Count == 0)
                return 25;

            long total = 0;
            foreach (string category in categories)
            {
                total += category switch
                {
                    "Historical" or "Art" or "Technology" or "Jewelry" => 100,
                    "Rare" or "Luxury" or "Collectible" => 150,
                    "Mechanical" or "Coins" => 75,
                    _ => 25
                };
            }

            return (int)(total / categories.Count);
        }
    }
}
