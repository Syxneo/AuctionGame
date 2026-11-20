using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace AuctionGame.Auction
{
    public enum NewspaperImportance { MainArticle, ShortReport }

    [Serializable]
    public sealed class NewspaperClue
    {
        [SerializeField] private string clueKey;
        [SerializeField] private string itemId;
        [SerializeField] private string headline;
        [SerializeField, TextArea] private string body;
        [SerializeField] private NewspaperImportance importance;

        public string ClueKey => clueKey;
        public string ItemId => itemId;
        public string Headline => headline;
        public string Body => body;
        public NewspaperImportance Importance => importance;
    }

    [CreateAssetMenu(menuName = "Auction/MVP Level")]
    public sealed class MvpAuctionLevel : ScriptableObject
    {
        [SerializeField, Min(0)] private int startingBudget = 1000;
        [SerializeField, Min(0)] private int profitTarget = 300;
        [SerializeField, Min(0)] private int aiStartingBudget = 600;
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField, FormerlySerializedAs("definitions")]
        private AuctionItem[] variants = Array.Empty<AuctionItem>();
        [SerializeField] private NewspaperClue[] clues = Array.Empty<NewspaperClue>();

        public int StartingBudget => startingBudget;
        public int ProfitTarget => profitTarget;
        public int AiStartingBudget => aiStartingBudget;
        public ItemDatabase Database => itemDatabase;
        public IReadOnlyList<AuctionItem> Variants => Array.AsReadOnly(variants);
        public IReadOnlyList<NewspaperClue> Clues => Array.AsReadOnly(clues);

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
