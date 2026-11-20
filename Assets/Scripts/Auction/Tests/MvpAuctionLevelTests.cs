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
            Assert.That(level.Database.Items, Has.Count.EqualTo(18));
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
        public void LevelReturnsSixOrderedVariantsAndCluesResolveAgainstDatabase()
        {
            Assert.That(level.StartingBudget, Is.EqualTo(1000));
            Assert.That(level.ProfitTarget, Is.EqualTo(300));
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

        [Test]
        public void VariantsStoreIndependentPricesValuesAndSprites()
        {
            AuctionItem first = Object.Instantiate(level.Database.GetItemById("clay-tablet"));
            AuctionItem second = Object.Instantiate(level.Database.GetItemById("clay-tablet-polished-gray"));
            var texture = new Texture2D(2, 2);
            Sprite firstSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            Sprite secondSprite = Sprite.Create(texture, new Rect(1, 0, 1, 1), Vector2.zero);
            try
            {
                Configure(first, 40, 230, firstSprite);
                Configure(second, 20, 25, secondSprite);
                Assert.That(first.ItemType, Is.SameAs(second.ItemType));
                Assert.That(first.StartingPrice, Is.EqualTo(40));
                Assert.That(second.StartingPrice, Is.EqualTo(20));
                Assert.That(first.TrueValue, Is.EqualTo(230));
                Assert.That(second.TrueValue, Is.EqualTo(25));
                Assert.That(first.Sprite, Is.SameAs(firstSprite));
                Assert.That(second.Sprite, Is.SameAs(secondSprite));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(firstSprite);
                Object.DestroyImmediate(secondSprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void AuctionStartsWithProductionVariantsAndFullCatalogInLevelOrder()
        {
            var run = level.CreateRun();
            Assert.That(run, Has.Count.EqualTo(6));
            var mvpVariants = run.SelectMany(item => level.Database.GetVariantsByType(item.ItemType))
                .Distinct().ToArray();
            Assert.That(mvpVariants, Has.Length.EqualTo(12));
            foreach (AuctionItem variant in mvpVariants)
            {
                Assert.That(variant.StartingPrice, Is.Positive, variant.ItemId);
                Assert.That(variant.TrueValue, Is.GreaterThanOrEqualTo(0), variant.ItemId);
            }

            var host = new GameObject("MVP auction run test");
            try
            {
                var auction = host.AddComponent<AuctionManager>();
                Assert.That(auction.StartAuction(run, level.Database), Is.True);
                Assert.That(auction.CurrentItem, Is.SameAs(run[0]));
                auction.Tick(AuctionManager.RevealDuration);
                Assert.That(auction.State, Is.EqualTo(AuctionState.Bidding));
                Assert.That(auction.Opponents, Has.Count.EqualTo(3));
                auction.Tick(1000);
                Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
                Assert.That(auction.Results.Select(result => result.itemId), Is.EqualTo(run.Select(item => item.ItemId)));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AuctionAcceptsProductionVariantAndReportUsesItsValue()
        {
            AuctionItem variant = level.CreateRun()[0];
            var host = new GameObject("MVP variant auction test");
            try
            {
                var auction = host.AddComponent<AuctionManager>();
                var config = new SerializedObject(auction);
                config.FindProperty("enableAutomaticAi").boolValue = false;
                config.FindProperty("itemDatabase").objectReferenceValue = level.Database;
                var items = config.FindProperty("items");
                items.arraySize = 1;
                items.GetArrayElementAtIndex(0).objectReferenceValue = variant;
                config.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(auction.StartAuction(), Is.True);
                Assert.That(auction.CurrentItem, Is.SameAs(variant));
                auction.Tick(AuctionManager.RevealDuration);
                Assert.That(auction.TryPlaceBid(BidderIds.Player, variant.StartingPrice), Is.True);
                auction.Tick(AuctionManager.InitialBiddingTime);
                Assert.That(auction.Results.Single().itemId, Is.EqualTo(variant.ItemId));
                Assert.That(auction.Economy.Purchases.Single().item, Is.SameAs(variant));
                Assert.That(auction.Economy.CalculateProfit(), Is.EqualTo(variant.TrueValue - variant.StartingPrice));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static void Configure(AuctionItem item, int price, int value, Sprite sprite)
        {
            var data = new SerializedObject(item);
            data.FindProperty("startingPrice").intValue = price;
            data.FindProperty("trueValue").intValue = value;
            data.FindProperty("sprite").objectReferenceValue = sprite;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
