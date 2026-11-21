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

        public AuctionItemType ItemType => itemType;
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

        public int StartingBudget => startingBudget;
        public int ProfitTarget => profitTarget;
        public int AiStartingBudget => aiStartingBudget;
        public ItemDatabase Database => itemDatabase;
        public IReadOnlyList<AuctionItem> Variants => Array.AsReadOnly(variants);
        public IReadOnlyList<NewspaperEntry> GetNewspaperEntries() =>
            Array.AsReadOnly((newspaperEntries ?? Array.Empty<NewspaperEntry>())
                .Where(entry => entry != null && entry.IsValid).ToArray());

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
