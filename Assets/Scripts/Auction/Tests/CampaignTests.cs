using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuctionGame.Auction.Tests
{
    public sealed class CampaignTests
    {
        private static MvpAuctionLevel Level =>
            AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>(EconomyPlaytest.LevelPath);

        [Test]
        public void ApprovedEconomyAndGenerationAcrossOneHundredSeedsPerDay()
        {
            var progress = new CampaignProgress(Level, false);
            var days = Level.CampaignDays;
            Assert.That(days.Select(day => day.StartingBudget), Is.EqualTo(new[] { 400, 500, 650, 800, 950 }));
            Assert.That(days.Select(day => day.DailyValueLimit), Is.EqualTo(new[] { 600, 900, 1200, 1550, 1900 }));
            Assert.That(days.Select(day => day.ProfitTarget), Is.EqualTo(new[] { 25, 60, 100, 180, 300 }));
            Assert.That(days.Select(day => day.ScaleAiBudget(10)), Is.EqualTo(new[] { 7, 9, 10, 11, 12 }));
            Assert.That(days.Select(day => day.MinLots), Is.EqualTo(new[] { 3, 4, 4, 4, 4 }));
            Assert.That(days.Select(day => day.MaxLots), Is.EqualTo(new[] { 4, 6, 8, 0, 0 }));

            for (int day = 1; day <= days.Count; day++)
            {
                var config = days[day - 1];
                int[] counts = new int[100];
                int[] attempts = new int[counts.Length];
                var rng = UnityEngine.Random.state;
                for (int seed = 0; seed < counts.Length; seed++)
                {
                    var plan = progress.Generate(day, seed, out attempts[seed]);
                    counts[seed] = plan.Lots.Count;
                    Assert.That(config.Accepts(plan, Level.PossibleItems), Is.True);
                    Assert.That(plan.Lots.Count, Is.InRange(config.MinLots,
                        config.MaxLots == 0 ? Level.PossibleItems.Count : config.MaxLots));
                    Assert.That(plan.TotalTrueValue, Is.LessThanOrEqualTo(config.DailyValueLimit));
                    Assert.That(plan.Lots.Select(item => item.ItemId), Is.Unique);
                }

                Assert.That(UnityEngine.Random.state, Is.EqualTo(rng));
                if (day == 1)
                    Assert.That(counts.Distinct(), Is.EquivalentTo(new[] { 3, 4 }));
                TestContext.WriteLine($"Day {day}: 100 accepted, {attempts.Sum() - counts.Length} rejected candidates, " +
                    $"0 exhausted seeds, {attempts.Max()} maximum attempts, lots {counts.Min()}–{counts.Max()}.");
            }
        }

        [Test]
        public void ValidationRejectsStructuralErrorsAndUsesTheBestAffordableLots()
        {
            var level = Level;
            var progress = new CampaignProgress(level, false);
            var plan = progress.Generate(1, 0, out _);
            var day = progress.Day;
            var lots = plan.Lots;
            DailyRunPlan WithLots(params AuctionItem[] items) =>
                new(plan.Seed, items, day.DailyValueLimit, plan.NewspaperEntries);

            Assert.That(day.Accepts(WithLots(lots[0], lots[1]), level.PossibleItems), Is.False);
            Assert.That(day.Accepts(WithLots(level.PossibleItems.Take(5).ToArray()), level.PossibleItems), Is.False);
            Assert.That(day.Accepts(WithLots(lots[0], lots[0], lots[1]), level.PossibleItems), Is.False);
            Assert.That(day.Accepts(WithLots(level.PossibleItems.OrderByDescending(item => item.TrueValue)
                .Take(4).ToArray()), level.PossibleItems), Is.False);
            var excluded = level.Database.Items.First(item => !level.PossibleItems.Contains(item));
            Assert.That(day.Accepts(WithLots(lots[0], lots[1], excluded), level.PossibleItems), Is.False);
            Assert.That(CampaignDay.MaximumProfit(lots, 9), Is.Zero);
            Assert.That(CampaignDay.MaximumProfit(lots, 19), Is.EqualTo(lots.Max(item => item.TrueValue) - 10));
            Assert.That(CampaignDay.MaximumProfit(lots, 20),
                Is.EqualTo(lots.OrderByDescending(item => item.TrueValue).Take(2).Sum(item => item.TrueValue - 10)));
        }

        [Test]
        public void RerollUsesFirstFeasibleSeedAndLeavesProgressIntactOnExhaustion()
        {
            var level = Object.Instantiate(Level);
            try
            {
                var progress = new CampaignProgress(level, false);
                progress.Accept(1, progress.Open(0));
                progress.RecordSuccess();
                var config = level.CampaignDays[1];
                var data = new SerializedObject(level);
                var target = data.FindProperty("campaignDays").GetArrayElementAtIndex(1).FindPropertyRelative("profitTarget");
                int BestProfit(int seed)
                {
                    var plan = DailyItemGenerator.Generate(level.PossibleItems, config.DailyValueLimit,
                        seed, level.DailyArticles(), config.MinLots, config.MaxLots);
                    return (int)CampaignDay.MaximumProfit(plan.Lots, config.StartingBudget);
                }

                const int step = unchecked((int)0x9E3779B9u);
                int start = Enumerable.Range(0, 32).First(seed => BestProfit(seed) < BestProfit(unchecked(seed + step)));
                target.intValue = BestProfit(unchecked(start + step));
                data.FindProperty("campaignGenerationAttempts").intValue = 2;
                data.ApplyModifiedPropertiesWithoutUndo();
                var plan = progress.Generate(2, start, out int attempts);
                Assert.That(attempts, Is.EqualTo(2));
                Assert.That(plan.Seed, Is.EqualTo(unchecked(start + step)));
                Assert.That(progress.Generate(2, start, out _).Lots, Is.EqualTo(plan.Lots));
                target.intValue = config.DailyValueLimit + 1;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(() => progress.Generate(2, start, out _),
                    Throws.InvalidOperationException.With.Message.Contains("after 2 attempts"));
                Assert.That(progress.CurrentDay, Is.EqualTo(1));
                Assert.That(progress.HighestUnlockedDay, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        [UnityTest]
        public IEnumerator FinalDayRetainsReportUntilContinueAndCompletesOnce()
        {
            yield return new EnterPlayMode();
            string key = CampaignProgress.SaveKey;
            string previous = PlayerPrefs.GetString(key, null);
            bool hadSave = PlayerPrefs.HasKey(key);
            try
            {
                var expected = new CampaignProgress(Level, false).Generate(5, 456, out _);
                string ids = string.Join(",", expected.Lots.Select(item => "\"" + item.ItemId + "\""));
                PlayerPrefs.SetString(key, $"{{\"currentDay\":5,\"highestUnlockedDay\":5,\"seed\":{expected.Seed},\"lots\":[{ids}]}}");
                GameIntroTestDriver.LoadIntegratedScene(campaign: true);
                PreparationPhase preparation = null;
                for (int frame = 0; frame < 120 && (!preparation || preparation.DailyPlan == null); frame++)
                {
                    yield return null;
                    preparation = Object.FindFirstObjectByType<PreparationPhase>();
                }

                Assert.That(preparation && preparation.DailyPlan != null, Is.True);
                Assert.That(preparation.CurrentDay, Is.EqualTo(5));
                Assert.That(preparation.DailyPlan.Seed, Is.EqualTo(expected.Seed));
                Assert.That(preparation.DailyPlan.Lots, Is.EqualTo(expected.Lots));
                Assert.That(GameObject.Find("Campaign Day").GetComponent<TMP_Text>().text, Is.EqualTo("DAY 5 / 5"));
                var cinematic = preparation.GetComponent<AuctionResultCinematic>();
                cinematic.enabled = false;
                Object.FindFirstObjectByType<SceneIntroOutro>().SkipToEnd();
                var auction = preparation.GetComponent<AuctionManager>();
                var settings = new SerializedObject(auction);
                settings.FindProperty("enableAutomaticAi").boolValue = false;
                settings.FindProperty("paceAiInteractions").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();

                yield return GameIntroTestDriver.OpenNewsFromIntro();
                yield return GameIntroTestDriver.ReadBroadcast();
                Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(950));
                foreach (var bidder in auction.Opponents)
                    Assert.That(bidder.AvailableBudget, Is.EqualTo(
                        Math.Round(bidder.Personality.StartingBudget * 1.20m, MidpointRounding.AwayFromZero)));
                foreach (AuctionItem lot in expected.Lots)
                {
                    cinematic.enabled = lot == expected.Lots.Last();
                    Assert.That(auction.CurrentItem, Is.SameAs(lot));
                    auction.Tick(AuctionManager.RevealDuration);
                    Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
                    auction.Tick(auction.RemainingTime + 2f);
                }

                Assert.That(auction.State, Is.EqualTo(AuctionState.Sold));
                Button resultContinue = null;
                float deadline = Time.realtimeSinceStartup + 8;
                while (Time.realtimeSinceStartup < deadline && (!resultContinue || !resultContinue.interactable))
                {
                    yield return null;
                    resultContinue = GameObject.Find("Continue Result")?.GetComponent<Button>();
                }
                Assert.That(resultContinue && resultContinue.interactable, Is.True);
                Assert.That(GameObject.Find("Result Outcome").GetComponent<TMP_Text>().text, Is.EqualTo("PREDANÉ"));
                resultContinue.onClick.Invoke();
                deadline = Time.realtimeSinceStartup + 8;
                while (auction.State != AuctionState.Finished && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));

                yield return GameIntroTestDriver.OpenFinalReport(true);
                var report = auction.GetComponent<AuctionReportUI>();
                var snapshot = report.Report;
                Assert.That(snapshot.TargetProfit, Is.EqualTo(300));
                Assert.That(preparation.CampaignCompleted, Is.True);
                Assert.That(GameObject.Find("Campaign Complete"), Is.Null);
                int endings = 0;
                preparation.OnCampaignCompletePresented += () => endings++;
                var action = GameObject.Find("Continue Button").GetComponent<Button>();
                action.onClick.Invoke();
                action.onClick.Invoke();
                Assert.That(GameObject.Find("Campaign Complete"), Is.Not.Null);
                Assert.That(endings, Is.EqualTo(1));
                Assert.That(report.Report, Is.SameAs(snapshot));
                Assert.That(preparation.CurrentDay, Is.EqualTo(5));
                Assert.That(preparation.HighestUnlockedDay, Is.EqualTo(5));
                Assert.That(new CampaignProgress(Level, true).Completed, Is.True);

                var finalPlan = preparation.DailyPlan;
                var restart = GameObject.Find("Restart Campaign").GetComponent<Button>();
                restart.onClick.Invoke();
                var freshPlan = preparation.DailyPlan;
                restart.onClick.Invoke();
                Assert.That(preparation.DailyPlan, Is.SameAs(freshPlan));
                Assert.That(freshPlan, Is.Not.SameAs(finalPlan));
                Assert.That(preparation.CurrentDay, Is.EqualTo(1));
                Assert.That(preparation.HighestUnlockedDay, Is.EqualTo(1));
                Assert.That(preparation.CampaignCompleted, Is.False);
                Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
                Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(400));
                Assert.That(auction.Economy.Purchases, Is.Empty);
                Assert.That(report.Report, Is.Null);
                Assert.That(GameObject.Find("Campaign Day").GetComponent<TMP_Text>().text, Is.EqualTo("DAY 1 / 5"));

                var saved = new CampaignProgress(Level, true);
                Assert.That(saved.CurrentDay, Is.EqualTo(1));
                Assert.That(saved.HighestUnlockedDay, Is.EqualTo(1));
                Assert.That(saved.Completed, Is.False);
                Assert.That(saved.Open(0).Lots, Is.EqualTo(freshPlan.Lots));
            }
            finally
            {
                if (hadSave)
                    PlayerPrefs.SetString(key, previous);
                else
                    PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
            yield return new ExitPlayMode();
        }
    }
}
