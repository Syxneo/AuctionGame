using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuctionGame.Auction.Tests
{
    public sealed class AuctionManagerTests
    {
        private readonly List<Object> objects = new();
        private AuctionManager auction;

        [SetUp]
        public void SetUp() => auction = CreateAuction();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in objects)
                Object.DestroyImmediate(obj);
            objects.Clear();
        }

        [Test]
        public void StartRevealsItemOnceAndRejectsBidsUntilTwoSeconds()
        {
            int reveals = 0;
            auction.OnItemChanged += _ => reveals++;
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            Assert.That(auction.StartAuction(), Is.True);
            Assert.That(auction.StartAuction(), Is.False);
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.False);
            auction.Tick(1.75f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            auction.Tick(0.25f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Bidding));
            Assert.That(auction.RemainingTime, Is.EqualTo(15));
            Assert.That(reveals, Is.EqualTo(1));
        }

        [Test]
        public void FirstBidMustMatchOpeningPrice()
        {
            BeginBidding();
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 90), Is.False);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 110), Is.False);
            Assert.That(auction.TryPlaceBid("unknown", 100), Is.False);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
        }

        [TestCase(10, true)]
        [TestCase(25, true)]
        [TestCase(50, true)]
        [TestCase(0, false)]
        [TestCase(15, false)]
        [TestCase(100, false)]
        public void OnlySupportedIncrementsAreAccepted(int increment, bool accepted)
        {
            BeginBidding();
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 100), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100 + increment), Is.EqualTo(accepted));
            Assert.That(auction.CurrentPrice, Is.EqualTo(accepted ? 100 + increment : 100));
        }

        [TestCase(BidderIds.Player)]
        [TestCase(BidderIds.Collector)]
        [TestCase(BidderIds.Trickster)]
        [TestCase(BidderIds.Analyst)]
        public void LeaderCannotOutbidItself(string bidder)
        {
            BeginBidding();
            Assert.That(auction.TryPlaceBid(bidder, 100), Is.True);
            Assert.That(auction.TryPlaceBid(bidder, 110), Is.False);
            Assert.That(auction.BidHistory.Count, Is.EqualTo(1));
        }

        [Test]
        public void BidAboveBudgetIsRejected()
        {
            auction = CreateAuction(budget: 100);
            BeginBidding();
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 100), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 110), Is.False);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(100));
        }

        [Test]
        public void BidAboveAiMaximumIsRejected()
        {
            auction = CreateAuction(value: 1);
            ConfigureCollector(accuracy: 1);
            BeginBidding();
            Assert.That(auction.TryGetMaxBid(BidderIds.Collector, out int max), Is.True);
            Assert.That(max, Is.LessThan(100));
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 100), Is.False);
        }

        [TestCase(1f, 40)]
        [TestCase(0f, 400)]
        public void EvaluationUsesActualVariantOrMatchingVariantOutsideTheRun(float accuracy, int expected)
        {
            ConfigureCollector(accuracy);
            AuctionItem current = CreateItem("current", 40, category: ItemCategory.Art);
            AuctionItem sameId = CreateItem("current", 800);
            AuctionItem match = CreateItem("match", 400, category: ItemCategory.Coins);
            AuctionItem unrelated = CreateItem("unrelated", 700, category: ItemCategory.Art);
            SetCategories(current, ItemCategory.Art, ItemCategory.Historical);
            SetCategories(sameId, ItemCategory.Historical);
            SetCategories(match, ItemCategory.Coins, ItemCategory.Historical);
            SetCategories(unrelated, ItemCategory.Technology);
            var unpriced = CreateItem("unpriced", -1);
            var database = CreateDatabase(sameId, match, unrelated, unpriced);
            Assert.That(auction.StartAuction(new[] { current }, database), Is.True);
            auction.Tick(2);

            AIBidder collector = auction.Opponents[0];
            Assert.That(collector.EstimatedValue, Is.EqualTo(expected));
            int max = collector.MaxBid;
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
            auction.Tick(5);
            Assert.That(collector.EstimatedValue, Is.EqualTo(expected));
            Assert.That(collector.MaxBid, Is.EqualTo(max));
        }

        [TestCase(1f, 2000, 2500)]
        [TestCase(0f, 300, 500)]
        public void FailedEvaluationWithoutAlternativesUsesSeededMultiplier(float probability, int min, int max)
        {
            ConfigureCollector(accuracy: 0);
            var config = new SerializedObject(auction);
            config.FindProperty("collector").FindPropertyRelative("overestimateProbability").floatValue = probability;
            config.ApplyModifiedPropertiesWithoutUndo();
            var current = CreateItem("current", 1000);
            var database = CreateDatabase(current, CreateItem("not-a-match", 600, ItemCategory.Art));
            var run = new[] { current };

            Assert.That(auction.StartAuction(run, database), Is.True);
            auction.Tick(2);
            int estimate = auction.Opponents[0].EstimatedValue;
            Assert.That(estimate, Is.InRange(min, max));
            auction.Tick(100);
            Assert.That(auction.ResetLevel(), Is.True);
            Assert.That(auction.StartAuction(run, database), Is.True);
            auction.Tick(2);
            Assert.That(auction.Opponents[0].EstimatedValue, Is.EqualTo(estimate));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InterestBonusesApplyOnceEvenWithMultipleMatches(bool multipleMatches)
        {
            ConfigureCollector(accuracy: 1);
            var config = new SerializedObject(auction);
            config.FindProperty("collector").FindPropertyRelative("aggressiveness").floatValue = 0.5f;
            config.ApplyModifiedPropertiesWithoutUndo();
            var current = CreateItem("current", 400);
            SetCategories(current, ItemCategory.Historical,
                multipleMatches ? ItemCategory.Luxury : ItemCategory.Technology);
            Assert.That(auction.StartAuction(new[] { current }, CreateDatabase(current)), Is.True);
            auction.Tick(2);

            var collector = auction.Opponents[0];
            Assert.That(collector.EstimatedValue, Is.EqualTo(400));
            Assert.That(collector.MaxBid, Is.EqualTo(420));
            var probability = typeof(AIBidder).GetField("bidProbability", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((double)probability.GetValue(collector), Is.EqualTo(0.81).Within(0.0001));
            auction.Tick(5);
            Assert.That(collector.MaxBid, Is.EqualTo(420));
        }

        [TestCase(40, -70)]
        [TestCase(300, 190)]
        public void AiSettlementUsesActualVariantAndCarriesBudgetUntilLevelReset(int actualValue, long profit)
        {
            ConfigureCollector(accuracy: 0, budget: 200);
            ConfigureProfile("analyst", accuracy: 0, budget: 600);
            var first = CreateItem("first", actualValue);
            var second = CreateItem("second", 500, ItemCategory.Art);
            var comparable = CreateItem("comparable", 500);
            var comparableArt = CreateItem("comparable-art", 500, category: ItemCategory.Art);
            var run = new[] { first, second };
            var catalog = CreateDatabase(first, second, comparable, comparableArt);
            Assert.That(auction.StartAuction(run, catalog), Is.True);
            auction.Tick(2);

            AIBidder collector = auction.Opponents[0];
            AIBidder analyst = auction.Opponents[2];
            Assert.That(auction.TryPlaceBid(BidderIds.Analyst, 100), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 110), Is.True);
            Assert.That(collector.AvailableBudget, Is.EqualTo(200));
            auction.OnAuctionEnded += _ => auction.Tick(1000);
            auction.Tick(15);
            Assert.That(collector.AvailableBudget, Is.EqualTo(90));
            Assert.That(analyst.AvailableBudget, Is.EqualTo(600));
            Assert.That(analyst.Purchases, Is.Empty);
            AIPurchase purchase = collector.Purchases.Single();
            Assert.That(purchase.ItemId, Is.EqualTo("first"));
            Assert.That(purchase.Item, Is.SameAs(first));
            Assert.That(purchase.Variant, Is.SameAs(first));
            Assert.That(purchase.PurchasePrice, Is.EqualTo(110));
            Assert.That(purchase.ItemProfit, Is.EqualTo(profit));
            Assert.That(collector.TotalProfit, Is.EqualTo(profit));

            auction.Tick(4);
            Assert.That(collector.MaxBid, Is.GreaterThan(100));
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 100), Is.False);
            auction.Tick(17);
            Assert.That(collector.Purchases.Count, Is.EqualTo(1));
            Assert.That(collector.AvailableBudget, Is.EqualTo(90));
            Assert.That(auction.ResetLevel(), Is.True);
            Assert.That(auction.StartAuction(run, catalog), Is.True);
            Assert.That(auction.Opponents[0].AvailableBudget, Is.EqualTo(200));
            Assert.That(auction.Opponents[0].Purchases, Is.Empty);
            Assert.That(auction.Opponents[0].TotalProfit, Is.Zero);
        }

        [Test]
        public void LateInvalidBidChangesNothingAndValidBidResetsClock()
        {
            BeginBidding();
            int published = 0;
            auction.OnBidPlaced += _ => published++;
            auction.Tick(14f);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 110), Is.False);
            Assert.That(auction.RemainingTime, Is.EqualTo(1));
            Assert.That(auction.CurrentPrice, Is.Zero);
            Assert.That(auction.HighestBidderId, Is.Empty);
            Assert.That(auction.Results, Is.Empty);
            Assert.That(auction.BidHistory, Is.Empty);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(600));
            Assert.That(published, Is.Zero);

            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
            Assert.That(auction.RemainingTime, Is.EqualTo(3));
            Assert.That(published, Is.EqualTo(1));
        }

        [Test]
        public void HardCapOverridesRepeatedLateBids()
        {
            BeginBidding();
            auction.Tick(14);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
            for (int index = 1; index <= 7; index++)
            {
                auction.Tick(2);
                string bidder = index % 2 == 1 ? BidderIds.Collector : BidderIds.Player;
                Assert.That(auction.TryPlaceBid(bidder, 100 + index * 10), Is.True);
            }

            auction.Tick(1);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 180), Is.True);
            Assert.That(auction.RemainingTime, Is.EqualTo(1));
            auction.Tick(1);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Sold));
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 190), Is.False);
            Assert.That(auction.Results.Single().finalPrice, Is.EqualTo(180));
        }

        [Test]
        public void ExpiredUnbidItemHasEmptyWinnerAndZeroPrice()
        {
            BeginBidding();
            auction.Tick(15);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Unsold));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.False);
            AuctionResult result = auction.Results.Single();
            Assert.That(result.sold, Is.False);
            Assert.That(result.winnerId, Is.Empty);
            Assert.That(result.finalPrice, Is.Zero);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(600));
        }

        [Test]
        public void PlayerPaysOnceOnlyOnWinningAndCompletionFiresOnce()
        {
            BeginBidding();
            int closed = 0;
            int finished = 0;
            auction.OnAuctionEnded += _ =>
            {
                closed++;
                auction.Tick(100);
                Assert.That(auction.TryPlaceBid(BidderIds.Collector, 110), Is.False);
            };
            auction.OnAuctionPhaseFinished += () => finished++;

            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(600));
            Assert.That(auction.Economy.Purchases, Is.Empty);
            auction.Tick(15);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(500));
            Assert.That(auction.Economy.Purchases.Single().purchasePrice, Is.EqualTo(100));
            Assert.That(auction.Results.Single().winnerId, Is.EqualTo(BidderIds.Player));
            Assert.That(auction.Economy.RecordPurchase(auction.CurrentItem, 100), Is.False);

            auction.Tick(2);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            auction.Tick(1000);
            auction.Tick(1000);
            Assert.That(auction.Results.Count, Is.EqualTo(1));
            Assert.That(auction.Economy.Purchases.Count, Is.EqualTo(1));
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(finished, Is.EqualTo(1));
        }

        [Test]
        public void AiVictoryLeavesPlayerBudgetUntouched()
        {
            BeginBidding();
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 110), Is.True);
            auction.Tick(15);
            Assert.That(auction.Results.Single().winnerId, Is.EqualTo(BidderIds.Collector));
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(600));
            Assert.That(auction.Economy.Purchases, Is.Empty);
        }

        [Test]
        public void LargeTickProcessesAllSixItemsInOrder()
        {
            auction = CreateAuction(count: 6);
            var states = new List<AuctionState>();
            auction.OnStateChanged += states.Add;
            auction.StartAuction();
            auction.Tick(1000);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(auction.Results.Select(result => result.itemId),
                Is.EqualTo(Enumerable.Range(0, 6).Select(index => $"item-{index}")));
            Assert.That(states.Count(state => state == AuctionState.ItemReveal), Is.EqualTo(6));
        }

        [Test]
        public void FixedSeedProducesIdenticalBidsAndResultsAcrossStepSizes()
        {
            var large = RunAiAuction(1000);
            var small = RunAiAuction(0.125f);
            Assert.That(large.bids.Count, Is.GreaterThan(6));
            Assert.That(large.bids, Is.EqualTo(small.bids));
            Assert.That(large.results, Is.EqualTo(small.results));
            Assert.That(large.estimates, Is.EqualTo(small.estimates));
        }

        [Test]
        public void TricksterCommentIsPublishedWithTheAcceptedBid()
        {
            BeginBidding();
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 100), Is.True);
            Assert.That(auction.BidHistory.Single().comment, Is.Not.Empty);
        }

        [TestCase(14.75, true)]
        [TestCase(15, false)]
        public void AiCanExtendBiddingBeforeDeadlineButLosesExactTie(double decisionTime, bool expectedBid)
        {
            auction = CreateAuction(automaticAi: true);
            BeginBidding();
            // Force only the deadline-adjacent decision, independently of the seed.
            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            var opponents = (Array)typeof(AuctionManager).GetField("opponents", privateInstance).GetValue(auction);
            for (int index = 0; index < opponents.Length; index++)
            {
                object opponent = opponents.GetValue(index);
                opponent.GetType().GetProperty("NextDecision", privateInstance).SetValue(opponent, index == 0 ? decisionTime : 30d);
                opponent.GetType().GetField("bidProbability", privateInstance).SetValue(opponent, 1d);
            }

            auction.Tick(15);
            Assert.That(auction.BidHistory.Count, Is.EqualTo(expectedBid ? 1 : 0));
            Assert.That(auction.State, Is.EqualTo(expectedBid ? AuctionState.Bidding : AuctionState.Unsold));
            Assert.That(auction.RemainingTime, Is.EqualTo(expectedBid ? 2.75f : 0));
        }

        [Test]
        public void ProfitSumsGainsAndLossesWithoutUnusedBudget()
        {
            auction = CreateAuction(count: 2, value: 160);
            AuctionItem loss = CreateItem("loss", 40);
            BeginBidding();
            Assert.That(auction.Economy.CalculateProfit(), Is.Zero);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
            auction.Tick(15);
            Assert.That(auction.Economy.RecordPurchase(loss, 70), Is.True);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(430));
            Assert.That(auction.Economy.CalculateProfit(), Is.EqualTo(30));
            Assert.That(auction.Economy.HasReachedTarget(30), Is.True);
            Assert.That(auction.Economy.HasReachedTarget(31), Is.False);
        }

        [Test]
        public void ReportClassifiesPurchasesAndComparesTheirTotalWithTheTarget()
        {
            var first = CreateItem("gain", 450);
            Assert.That(auction.Economy.RecordPurchase(first, 100), Is.True);
            Assert.That(auction.Economy.RecordPurchase(CreateItem("loss", 50), 100), Is.True);
            Assert.That(auction.Economy.RecordPurchase(CreateItem("even", 100), 100), Is.True);

            var report = new AuctionReport(auction.Economy.Purchases, 300);
            Assert.That(report.Purchases.Select(row => row.Outcome), Is.EqualTo(new[]
                { PurchaseOutcome.GoodBuy, PurchaseOutcome.Overpaid, PurchaseOutcome.BreakEven }));
            Assert.That(report.Purchases.Select(row => row.Profit), Is.EqualTo(new long[] { 350, -50, 0 }));
            Assert.That(report.Purchases[0].TrueValue, Is.EqualTo(450));
            Assert.That(report.Purchases[0].PurchasePrice, Is.EqualTo(100));
            Assert.That(report.TotalProfit, Is.EqualTo(300));
            Assert.That(report.Succeeded, Is.True);
            Assert.That(new AuctionReport(auction.Economy.Purchases, 301).Succeeded, Is.False);

            Assert.That(auction.Economy.RecordPurchase(CreateItem("later", 0), 100), Is.True);
            Assert.That(report.Purchases.Count, Is.EqualTo(3));
            Assert.That(report.TotalProfit, Is.EqualTo(300));
        }

        [Test]
        public void EmptyReportHasZeroProfitDespiteFullBudget()
        {
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(600));
            var report = new AuctionReport(auction.Economy.Purchases, 300);
            Assert.That(report.Purchases, Is.Empty);
            Assert.That(report.TotalProfit, Is.Zero);
            Assert.That(report.Succeeded, Is.False);
        }

        [Test]
        public void FinishedReportExcludesAiPurchasesAndRetryRestoresTheSameRunAndAllBudgets()
        {
            var playerLot = CreateItem("player-lot", 400);
            var aiLot = CreateItem("ai-lot", 500);
            Assert.That(auction.StartAuction(new[] { playerLot, aiLot }, CreateDatabase(playerLot, aiLot)), Is.True);
            auction.Tick(AuctionManager.RevealDuration);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
            auction.Tick(19);
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 100), Is.True);
            auction.Tick(17);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 110), Is.False);

            var report = new AuctionReport(auction.Economy.Purchases, 301);
            Assert.That(report.Purchases.Single().ItemName, Is.EqualTo(playerLot.ItemName));
            Assert.That(report.TotalProfit, Is.EqualTo(300));
            Assert.That(report.Succeeded, Is.False);
            auction.Tick(100);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(500));
            Assert.That(auction.Opponents[0].AvailableBudget, Is.EqualTo(500));
            Assert.That(auction.Opponents[0].Purchases.Count, Is.EqualTo(1));

            Assert.That(auction.RestartAuction(), Is.True);
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            Assert.That(auction.CurrentItem, Is.SameAs(playerLot));
            Assert.That(auction.Results, Is.Empty);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(600));
            Assert.That(auction.Economy.Purchases, Is.Empty);
            Assert.That(auction.Opponents.Count, Is.EqualTo(3));
            Assert.That(auction.Opponents.All(opponent =>
                opponent.AvailableBudget == 600 && opponent.Purchases.Count == 0), Is.True);
            Assert.That(report.TotalProfit, Is.EqualTo(300));
        }

        [Test]
        public void DuplicateItemIdsPreventStart()
        {
            var serialized = new SerializedObject(auction);
            var items = serialized.FindProperty("items");
            items.arraySize = 2;
            items.GetArrayElementAtIndex(1).objectReferenceValue = items.GetArrayElementAtIndex(0).objectReferenceValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(auction.StartAuction(), Is.False);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
        }

        private void BeginBidding()
        {
            Assert.That(auction.StartAuction(), Is.True);
            auction.Tick(2);
        }

        private (List<(string, string, int, double)> bids, string[] results, List<int> estimates) RunAiAuction(float step)
        {
            var manager = CreateAuction(count: 6, automaticAi: true);
            var bids = new List<(string, string, int, double)>();
            var estimates = new List<int>();
            manager.OnBidPlaced += bid => bids.Add((bid.itemId, bid.bidderId, bid.amount, bid.biddingElapsed));
            manager.OnStateChanged += state =>
            {
                if (state == AuctionState.Bidding)
                    estimates.AddRange(manager.Opponents.Select(opponent => opponent.EstimatedValue));
            };
            manager.StartAuction();
            for (int iteration = 0; iteration * step <= 1000 && manager.State != AuctionState.Finished; iteration++)
                manager.Tick(step);

            Assert.That(manager.State, Is.EqualTo(AuctionState.Finished));
            return (bids, manager.Results.Select(result => $"{result.itemId}/{result.winnerId}/{result.finalPrice}").ToArray(), estimates);
        }

        private AuctionManager CreateAuction(int count = 1, int budget = 600, int value = 1000, bool automaticAi = false)
        {
            var gameObject = new GameObject("Auction test");
            objects.Add(gameObject);
            var manager = gameObject.AddComponent<AuctionManager>();
            var config = new SerializedObject(manager);
            config.FindProperty("enableAutomaticAi").boolValue = automaticAi;
            var items = config.FindProperty("items");
            items.arraySize = count;
            for (int index = 0; index < count; index++)
                items.GetArrayElementAtIndex(index).objectReferenceValue = CreateItem($"item-{index}", value + index * 50);
            config.FindProperty("itemDatabase").objectReferenceValue =
                CreateDatabase(CreateItem("comparable-test-item", value));
            config.ApplyModifiedPropertiesWithoutUndo();

            var wallet = new SerializedObject(manager.Economy);
            wallet.FindProperty("startingBudget").intValue = budget;
            wallet.ApplyModifiedPropertiesWithoutUndo();
            return manager;
        }

        private ItemDatabase CreateDatabase(params AuctionItem[] items)
        {
            var database = ScriptableObject.CreateInstance<ItemDatabase>();
            objects.Add(database);
            var data = new SerializedObject(database);
            var catalog = data.FindProperty("items");
            catalog.arraySize = items.Length;
            for (int index = 0; index < items.Length; index++)
                catalog.GetArrayElementAtIndex(index).objectReferenceValue = items[index];
            data.ApplyModifiedPropertiesWithoutUndo();
            return database;
        }

        private void SetCategories(AuctionItem item, params ItemCategory[] categories)
        {
            var type = ScriptableObject.CreateInstance<AuctionItemType>();
            objects.Add(type);
            var typeData = new SerializedObject(type);
            var tags = typeData.FindProperty("categories");
            tags.arraySize = categories.Length;
            for (int index = 0; index < categories.Length; index++)
                tags.GetArrayElementAtIndex(index).enumValueIndex = (int)categories[index];
            typeData.ApplyModifiedPropertiesWithoutUndo();

            var itemData = new SerializedObject(item);
            itemData.FindProperty("itemType").objectReferenceValue = type;
            itemData.ApplyModifiedPropertiesWithoutUndo();
        }

        private void ConfigureCollector(float accuracy, int budget = 600) =>
            ConfigureProfile("collector", accuracy, budget);

        private void ConfigureProfile(string name, float accuracy, int budget)
        {
            var settings = new SerializedObject(auction);
            var profile = settings.FindProperty(name);
            profile.FindPropertyRelative("evaluationAccuracy").floatValue = accuracy;
            profile.FindPropertyRelative("startingBudget").intValue = budget;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private AuctionItem CreateItem(string id, int value,
            ItemCategory category = ItemCategory.Historical)
        {
            var item = ScriptableObject.CreateInstance<AuctionItem>();
            objects.Add(item);
            var data = new SerializedObject(item);
            data.FindProperty("itemId").stringValue = id;
            data.FindProperty("itemName").stringValue = id;
            data.FindProperty("startingPrice").intValue = 100;
            data.FindProperty("trueValue").intValue = value;
            data.FindProperty("category").enumValueIndex = (int)category;
            data.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }
    }
}
