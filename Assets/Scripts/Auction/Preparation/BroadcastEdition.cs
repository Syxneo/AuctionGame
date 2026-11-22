using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AuctionGame.Auction
{
    internal enum NewsType { Item, Bidder, World }

    [Serializable]
    internal sealed class BroadcastArticle
    {
        public string id;
        public NewsType type;
        public string headline;
        [TextArea] public string body;
        public Sprite image;
        public AIPersonality bidder;
        public ItemCategory category;
        [Min(1)] public int selectionWeight = 1;
    }

    internal sealed class NewsCard
    {
        internal string Id { get; }
        internal NewsType Type { get; }
        internal string Body { get; }
        internal string Headline { get; }
        internal string DisplayText => string.IsNullOrWhiteSpace(Headline) ? Body : Headline + "\n\n" + Body;
        internal Sprite Image { get; }
        internal AuctionItem Item { get; }
        internal AIPersonality Bidder { get; }

        internal NewsCard(string id, NewsType type, string body, Sprite image,
            AuctionItem item = null, AIPersonality bidder = null, string headline = null)
        {
            Id = id;
            Type = type;
            Body = body;
            Headline = headline;
            Image = image;
            Item = item;
            Bidder = bidder;
        }
    }

    [Serializable]
    internal sealed class BroadcastEdition
    {
        [SerializeField] private BroadcastArticle[] articles = Array.Empty<BroadcastArticle>();

        internal Sprite GetBidderPortrait(AIPersonality bidder)
        {
            if (!bidder)
                return null;

            foreach (BroadcastArticle article in articles)
                if (article != null && article.type == NewsType.Bidder && article.bidder == bidder && article.image)
                    return article.image;

            return null;
        }

        internal IReadOnlyList<NewsCard> Select(DailyRunPlan plan, IReadOnlyList<AIPersonality> pool,
            CampaignDay day = null) =>
            Select(plan.Lots, plan.NewspaperEntries, pool, plan.Seed, day);

        internal IReadOnlyList<NewsCard> Select(IReadOnlyList<AuctionItem> lots,
            IReadOnlyList<NewspaperEntry> itemArticles, IReadOnlyList<AIPersonality> pool, int seed,
            CampaignDay day = null)
        {
            var random = new System.Random(seed ^ 0x35192B17);
            var selected = new List<NewsCard>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var family in lots.GroupBy(item => item.ItemType))
            {
                NewspaperEntry clue = itemArticles.FirstOrDefault(entry => entry.ItemType == family.Key);
                AuctionItem match = clue == null ? null : family.FirstOrDefault(item =>
                    clue.Variant ? item == clue.Variant : clue.FamilyWide);
                AuctionItem illustratedItem = match ? match : family.First();
                string body = match ? clue.BodyText : clue?.FamilyFallbackBody;
                if (string.IsNullOrWhiteSpace(body))
                {
                    body = $"{family.Key.DisplayName}. V katalógu tejto skupiny si zberatelia všímajú tvar, povrch a stav jednotlivých kusov.";
                    if (clue == null)
                        Debug.LogWarning($"Missing authored news clue for '{family.Key.TypeId}'.", family.Key);
                }

                Sprite image = match && clue.Illustration ? clue.Illustration : illustratedItem.Sprite;
                if (!image && (!match || clue.FamilyWide))
                    image = family.Key.NewspaperSprite;
                var card = new NewsCard("item-" + family.Key.TypeId, NewsType.Item, body, image, illustratedItem);
                selected.Add(card);
                seenIds.Add(card.Id);
            }

            var categories = lots.SelectMany(item => item.Categories).Distinct().ToArray();
            var bidders = articles.Where(entry => Valid(entry, NewsType.Bidder) && entry.bidder &&
                    pool.Contains(entry.bidder) && entry.bidder.GetEffectiveProfile(new[] { entry.category }).interest > 0)
                .OrderBy(entry => entry.id, StringComparer.Ordinal).ToArray();
            var seenBidders = new HashSet<AIPersonality>();
            for (int index = 0; index < 2; index++)
            {
                var options = bidders.Where(entry => !seenBidders.Contains(entry.bidder) && !seenIds.Contains(entry.id))
                    .ToArray();
                if (options.Length == 0)
                    throw new InvalidOperationException("The broadcast requires articles about two different bidders in the current pool.");

                BroadcastArticle entry = Pick(options, candidate =>
                    Math.Max(1, candidate.selectionWeight) *
                    (1d + candidate.bidder.GetEffectiveProfile(categories).interest), random);
                seenBidders.Add(entry.bidder);
                seenIds.Add(entry.id);
                selected.Add(new NewsCard(entry.id, entry.type, entry.body, entry.image, bidder: entry.bidder));
            }

            var world = articles.Where(entry => Valid(entry, NewsType.World) && !seenIds.Contains(entry.id))
                .OrderBy(entry => entry.id, StringComparer.Ordinal).ToArray();
            if (world.Length > 0)
            {
                var entry = Pick(world, candidate => Math.Max(1, candidate.selectionWeight), random);
                selected.Add(new NewsCard(entry.id, entry.type, entry.body, entry.image));
            }
            else
            {
                string id = "world-fallback";
                while (seenIds.Contains(id))
                    id += "-fallback";
                selected.Add(new NewsCard(id, NewsType.World,
                    "Mestské múzeum otvorilo výstavu prázdnych vitrín. Podľa kurátora je to prvá zbierka, z ktorej sa ešte nič nestratilo.", null));
            }

            for (int index = selected.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                (selected[index], selected[other]) = (selected[other], selected[index]);
            }

            // Keep the original world draw and shuffle so their random calls stay in the same order.
            if (day != null && !string.IsNullOrWhiteSpace(day.WorldNewsHeadline) &&
                !string.IsNullOrWhiteSpace(day.WorldNewsBody))
            {
                int index = selected.FindIndex(card => card.Type == NewsType.World);
                var slot = selected[index];
                selected[index] = new NewsCard(slot.Id, NewsType.World, day.WorldNewsBody, day.WorldNewsImage,
                    headline: day.WorldNewsHeadline);
            }
            return selected.AsReadOnly();
        }

        private static bool Valid(BroadcastArticle entry, NewsType type) =>
            entry != null && entry.type == type && !string.IsNullOrWhiteSpace(entry.id) &&
            !string.IsNullOrWhiteSpace(entry.body);

        private static BroadcastArticle Pick(BroadcastArticle[] options, Func<BroadcastArticle, double> weight,
            System.Random random)
        {
            double roll = random.NextDouble() * options.Sum(weight);
            foreach (BroadcastArticle entry in options)
            {
                roll -= weight(entry);
                if (roll < 0)
                    return entry;
            }
            return options[^1];
        }
    }
}
