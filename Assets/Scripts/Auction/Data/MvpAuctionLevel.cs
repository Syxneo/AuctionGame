using System;
using System.Collections.Generic;
using UnityEngine;

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
        [SerializeField] private bool useFixedSeed;
        [SerializeField] private int fixedSeed;
        [SerializeField] private AuctionItem[] definitions = Array.Empty<AuctionItem>();
        [SerializeField] private NewspaperClue[] clues = Array.Empty<NewspaperClue>();

        public int StartingBudget => startingBudget;
        public int ProfitTarget => profitTarget;
        public IReadOnlyList<AuctionItem> Definitions => Array.AsReadOnly(definitions);
        public IReadOnlyList<NewspaperClue> Clues => Array.AsReadOnly(clues);

        public IReadOnlyList<AuctionItemInstance> CreateRun(int? seed = null)
        {
            if (definitions.Length != 6)
                throw new InvalidOperationException("The MVP auction requires six item definitions.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var critical = new List<int>();
            var fluff = new List<int>();
            for (int index = 0; index < definitions.Length; index++)
            {
                AuctionItem definition = definitions[index];
                if (!definition || string.IsNullOrWhiteSpace(definition.ItemId) || !ids.Add(definition.ItemId))
                    throw new InvalidOperationException("MVP auction item IDs must be present and unique.");

                switch (definition.Role)
                {
                    case ItemRole.Critical:
                        critical.Add(index);
                        break;
                    case ItemRole.Fluff:
                        fluff.Add(index);
                        break;
                }
            }

            if (critical.Count != 3 || fluff.Count != 3)
                throw new InvalidOperationException("The MVP auction requires three items of each role.");

            var random = seed.HasValue ? new System.Random(seed.Value) :
                useFixedSeed ? new System.Random(fixedSeed) : new System.Random();
            int fakeCritical = critical[random.Next(critical.Count)];
            int fakeFluff = fluff[random.Next(fluff.Count)];
            var instances = new AuctionItemInstance[definitions.Length];
            for (int index = 0; index < instances.Length; index++)
            {
                var authenticity = index == fakeCritical || index == fakeFluff
                    ? ItemAuthenticity.Fake : ItemAuthenticity.Genuine;
                instances[index] = new AuctionItemInstance(definitions[index], authenticity);
            }

            return Array.AsReadOnly(instances);
        }
    }
}
