using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuctionGame.Auction.Tests
{
    public sealed class MvpAuctionLevelTests
    {
        private MvpAuctionLevel level;

        [SetUp]
        public void LoadLevel()
        {
            level = AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>("Assets/AuctionMvp/MvpAuctionLevel.asset");
            Assert.That(level, Is.Not.Null);
            Assert.That(level.Database, Is.Not.Null);
        }

        [Test]
        public void DatabaseContainsEveryItemAssetAndUsesUniqueIds()
        {
            var assets = AssetDatabase.FindAssets("t:AuctionItem", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AuctionItem>);
            Assert.That(level.Database.Items, Is.EquivalentTo(assets));
            Assert.That(level.Database.Items.Select(item => item.ItemId), Is.Unique);
            Assert.That(level.Database.Items.All(item => item.ItemType && item.ItemType.Categories.Count > 0), Is.True);
            Assert.That(level.Database.Items.Select(item => item.ItemType).Distinct().Select(type => type.TypeId), Is.Unique);
            Assert.That(level.Database.GetItemById("unknown"), Is.Null);
            foreach (AuctionItem item in level.Database.Items)
                Assert.That(level.Database.GetItemById(item.ItemId), Is.SameAs(item));
        }

        [Test]
        public void SharedCategoriesFindOtherTypesAndOffLevelVariants()
        {
            AuctionItem item = level.Database.GetItemById("clay-tablet");
            AuctionItem alternate = level.Database.GetItemById("clay-tablet-polished-gray");
            Assert.That(item.Categories, Is.EqualTo(new[] { ItemCategory.Historical, ItemCategory.Collectible }));
            Assert.That(alternate.Categories, Is.EqualTo(item.Categories));
            Assert.That(level.Database.GetVariantsByType(item.ItemType), Is.EquivalentTo(new[] { item, alternate }));

            var candidates = level.Database.GetItemsSharingCategories(item);
            Assert.That(level.Variants, Has.No.Member(alternate));
            Assert.That(candidates, Has.Member(alternate));
            Assert.That(candidates.Any(candidate => candidate.ItemType != item.ItemType), Is.True);
            Assert.That(candidates, Has.No.Member(item));
            Assert.That(candidates, Has.No.Member(level.Database.GetItemById("relay-prototype")));
            Assert.That(candidates.All(candidate => candidate.Categories.Intersect(item.Categories).Any()), Is.True);
        }

        [Test]
        public void SharedCategoriesReturnsEmptyWhenOnlyTheCurrentItemMatches()
        {
            AuctionItem relay = level.Database.GetItemById("relay-prototype");
            Assert.That(relay.Categories, Is.EqualTo(new[] { ItemCategory.Technology }));
            Assert.That(level.Database.GetItemsSharingCategories(relay), Is.Empty);
            Assert.That(level.Database.GetItemsSharingCategories(null), Is.Empty);
        }

        [Test]
        public void LevelReturnsSixOrderedVariantsAndCluesResolveAgainstDatabase()
        {
            Assert.That(level.StartingBudget, Is.EqualTo(1000));
            Assert.That(level.ProfitTarget, Is.EqualTo(300));
            Assert.That(level.AiStartingBudget, Is.EqualTo(600));
            Assert.That(level.CreateRun(), Is.EqualTo(level.Variants).And.Count.EqualTo(6));
            Assert.That(level.Variants.Select(item => item.ItemId), Is.Unique);
            Assert.That(level.Variants.All(item => level.Database.GetItemById(item.ItemId) == item), Is.True);
            Assert.That(level.Clues.Select(clue => clue.ClueKey), Is.Unique);
            Assert.That(level.Clues.Select(clue => clue.ItemId).Distinct().Count(), Is.EqualTo(6));
            Assert.That(level.Clues.Count(clue => clue.Importance == NewspaperImportance.MainArticle), Is.EqualTo(3));
            foreach (NewspaperClue clue in level.Clues)
            {
                Assert.That(level.Database.GetItemById(clue.ItemId), Is.Not.Null);
                Assert.That(clue.Headline, Is.Not.Empty);
                Assert.That(clue.Body, Is.Not.Empty);
            }
        }

        [TestCase("clay-tablet", "clay-tablet-polished-gray", ItemCategory.Historical)]
        [TestCase("ibis", "ibis-white-linen", ItemCategory.Historical)]
        [TestCase("blue-horse", "blue-horse-black-signature", ItemCategory.Art)]
        [TestCase("obsidian-blade", "obsidian-blade-opaque", ItemCategory.Historical)]
        [TestCase("bronze-statue", "bronze-statue-brown-finish", ItemCategory.Art)]
        [TestCase("glass-vase", "glass-vase-green", ItemCategory.Art)]
        public void MvpFamilyHasTwoPricedVariantsWithDistinctSprites(
            string clueItemId, string otherItemId, ItemCategory category)
        {
            AuctionItem clueItem = level.Database.GetItemById(clueItemId);
            AuctionItem otherItem = level.Database.GetItemById(otherItemId);
            Assert.That(clueItem.ItemType.TypeId, Is.EqualTo(clueItemId));
            Assert.That(level.Database.GetVariantsByType(clueItem.ItemType),
                Is.EquivalentTo(new[] { clueItem, otherItem }));
            Assert.That(level.Clues.Count(clue => clue.ItemId == clueItemId), Is.EqualTo(1));
            Assert.That(clueItem.Categories, Is.EqualTo(new[] { category, ItemCategory.Collectible }));
            Assert.That(otherItem.Categories, Is.EqualTo(clueItem.Categories));
            Assert.That(clueItem.StartingPrice, Is.Positive);
            Assert.That(otherItem.StartingPrice, Is.Positive);
            Assert.That(clueItem.TrueValue, Is.GreaterThan(clueItem.StartingPrice));
            Assert.That(otherItem.TrueValue, Is.InRange(0, otherItem.StartingPrice - 1));
            Assert.That(clueItem.Sprite, Is.Not.Null);
            Assert.That(otherItem.Sprite, Is.Not.Null);
            Assert.That(otherItem.Sprite, Is.Not.SameAs(clueItem.Sprite));
        }

        [Test]
        public void AuctionRunsMvpAssetsAndThreeFavorablePurchasesReachTarget()
        {
            var run = level.CreateRun();
            var host = new GameObject("MVP auction test");
            try
            {
                var auction = host.AddComponent<AuctionManager>();
                var config = new SerializedObject(auction);
                config.FindProperty("enableAutomaticAi").boolValue = false;
                var items = config.FindProperty("items");
                items.arraySize = run.Count;
                for (int index = 0; index < run.Count; index++)
                    items.GetArrayElementAtIndex(index).objectReferenceValue = run[index];
                config.ApplyModifiedPropertiesWithoutUndo();

                var wallet = new SerializedObject(auction.Economy);
                wallet.FindProperty("startingBudget").intValue = level.StartingBudget;
                wallet.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(auction.StartAuction(), Is.True);
                var favorable = run.Where(item => item.TrueValue > item.StartingPrice).ToArray();
                Assert.That(favorable.Length, Is.EqualTo(3));
                Assert.That(run.Sum(item => item.TrueValue - item.StartingPrice), Is.LessThan(level.ProfitTarget));
                foreach (AuctionItem item in run)
                {
                    Assert.That(auction.CurrentItem, Is.SameAs(item));
                    auction.Tick(AuctionManager.RevealDuration);
                    if (favorable.Contains(item))
                        Assert.That(auction.TryPlaceBid(BidderIds.Player, item.StartingPrice), Is.True);
                    auction.Tick(AuctionManager.InitialBiddingTime);
                    auction.Tick(2f);
                }

                Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
                Assert.That(auction.Results.Select(result => result.itemId), Is.EqualTo(run.Select(item => item.ItemId)));
                Assert.That(auction.Economy.Purchases.Select(purchase => purchase.item), Is.EqualTo(favorable));
                Assert.That(auction.Economy.CalculateProfit(),
                    Is.EqualTo(favorable.Sum(item => item.TrueValue - item.StartingPrice)));
                Assert.That(auction.Economy.HasReachedTarget(level.ProfitTarget), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
