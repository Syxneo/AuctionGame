using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuctionGame.Auction.Tests
{
    internal enum EconomyPolicy { SkipAll, InformedConservative, InformedAggressive }

    internal sealed class EconomyLot
    {
        public AuctionItem Item;
        public AuctionResult Result;
        public BidRecord[] Bids;
        public double Duration;
        public int[] Before;
        public int[] After;
        public int[] Estimates;
        public int[] EffectiveInterests;
        public int[] EffectiveAccuracies;
        public bool[] AiUsedLastBid;
        public bool[] AiChickenedOut;
        public bool WalletBlockedPlayer;
        public bool PlayerBought => Result.winnerId == BidderIds.Player;
        public int PlayerProfit => PlayerBought ? Item.TrueValue - Result.finalPrice : 0;
    }

    internal sealed class EconomyRun
    {
        public int Seed;
        public EconomyPolicy Policy;
        public EconomyLot[] Lots;
        public int[] EndBalances;
        public string Error;
        public bool Completed => Error == null;
        public int Purchases => Lots.Count(lot => lot.PlayerBought);
        public int Spend => Lots.Where(lot => lot.PlayerBought).Sum(lot => lot.Result.finalPrice);
        public int Profit => Lots.Sum(lot => lot.PlayerProfit);
    }

    internal sealed class EconomyPlaytest
    {
        public const string ScenePath = "Assets/Scenes/AuctionDemo.unity";
        public const string LevelPath = "Assets/AuctionMvp/MvpAuctionLevel.asset";
        public const string DatabasePath = "Assets/AuctionMvp/ItemDatabase.asset";
        public const float Step = 1f / 64f;
        public const int CriticalReserve = 70;
        public const int FluffReserve = 40;
        private const double RunTimeLimit = 240;
        private static readonly string[] TimingFields =
        {
            "aiDecisionInterval", "aiPlayerBidMinReactionDelay", "aiPlayerBidMaxReactionDelay"
        };
        private static readonly string[] IntimidationFields =
        {
            "smallBidIntimidation", "mediumBidIntimidation", "highBidIntimidation"
        };
        public static readonly int[] Seeds = Enumerable.Range(1000, 30).ToArray();
        public static readonly EconomyPolicy[] Policies = (EconomyPolicy[])Enum.GetValues(typeof(EconomyPolicy));
        public static readonly string[] BidderOrder = { BidderIds.Player, BidderIds.Collector, BidderIds.Trickster, BidderIds.Analyst };

        public MvpAuctionLevel Level { get; }
        public int ProfitTarget { get; }
        public IReadOnlyList<AuctionItem> Items { get; }
        public IReadOnlyList<AIPersonality> Personalities { get; }
        public IReadOnlyDictionary<string, float> TimingSettings { get; }
        public IReadOnlyDictionary<string, int> IntimidationSettings { get; }
        private readonly bool generateDailyRun;
        private readonly string auctionConfiguration;
        private readonly string[] bidderOrder;

        public EconomyPlaytest(int? profitTarget = null, bool generateDailyRun = false)
        {
            this.generateDailyRun = generateDailyRun;
            var scene = EditorSceneManager.OpenPreviewScene(generateDailyRun
                ? DailyItemGenerationTests.IntegratedScenePath : ScenePath);
            try
            {
                var roots = scene.GetRootGameObjects();
                var preparation = roots.SelectMany(root => root.GetComponentsInChildren<PreparationPhase>(true)).Single();
                var setup = new SerializedObject(preparation);
                Level = (MvpAuctionLevel)setup.FindProperty("level").objectReferenceValue;
                Assert.That(AssetDatabase.GetAssetPath(Level), Is.EqualTo(LevelPath));
                Assert.That(AssetDatabase.GetAssetPath(Level.Database), Is.EqualTo(DatabasePath));
                if (!generateDailyRun)
                    Items = Level.CreateRun();
                ProfitTarget = profitTarget ?? Level.ProfitTarget;

                var auction = (AuctionManager)setup.FindProperty("auction").objectReferenceValue;
                var config = new SerializedObject(auction);
                auctionConfiguration = EditorJsonUtility.ToJson(auction);
                Assert.That(config.FindProperty("enableAutomaticAi").boolValue, Is.True);
                TimingSettings = TimingFields.ToDictionary(field => field, field => config.FindProperty(field).floatValue);
                IntimidationSettings = IntimidationFields.ToDictionary(field => field, field => config.FindProperty(field).intValue);
                var bidders = config.FindProperty("bidders");
                Personalities = Enumerable.Range(0, bidders.arraySize)
                    .Select(index => (AIPersonality)bidders.GetArrayElementAtIndex(index).objectReferenceValue).ToArray();
                Assert.That(Personalities.Count, Is.EqualTo(generateDailyRun ? 5 : 3));
                Assert.That(Personalities.All(personality => personality), Is.True);
                bidderOrder = generateDailyRun
                    ? new[] { BidderIds.Player }.Concat(Personalities.Select(personality =>
                        new SerializedObject(personality).FindProperty("dialogueProfileId").stringValue)).ToArray()
                    : BidderOrder;
                if (generateDailyRun)
                {
                    Assert.That(setup.FindProperty("useValueCappedGeneration").boolValue, Is.True);
                    Assert.That(auction.PacesAiInteractions, Is.True);
                    Assert.That(config.FindProperty("activeBidderLimit").intValue, Is.EqualTo(3));
                    Assert.That(Level.Database.Items, Has.Count.EqualTo(26));
                }
                Assert.That(new SerializedObject(auction.Economy).FindProperty("startingBudgetLevel").objectReferenceValue,
                    Is.SameAs(Level));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public EconomyRun Run(int seed, EconomyPolicy policy, float step = Step)
        {
            var host = new GameObject($"Economy playtest {seed} {policy}") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                return Simulate(host, seed, policy, step);
            }
            catch (Exception exception)
            {
                return new EconomyRun { Seed = seed, Policy = policy, Error = exception.ToString() };
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private EconomyRun Simulate(GameObject host, int seed, EconomyPolicy policy, float step)
        {
            var auction = host.AddComponent<AuctionManager>();
            EditorJsonUtility.FromJsonOverwrite(auctionConfiguration, auction);
            var config = new SerializedObject(auction);
            if (!generateDailyRun)
                config.FindProperty("randomSeed").intValue = seed;
            config.FindProperty("enableAuctionDebugLog").boolValue = false;
            config.FindProperty("enableAiDialogueLog").boolValue = false;
            config.ApplyModifiedPropertiesWithoutUndo();
            var economy = new SerializedObject(auction.Economy);
            economy.FindProperty("startingBudgetLevel").objectReferenceValue = Level;
            economy.ApplyModifiedPropertiesWithoutUndo();

            var lots = new List<EconomyLot>();
            DailyRunPlan plan = generateDailyRun ? Level.CreateDailyRun(seed) : null;
            var runItems = plan?.Lots ?? Items;
            EconomyLot current = null;
            auction.OnStateChanged += state =>
            {
                if (state != AuctionState.Bidding)
                    return;

                Assert.That(auction.CurrentPrice, Is.Zero);
                Assert.That(auction.HighestBidderId, Is.Empty);
                Assert.That(auction.BidHistory, Is.Empty);
                if (generateDailyRun)
                    Assert.That(auction.Opponents.Count(bidder => bidder.IsActiveForLot), Is.LessThanOrEqualTo(3));
                var item = auction.CurrentItem;
                current = new EconomyLot
                {
                    Item = item,
                    Before = Wallets(auction),
                    Duration = auction.RemainingTime,
                    Estimates = auction.Opponents.Select(bidder => bidder.EstimatedValue).ToArray(),
                    EffectiveInterests = auction.Opponents.Select(bidder => bidder.EffectiveInterest).ToArray(),
                    EffectiveAccuracies = auction.Opponents.Select(bidder => bidder.EffectiveAccuracy).ToArray()
                };
            };
            // The manager resets BiddingElapsed on close. Each accepted bid exposes the
            // updated effective close time (including extensions and the hard cap).
            auction.OnBidPlaced += bid => current.Duration = bid.biddingElapsed + auction.RemainingTime;
            auction.OnAuctionEnded += result =>
            {
                current.Result = result;
                current.Bids = auction.BidHistory.ToArray();
                current.After = Wallets(auction);
                current.AiUsedLastBid = auction.Opponents.Select(bidder => bidder.HasUsedLastBid).ToArray();
                current.AiChickenedOut = auction.Opponents.Select(bidder => bidder.HasChickenedOut).ToArray();
                if (lots.Count > 0)
                    Assert.That(current.Before, Is.EqualTo(lots.Last().After));
                ValidateLot(current, policy);
                Assert.That(auction.TryPlaceBid(BidderIds.Player, AuctionManager.MinimumOpeningBid), Is.False);
                lots.Add(current);
            };

            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            Assert.That(auction.Results, Is.Empty);
            Assert.That(auction.BidHistory, Is.Empty);
            Assert.That(auction.Economy.Purchases, Is.Empty);
            bool started = plan == null ? auction.StartAuction(runItems, Level.Database) :
                auction.StartDailyAuction(plan, Level.Database);
            Assert.That(started, Is.True);
            Assert.That(auction.Opponents.Select(bidder => bidder.Id), Is.EqualTo(bidderOrder.Skip(1)));
            Assert.That(auction.Opponents.All(bidder => bidder.Purchases.Count == 0 && bidder.TotalProfit == 0), Is.True);
            Assert.That(Wallets(auction), Is.EqualTo(new[] { Level.StartingBudget }
                .Concat(Personalities.Select(personality => personality.StartingBudget))));

            // Tick dispatches every scheduled AI decision in order. Input is sampled once
            // after each tick, including the frame on which bidding first opens.
            double timeLimit = Math.Max(RunTimeLimit, runItems.Count * 60d);
            for (double elapsed = 0; auction.State != AuctionState.Finished && elapsed < timeLimit; elapsed += step)
            {
                auction.Tick(step);
                if (auction.State == AuctionState.Bidding)
                    BidAsPlayer(auction, current, policy);
            }

            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(lots.Select(lot => lot.Item), Is.EqualTo(runItems));
            Assert.That(auction.Results, Is.EqualTo(lots.Select(lot => lot.Result)));
            Assert.That(auction.CurrentItem, Is.Null);
            Assert.That(auction.BidHistory, Is.Empty);
            ValidatePurchases(auction, lots);
            return new EconomyRun { Seed = seed, Policy = policy, Lots = lots.ToArray(), EndBalances = Wallets(auction) };
        }

        private static void BidAsPlayer(AuctionManager auction, EconomyLot lot, EconomyPolicy policy)
        {
            if (policy == EconomyPolicy.SkipAll || lot.Item.TrueValue <= AuctionManager.MinimumOpeningBid ||
                auction.HighestBidderId == BidderIds.Player)
                return;

            int next = auction.CurrentPrice + AuctionManager.MinimumOpeningBid;
            int valueCap = ValueCap(lot.Item, policy);
            int budget = auction.Economy.AvailableBudget;
            if (next <= valueCap && next > budget)
                lot.WalletBlockedPlayer = true;
            // Affordability and the value cap are monotonic, so a rejected $10 increment
            // cannot be rescued by trying $25 or $50 at the same opportunity.
            if (next <= Math.Min(budget, valueCap))
                Assert.That(auction.TryPlaceBid(BidderIds.Player, next), Is.True);
        }

        private static int ValueCap(AuctionItem item, EconomyPolicy policy) =>
            item.TrueValue - (policy == EconomyPolicy.InformedAggressive ? 1 :
                item.ItemType.Role == ItemRole.Critical ? CriticalReserve : FluffReserve);

        private static int[] Wallets(AuctionManager auction) =>
            new[] { auction.Economy.AvailableBudget }.Concat(auction.Opponents.Select(bidder => bidder.AvailableBudget)).ToArray();

        private void ValidateLot(EconomyLot lot, EconomyPolicy policy)
        {
            Assert.That(lot.Result.itemId, Is.EqualTo(lot.Item.ItemId));
            Assert.That(lot.Result.sold, Is.EqualTo(lot.Bids.Length > 0));
            int previousPrice = 0;
            string previousBidder = "";
            double previousTime = 0;
            double deadline = AuctionManager.InitialBiddingTime;
            foreach (BidRecord bid in lot.Bids)
            {
                Assert.That(bid.itemId, Is.EqualTo(lot.Item.ItemId));
                int bidderIndex = Array.IndexOf(bidderOrder, bid.bidderId);
                Assert.That(bidderIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(bid.bidderId, Is.Not.EqualTo(previousBidder));
                Assert.That(bid.biddingElapsed, Is.GreaterThanOrEqualTo(previousTime).And.LessThan(Math.Min(deadline, AuctionManager.MaximumBiddingDuration)));
                if (previousBidder.Length == 0)
                    Assert.That(bid.amount, Is.EqualTo(AuctionManager.MinimumOpeningBid));
                else
                    Assert.That(bid.amount - previousPrice, Is.EqualTo(10).Or.EqualTo(25).Or.EqualTo(50));
                Assert.That(bid.amount, Is.LessThanOrEqualTo(lot.Before[bidderIndex]));
                if (bid.bidderId == BidderIds.Player)
                {
                    Assert.That(policy, Is.Not.EqualTo(EconomyPolicy.SkipAll));
                    Assert.That(lot.Item.TrueValue, Is.GreaterThan(AuctionManager.MinimumOpeningBid));
                    Assert.That(bid.amount, Is.LessThanOrEqualTo(ValueCap(lot.Item, policy)));
                    if (previousBidder.Length > 0)
                        Assert.That(bid.amount - previousPrice, Is.EqualTo(10));
                }
                if (deadline - bid.biddingElapsed <= AuctionManager.AntiSnipingTime)
                    deadline = bid.biddingElapsed + AuctionManager.AntiSnipingTime;
                previousBidder = bid.bidderId;
                previousPrice = bid.amount;
                previousTime = bid.biddingElapsed;
            }

            Assert.That(lot.Duration, Is.EqualTo(Math.Min(deadline, AuctionManager.MaximumBiddingDuration)).Within(0.00001));
            Assert.That(lot.Result.winnerId, Is.EqualTo(previousBidder));
            Assert.That(lot.Result.finalPrice, Is.EqualTo(previousPrice));
            for (int i = 0; i < bidderOrder.Length; i++)
            {
                int charge = lot.Result.winnerId == bidderOrder[i] ? lot.Result.finalPrice : 0;
                Assert.That(lot.After[i], Is.GreaterThanOrEqualTo(0).And.EqualTo(lot.Before[i] - charge));
            }
        }

        private void ValidatePurchases(AuctionManager auction, List<EconomyLot> lots)
        {
            var won = lots.Where(lot => lot.PlayerBought).ToArray();
            Assert.That(auction.Economy.Purchases.Select(p => (p.item, p.purchasePrice)),
                Is.EqualTo(won.Select(lot => (lot.Item, lot.Result.finalPrice))));
            int profit = won.Sum(lot => lot.Item.TrueValue - lot.Result.finalPrice);
            var report = new AuctionReport(auction.Economy.Purchases, ProfitTarget);
            Assert.That(report.TotalProfit, Is.EqualTo(profit));
            Assert.That(auction.Economy.CalculateProfit(), Is.EqualTo(profit));
            Assert.That(report.Succeeded, Is.EqualTo(profit >= ProfitTarget));
            Assert.That(auction.Economy.HasReachedTarget(ProfitTarget), Is.EqualTo(report.Succeeded));

            foreach (var bidder in auction.Opponents)
            {
                var aiWins = lots.Where(lot => lot.Result.winnerId == bidder.Id).ToArray();
                Assert.That(bidder.Purchases.Select(p => (p.Item, p.PurchasePrice)),
                    Is.EqualTo(aiWins.Select(lot => (lot.Item, lot.Result.finalPrice))));
                Assert.That(bidder.TotalProfit, Is.EqualTo(aiWins.Sum(lot => lot.Item.TrueValue - lot.Result.finalPrice)));
            }
        }
    }
}
