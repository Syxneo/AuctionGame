using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuctionGame.Auction.Tests
{
    public sealed class DailyItemGenerationTests
    {
        internal const string IntegratedScenePath = "Assets/Scenes/AuctionIntegrated.unity";
        private static MvpAuctionLevel LoadLevel() => AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>(EconomyPlaytest.LevelPath);

        [Test]
        public void ThousandDaysRespectCapUniquenessAndMaximalityAndRepeatBySeed()
        {
            var level = LoadLevel();
            Assert.That(level.Database.Items, Has.Count.EqualTo(26));
            Assert.That(level.PossibleItems, Has.Count.EqualTo(20));
            Assert.That(level.PossibleItems.GroupBy(item => item.ItemType).Select(group => group.Count()),
                Is.All.EqualTo(2));
            Assert.That(level.DailyValueCap, Is.EqualTo(1300));
            var reversedPool = level.PossibleItems.Reverse().ToArray();
            var plans = new List<DailyRunPlan>();
            for (int seed = 0; seed < 1000; seed++)
            {
                var randomState = UnityEngine.Random.state;
                DailyRunPlan plan = level.CreateDailyRun(seed);
                Assert.That(UnityEngine.Random.state, Is.EqualTo(randomState), $"Seed {seed}");
                Assert.That(plan.Lots.Count, Is.GreaterThanOrEqualTo(4), $"Seed {seed}");
                Assert.That(plan.TotalTrueValue, Is.EqualTo(plan.Lots.Sum(item => item.TrueValue)).And.LessThanOrEqualTo(1300));
                Assert.That(plan.Lots.All(level.PossibleItems.Contains), Is.True);
                Assert.That(plan.Lots.Select(item => item.ItemId), Is.Unique);
                Assert.That(level.PossibleItems.Except(plan.Lots).Any(item => item.TrueValue <= plan.UnusedCap), Is.False);
                Assert.That(plan.NewspaperEntries.Select(entry => entry.ItemType),
                    Is.EqualTo(plan.Lots.Select(item => item.ItemType).Distinct()));
                DailyRunPlan replay = level.CreateDailyRun(seed);
                Assert.That(replay.Seed, Is.EqualTo(seed));
                Assert.That(replay.Lots, Is.EqualTo(plan.Lots));
                Assert.That(replay.TotalTrueValue, Is.EqualTo(plan.TotalTrueValue));
                Assert.That(replay.NewspaperEntries, Is.EqualTo(plan.NewspaperEntries));
                plans.Add(plan);
            }

            Assert.That(plans.Select(plan => plan.Lots.Count).Distinct().Count(), Is.GreaterThan(2));
            Assert.That(plans.Any(plan => plan.Lots.Count > 8 && plan.NewspaperEntries.Count > 6), Is.True);
            Assert.That(plans.Any(plan => plan.Lots.Count > plan.NewspaperEntries.Count), Is.True);
            var articles = plans.SelectMany(plan => plan.NewspaperEntries).Distinct().ToArray();
            Assert.That(articles, Has.Length.EqualTo(10));
            var reorderedLevel = Object.Instantiate(level);
            try
            {
                var data = new SerializedObject(reorderedLevel);
                var pool = data.FindProperty("possibleItems");
                for (int index = 0; index < reversedPool.Length; index++)
                    pool.GetArrayElementAtIndex(index).objectReferenceValue = reversedPool[index];
                data.ApplyModifiedPropertiesWithoutUndo();
                var reversed = reorderedLevel.CreateDailyRun(0);
                Assert.That(reversed.Lots, Is.EqualTo(plans[0].Lots));
                Assert.That(reversed.NewspaperEntries.Select(entry => entry.Headline),
                    Is.EqualTo(plans[0].NewspaperEntries.Select(entry => entry.Headline)));
            }
            finally
            {
                Object.DestroyImmediate(reorderedLevel);
            }
            WriteGenerationLog(plans);
        }

        [Test]
        public void TightCapHasFourLotsAndHighCapCanUseTheEntirePool()
        {
            var level = Object.Instantiate(LoadLevel());
            try
            {
                var data = new SerializedObject(level);
                var cap = data.FindProperty("dailyValueCap");
                int cheapestFour = level.PossibleItems.OrderBy(item => item.TrueValue).Take(4).Sum(item => item.TrueValue);
                cap.intValue = cheapestFour;
                data.ApplyModifiedPropertiesWithoutUndo();
                foreach (int seed in Enumerable.Range(0, 20))
                {
                    var plan = level.CreateDailyRun(seed);
                    Assert.That(plan.Lots, Has.Count.EqualTo(4));
                    Assert.That(plan.TotalTrueValue, Is.EqualTo(cheapestFour));
                }
                cap.intValue = level.PossibleItems.Sum(item => item.TrueValue);
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(level.CreateDailyRun(0).Lots, Has.Count.EqualTo(20));
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        [TestCase("empty", "at least 4")]
        [TestCase("three", "at least 4")]
        [TestCase("cap", "must be positive")]
        [TestCase("too low", "cheapest variants exceed")]
        [TestCase("missing", "missing item reference")]
        [TestCase("duplicate", "Duplicate Possible Items ID")]
        [TestCase("zero value", "positive True Value")]
        [TestCase("negative value", "positive True Value")]
        [TestCase("missing id", "missing item reference or ID")]
        [TestCase("missing type", "valid Item Type")]
        [TestCase("missing sprite", "valid Item Type and sprite")]
        [TestCase("missing article", "Missing newspaper article")]
        public void InvalidConfigurationFailsExplicitly(string problem, string message)
        {
            var level = Object.Instantiate(LoadLevel());
            var item = Object.Instantiate(level.PossibleItems[0]);
            try
            {
                var data = new SerializedObject(level);
                var pool = data.FindProperty("possibleItems");
                var first = pool.GetArrayElementAtIndex(0);
                var itemData = new SerializedObject(item);
                switch (problem)
                {
                    case "empty": pool.arraySize = 0; break;
                    case "three": pool.arraySize = 3; break;
                    case "cap": data.FindProperty("dailyValueCap").intValue = 0; break;
                    case "too low": data.FindProperty("dailyValueCap").intValue = 1; break;
                    case "missing": first.objectReferenceValue = null; break;
                    case "duplicate": first.objectReferenceValue = level.PossibleItems[1]; break;
                    case "missing article": data.FindProperty("additionalDailyNewspaperEntries").arraySize = 0; break;
                    default:
                        first.objectReferenceValue = item;
                        switch (problem)
                        {
                            case "zero value": itemData.FindProperty("trueValue").intValue = 0; break;
                            case "negative value": itemData.FindProperty("trueValue").intValue = -1; break;
                            case "missing id": itemData.FindProperty("itemId").stringValue = ""; break;
                            case "missing type": itemData.FindProperty("itemType").objectReferenceValue = null; break;
                            case "missing sprite": itemData.FindProperty("sprite").objectReferenceValue = null; break;
                        }
                        itemData.ApplyModifiedPropertiesWithoutUndo();
                        break;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(() => level.CreateDailyRun(0), Throws.InvalidOperationException.With.Message.Contains(message));
            }
            finally
            {
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(level);
            }
        }

        [Test]
        public void BroadcastSelectionUsesMatchingVariantsAndLeavesUnityRandomAlone()
        {
            var level = LoadLevel();
            var profiles = new[] { "Helena", "Ferko", "Viktor", "Zuzu", "Grumblewick" }
                .Select(name => Resources.Load<AIPersonality>("Auction/Personalities/" + name)).ToArray();
            var edition = new BroadcastEdition();
            var authored = profiles.Select((profile, index) => new BroadcastArticle
            {
                id = "bidder-test-" + index, type = NewsType.Bidder, body = "Test article",
                bidder = profile, category = new[] { ItemCategory.Art, ItemCategory.Silly,
                    ItemCategory.Archeological, ItemCategory.PopCulture, ItemCategory.Fantasy }[index]
            }).ToArray();
            typeof(BroadcastEdition).GetField("articles",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(edition, authored);

            var state = UnityEngine.Random.state;
            try
            {
                int expected = UnityEngine.Random.Range(0, int.MaxValue);
                UnityEngine.Random.state = state;
                var counts = new HashSet<int>();
                bool shuffled = false;
                foreach (var run in Enumerable.Range(0, 30).Select(level.CreateDailyRun))
                {
                    var snapshot = run.Lots.Select(lot => (lot, lot.TrueValue)).ToArray();
                    var selected = edition.Select(run, profiles);
                    var repeated = edition.Select(run, profiles);
                    var families = run.Lots.Select(item => item.ItemType).Distinct().ToArray();
                    counts.Add(run.Lots.Count);
                    Assert.That(selected.Count, Is.EqualTo(families.Length + 3));
                    Assert.That(selected.Select(card => card.Id).Distinct().Count(), Is.EqualTo(selected.Count));
                    Assert.That(repeated.Select(card => card.Id), Is.EqualTo(selected.Select(card => card.Id)));
                    Assert.That(selected.Where(card => card.Type == NewsType.Item).Select(card => card.Item.ItemType),
                        Is.EquivalentTo(families));
                    Assert.That(selected.Where(card => card.Type == NewsType.Bidder).Select(card => card.Bidder).Distinct().Count(),
                        Is.EqualTo(2));
                    Assert.That(selected.Single(card => card.Type == NewsType.World).Id, Is.EqualTo("world-fallback"));
                    foreach (var card in selected.Where(card => card.Type == NewsType.Item))
                    {
                        var clue = run.NewspaperEntries.Single(entry => entry.ItemType == card.Item.ItemType);
                        bool variantPresent = run.Lots.Contains(clue.Variant);
                        Assert.That(card.Body, Is.EqualTo(variantPresent ? clue.BodyText : clue.FamilyFallbackBody));
                        Assert.That(card.Image, Is.SameAs(card.Item.Sprite));
                        if (variantPresent)
                            Assert.That(card.Item, Is.SameAs(clue.Variant));
                    }
                    Assert.That(run.Lots.Select(lot => (lot, lot.TrueValue)), Is.EqualTo(snapshot));
                    shuffled |= selected.Take(families.Length).Any(card => card.Type != NewsType.Item);
                }
                var missing = level.CreateDailyRun(0);
                AuctionItemType missingFamily = missing.Lots[0].ItemType;
                LogAssert.Expect(LogType.Warning, $"Missing authored news clue for '{missingFamily.TypeId}'.");
                var fallbackEdition = edition.Select(missing.Lots,
                    missing.NewspaperEntries.Where(entry => entry.ItemType != missingFamily).ToArray(),
                    profiles, missing.Seed);
                var fallback = fallbackEdition.Single(card => card.Item && card.Item.ItemType == missingFamily);
                Assert.That(fallback.Body, Does.Contain(missingFamily.DisplayName));
                Assert.That(fallback.Image, Is.SameAs(fallback.Item.Sprite));
                Assert.That(counts.Count, Is.GreaterThan(1));
                Assert.That(shuffled, Is.True);
                Assert.That(UnityEngine.Random.Range(0, int.MaxValue), Is.EqualTo(expected));
                var fixture = new EconomyPlaytest(generateDailyRun: true);
                var before = fixture.Run(missing.Seed, EconomyPolicy.SkipAll);
                var originalOrder = edition.Select(missing, profiles).Select(card => card.Id);
                var alternateOrder = edition.Select(missing.Lots, missing.NewspaperEntries, profiles,
                    missing.Seed + 1).Select(card => card.Id);
                Assert.That(alternateOrder, Is.Not.EqualTo(originalOrder));
                var after = fixture.Run(missing.Seed, EconomyPolicy.SkipAll);
                Assert.That(before.Completed && after.Completed, Is.True);
                Assert.That(EconomyPlaytestTests.Fingerprint(after), Is.EqualTo(EconomyPlaytestTests.Fingerprint(before)));
            }
            finally
            {
                UnityEngine.Random.state = state;
            }
        }

        [UnityTest]
        public IEnumerator IntegratedDaySurvivesRetryAndReportsEveryPurchase()
        {
            yield return new EnterPlayMode();
            var level = LoadLevel();
            int seed = Enumerable.Range(0, 100).Select(level.CreateDailyRun)
                .First(plan => plan.NewspaperEntries.Count > 6 && plan.Lots.Count > plan.NewspaperEntries.Count).Seed;
            GameIntroTestDriver.LoadIntegratedScene(seed);
            PreparationPhase preparation = null;
            for (int frame = 0; frame < 120 && (!preparation || preparation.DailyPlan == null); frame++)
            {
                yield return null;
                preparation = Object.FindFirstObjectByType<PreparationPhase>();
            }

            Assert.That(preparation && preparation.DailyPlan != null, Is.True, "Preparation must load the daily run.");
            var cinematic = preparation.GetComponent<AuctionResultCinematic>();
            Assert.That(cinematic != null, Is.True, "The integrated scene has a result cinematic.");
            cinematic.enabled = false;
            var presenter = Object.FindFirstObjectByType<BidderDialoguePresenter>(FindObjectsInactive.Include);
            if (presenter)
                presenter.enabled = false;
            var stage = Object.FindFirstObjectByType<SceneIntroOutro>();
            if (stage)
                stage.SkipToEnd();

            var auction = Object.FindFirstObjectByType<AuctionManager>();
            Assert.That(auction != null, Is.True, "The integrated scene has an active auction manager.");
            var plan = preparation.DailyPlan;
            Assert.That(plan.Seed, Is.EqualTo(seed));
            var edition = preparation.SelectedNews;
            Assert.That(edition, Is.Not.Null, "Preparation must select news for the daily run.");
            var families = plan.Lots.Select(lot => lot.ItemType).Distinct();
            Assert.That(edition.Count, Is.EqualTo(families.Count() + 3));
            Assert.That(edition.Where(card => card.Type == NewsType.Item).Select(card => card.Item.ItemType),
                Is.EquivalentTo(families));
            Assert.That(edition.Count(card => card.Type == NewsType.World), Is.EqualTo(1));
            Assert.That(edition.Select(card => card.Id).Distinct().Count(), Is.EqualTo(edition.Count));
            foreach (NewsCard card in edition.Where(card => card.Type == NewsType.Item && card.Item))
            {
                Assert.That(plan.Lots, Does.Contain(card.Item));
                Assert.That(card.Image, Is.SameAs(card.Item.Sprite));
            }
            var bidders = edition.Where(card => card.Type == NewsType.Bidder).ToArray();
            Assert.That(bidders.Select(card => card.Bidder).Distinct().Count(), Is.EqualTo(2));
            foreach (var card in bidders)
            {
                Assert.That(auction.NewsBidderPool, Does.Contain(card.Bidder));
                string portraitPath = $"Assets/Art/Characters/{card.Bidder.DialogueProfileId}_front.png";
                Assert.That(card.Image, Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>(portraitPath)));
            }
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            var settings = new SerializedObject(auction);
            settings.FindProperty("enableAutomaticAi").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(auction.PacesAiInteractions, Is.True);

            yield return GameIntroTestDriver.OpenNewsFromIntro();
            yield return GameIntroTestDriver.ReadBroadcast();
            Assert.That(auction.DailyPlan, Is.SameAs(plan));
            Assert.That(auction.Opponents, Has.Count.EqualTo(5));
            Assert.That(auction.Opponents.Count(bidder => bidder.IsActiveForLot), Is.EqualTo(3));
            auction.Tick(1000f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(auction.Results.Select(result => result.itemId), Is.EqualTo(plan.Lots.Select(item => item.ItemId)));
            var report = auction.GetComponent<AuctionReportUI>();
            yield return GameIntroTestDriver.OpenFinalReport(false);
            Assert.That(report.Report.Succeeded, Is.False);
            Click("Retry Button");
            Assert.That(preparation.SelectedNews, Is.SameAs(edition));
            Assert.That(preparation.DailyPlan, Is.SameAs(plan));
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(1000));
            Assert.That(auction.Results, Is.Empty);
            settings.Update();
            settings.FindProperty("paceAiInteractions").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();

            yield return GameIntroTestDriver.OpenNewsFromIntro();
            yield return GameIntroTestDriver.ReadBroadcast();
            Assert.That(auction.DailyPlan, Is.SameAs(plan));
            foreach (AuctionItem item in plan.Lots)
            {
                Assert.That(auction.CurrentItem, Is.SameAs(item));
                auction.Tick(AuctionManager.RevealDuration);
                Assert.That(auction.CurrentPrice, Is.Zero);
                Assert.That(auction.TryPlaceBid(BidderIds.Player, 0), Is.False);
                Click("+$10 Button");
                auction.Tick(auction.RemainingTime + 2f);
            }

            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(auction.Results.Select(result => result.itemId), Is.EqualTo(plan.Lots.Select(item => item.ItemId)));
            Assert.That(auction.Results.Select(result => result.finalPrice), Is.All.EqualTo(10));
            Assert.That(auction.Economy.Purchases.Select(purchase => purchase.Variant), Is.EqualTo(plan.Lots));
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(1000 - 10 * plan.Lots.Count));
            yield return GameIntroTestDriver.OpenFinalReport(true);
            Assert.That(report.Report.Purchases, Has.Count.EqualTo(plan.Lots.Count));
            Assert.That(report.Report.TotalProfit, Is.EqualTo(plan.TotalTrueValue - 10 * plan.Lots.Count));
            Assert.That(report.Report.TargetProfit, Is.EqualTo(50));
            var bar = Object.FindFirstObjectByType<AuctionPurchaseBar>(FindObjectsInactive.Include);
            Assert.That(bar.GetComponentsInChildren<Image>(true).Where(image => image.enabled).Select(image => image.sprite),
                Is.EqualTo(plan.Lots.Select(item => item.Sprite)));
            Assert.That(auction.Economy.Purchases.Select(purchase => purchase.item), Is.EqualTo(plan.Lots));
            Click("Continue Button");
            Assert.That(GameObject.Find("Level Completion"), Is.Not.Null);
            yield return new ExitPlayMode();
        }

        private static void Click(string name)
        {
            var button = GameObject.Find(name)?.GetComponent<Button>();
            Assert.That(button && button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }

        private static void WriteGenerationLog(IEnumerable<DailyRunPlan> plans)
        {
            string directory = Path.Combine(Application.dataPath, "..", "Logs", "DailyGeneration");
            Directory.CreateDirectory(directory);
            var rows = plans.Select(plan => string.Join(",", plan.Seed, plan.Lots.Count, plan.NewspaperEntries.Count,
                plan.TotalTrueValue, plan.UnusedCap, plan.Lots.Count(item => item.ItemId.EndsWith("_a", StringComparison.Ordinal)),
                plan.Lots.Count(item => item.ItemId.EndsWith("_b", StringComparison.Ordinal)),
                plan.Lots.Count(item => item.ItemType.Role == ItemRole.Critical),
                plan.Lots.Count(item => item.ItemType.Role == ItemRole.Fluff),
                plan.Lots.Count(item => item.TrueValue > AuctionManager.MinimumOpeningBid),
                string.Join("|", plan.Lots.Select(item => $"{item.ItemId}={item.TrueValue}")),
                string.Join("|", plan.NewspaperEntries.Select(entry => entry.ItemType.TypeId))));
            File.WriteAllLines(Path.Combine(directory, "generation.csv"), new[]
            {
                "seed,lots,families,totalValue,unusedCap,a,b,critical,fluff,optimisticProfitableLots,orderedLots,articles"
            }.Concat(rows));
        }
    }
}
