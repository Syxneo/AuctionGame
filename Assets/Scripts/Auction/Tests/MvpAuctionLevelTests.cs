using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
        }

        [Test]
        public void CatalogHasUniqueDefinitionsAndOneCluePerItem()
        {
            Assert.That(level.StartingBudget, Is.EqualTo(1000));
            Assert.That(level.ProfitTarget, Is.EqualTo(300));
            Assert.That(level.Definitions.Select(item => item.ItemId).Distinct().Count(), Is.EqualTo(6));
            Assert.That(level.Clues.Select(clue => clue.ClueKey).Distinct().Count(), Is.EqualTo(6));
            Assert.That(level.Clues.Count(clue => clue.Importance == NewspaperImportance.MainArticle), Is.EqualTo(3));
            foreach (AuctionItem item in level.Definitions)
            {
                NewspaperClue clue = level.Clues.Single(entry => entry.ItemId == item.ItemId);
                Assert.That(clue.ClueKey, Is.EqualTo(item.NewspaperClueKey));
                Assert.That(clue.Headline, Is.Not.Empty);
                Assert.That(clue.Body, Is.Not.Empty);
            }
        }

        [Test]
        public void SeededRunsKeepOneFakePerRoleAndCanVary()
        {
            var first = level.CreateRun(1729);
            var authenticities = first.Select(item => item.Authenticity).ToArray();
            var repeat = level.CreateRun(1729);
            Assert.That(repeat.Select(item => item.Authenticity), Is.EqualTo(authenticities));
            Assert.That(Enumerable.Range(0, 32).Any(seed =>
                !level.CreateRun(seed).Select(item => item.Authenticity).SequenceEqual(authenticities)), Is.True);
            Assert.That(first.Select(item => item.Authenticity), Is.EqualTo(authenticities));
            Assert.That(first.Count, Is.EqualTo(6));
            foreach (ItemRole role in new[] { ItemRole.Critical, ItemRole.Fluff })
            {
                var group = first.Where(item => item.Definition.Role == role).ToArray();
                Assert.That(group.Count(item => item.Authenticity == ItemAuthenticity.Fake), Is.EqualTo(1));
                Assert.That(group.Count(item => item.Authenticity == ItemAuthenticity.Genuine), Is.EqualTo(2));
            }
        }

        [Test]
        public void VariantResolvesSpriteAndValueWhileAiAndClueIdentityStayTheSame()
        {
            AuctionItem definition = Object.Instantiate(level.Definitions[0]);
            var texture = new Texture2D(2, 2);
            var genuine = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            var fake = Sprite.Create(texture, new Rect(1, 0, 1, 1), Vector2.zero);
            MvpAuctionLevel copy = Object.Instantiate(level);
            try
            {
                var itemData = new SerializedObject(definition);
                itemData.FindProperty("startingPrice").intValue = 40;
                itemData.FindProperty("genuineTrueValue").intValue = 230;
                itemData.FindProperty("fakeTrueValue").intValue = 90;
                itemData.FindProperty("genuineSprite").objectReferenceValue = genuine;
                itemData.FindProperty("fakeSprite").objectReferenceValue = fake;
                itemData.ApplyModifiedPropertiesWithoutUndo();
                var levelData = new SerializedObject(copy);
                levelData.FindProperty("definitions").GetArrayElementAtIndex(0).objectReferenceValue = definition;
                levelData.ApplyModifiedPropertiesWithoutUndo();
                var runs = Enumerable.Range(0, 32).Select(seed => copy.CreateRun(seed)[0]).ToArray();
                var original = runs.First(item => item.Authenticity == ItemAuthenticity.Genuine);
                var imitation = runs.First(item => item.Authenticity == ItemAuthenticity.Fake);
                Assert.That(original.DisplaySprite, Is.SameAs(genuine));
                Assert.That(imitation.DisplaySprite, Is.SameAs(fake));
                Assert.That(original.ActualTrueValue, Is.EqualTo(230));
                Assert.That(imitation.ActualTrueValue, Is.EqualTo(90));
                Assert.That(original.StartingPrice, Is.EqualTo(40));
                Assert.That(imitation.StartingPrice, Is.EqualTo(40));
                Assert.That(original.AiReferenceValue, Is.EqualTo(imitation.AiReferenceValue));
                Assert.That(original.ItemId, Is.EqualTo(imitation.ItemId));
                Assert.That(original.Definition.NewspaperClueKey, Is.EqualTo(imitation.Definition.NewspaperClueKey));
                itemData.FindProperty("genuineSprite").objectReferenceValue = null;
                itemData.FindProperty("fakeSprite").objectReferenceValue = null;
                itemData.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(copy.CreateRun(1729)[0].DisplaySprite, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(genuine);
                Object.DestroyImmediate(fake);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
