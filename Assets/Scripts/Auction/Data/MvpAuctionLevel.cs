using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace AuctionGame.Auction
{
    [Serializable]
    public sealed class NewspaperEntry
    {
        [SerializeField] private AuctionItemType itemType;
        [SerializeField] private string headline;
        [SerializeField, TextArea, FormerlySerializedAs("body")] private string bodyText;

        [SerializeField] private AuctionItem variant;
        [SerializeField] private bool familyWide;
        [SerializeField, TextArea] private string familyFallbackBody;
        [SerializeField] private Sprite illustration;

        public AuctionItemType ItemType => itemType;
        internal AuctionItem Variant => variant;
        internal bool FamilyWide => familyWide;
        internal string FamilyFallbackBody => familyFallbackBody;
        internal Sprite Illustration => illustration;
        public string Headline => headline;
        public string BodyText => bodyText;

        internal bool IsValid => itemType && !string.IsNullOrWhiteSpace(itemType.TypeId) &&
            !string.IsNullOrWhiteSpace(headline) && !string.IsNullOrWhiteSpace(bodyText);
    }

    [CreateAssetMenu(menuName = "Auction/MVP Level")]
    public sealed class MvpAuctionLevel : ScriptableObject
    {
        [SerializeField, Min(0)] private int startingBudget = 1000;
        [SerializeField, Min(0)] private int profitTarget = 50;
        [SerializeField, Min(0)] private int aiStartingBudget = 600;
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField, FormerlySerializedAs("definitions")]
        private AuctionItem[] variants = Array.Empty<AuctionItem>();
        [SerializeField, FormerlySerializedAs("clues")]
        private NewspaperEntry[] newspaperEntries = Array.Empty<NewspaperEntry>();
        [SerializeField] private AuctionItem[] possibleItems = Array.Empty<AuctionItem>();
        [SerializeField, Min(1)] private int dailyValueCap = 1300;
        [SerializeField] private NewspaperEntry[] additionalDailyNewspaperEntries = Array.Empty<NewspaperEntry>();

        public int StartingBudget => startingBudget;
        public int ProfitTarget => profitTarget;
        public int AiStartingBudget => aiStartingBudget;
        public ItemDatabase Database => itemDatabase;
        public IReadOnlyList<AuctionItem> PossibleItems => Array.AsReadOnly(possibleItems);
        public int DailyValueCap => dailyValueCap;
        public IReadOnlyList<AuctionItem> Variants => Array.AsReadOnly(variants);
        public IReadOnlyList<NewspaperEntry> GetNewspaperEntries() =>
            Array.AsReadOnly((newspaperEntries ?? Array.Empty<NewspaperEntry>())
                .Where(entry => entry != null && entry.IsValid).ToArray());

        public DailyRunPlan CreateDailyRun(int seed)
        {
            var articles = GetNewspaperEntries().Concat(additionalDailyNewspaperEntries
                .Where(entry => entry != null && entry.IsValid)).ToArray();
            var plan = DailyItemGenerator.Generate(possibleItems, dailyValueCap, seed, articles);
            if (!itemDatabase)
                throw new InvalidOperationException("Assign the full Item Database to the MVP level.");
            foreach (AuctionItem item in possibleItems)
            {
                if (itemDatabase.GetItemById(item.ItemId) != item)
                    throw new InvalidOperationException($"Possible Items variant '{item.ItemId}' is missing from the Item Database.");
            }
            return plan;
        }

        // The original authored run remains available for the fixed MVP debug scene.
        public IReadOnlyList<AuctionItem> CreateRun()
        {
            if (variants.Length != 6)
                throw new InvalidOperationException("The MVP auction requires six item variants.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (AuctionItem variant in variants)
            {
                if (!variant || string.IsNullOrWhiteSpace(variant.ItemId) || !ids.Add(variant.ItemId))
                    throw new InvalidOperationException("MVP auction variant IDs must be present and unique.");
            }

            return Array.AsReadOnly((AuctionItem[])variants.Clone());
        }
    }
}
