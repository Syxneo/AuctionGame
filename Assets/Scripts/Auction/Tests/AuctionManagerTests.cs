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
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.False);
            auction.Tick(1.75f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            auction.Tick(0.25f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Bidding));
            Assert.That(auction.RemainingTime, Is.EqualTo(15));
            Assert.That(reveals, Is.EqualTo(1));
        }

        [TestCase(10)]
        [TestCase(25)]
        [TestCase(50)]
        public void PlayerCanOpenWithAnyStandardBid(int amount)
        {
            BeginBidding();
            Assert.That(auction.CurrentPrice, Is.Zero);
            Assert.That(auction.HighestBidderId, Is.Empty);
            Assert.That(auction.BidHistory, Is.Empty);
            var accepted = new List<BidRecord>();
            int rejected = 0;
            auction.OnBidPlaced += accepted.Add;
            auction.OnPlayerBidRejected += () => rejected++;
            foreach (int invalid in new[] { -10, 0, 9, 15 })
                Assert.That(auction.TryPlaceBid(BidderIds.Player, invalid), Is.False);
            Assert.That(auction.TryPlaceBid("unknown", 10), Is.False);
            Assert.That(accepted, Is.Empty);
            Assert.That(rejected, Is.EqualTo(4));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, amount), Is.True);
            Assert.That(auction.CurrentPrice, Is.EqualTo(amount));
            Assert.That(auction.HighestBidderId, Is.EqualTo(BidderIds.Player));
            Assert.That(auction.BidHistory.Single().amount, Is.EqualTo(amount));
            Assert.That(accepted.Single().amount, Is.EqualTo(amount));
            Assert.That(rejected, Is.EqualTo(4));
        }

        [Test]
        public void AiCanOnlyOpenAtTenAndWithinBudget()
        {
            var profile = GetPersonalitySettings(0);
            profile.FindProperty("startingBudget").intValue = 9;
            profile.ApplyModifiedPropertiesWithoutUndo();
            BeginBidding();
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 10), Is.False);
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 0), Is.False);
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 25), Is.False);
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 10), Is.True);
        }

        [Test]
        public void PoolDrawIsSeededWeightedDistinctAndRepeatsForEveryLot()
        {
            string Run(int seed, bool useDialogue)
            {
                auction = CreateAuction(count: 2, automaticAi: true);
                ConfigurePool(seed, 600, 600, 600, 600, 600);
                var settings = new SerializedObject(auction);
                settings.FindProperty("aiDialogueLibrary").objectReferenceValue = useDialogue
                    ? Resources.Load<TextAsset>("Auction/AiDialoguePool") : new TextAsset("{}");
                if (!useDialogue)
                    objects.Add(settings.FindProperty("aiDialogueLibrary").objectReferenceValue);
                settings.ApplyModifiedPropertiesWithoutUndo();
                var draws = new List<string>();
                auction.OnItemChanged += _ =>
                {
                    var active = auction.Opponents.Where(bidder => bidder.IsActiveForLot).ToArray();
                    Assert.That(auction.Opponents, Has.Count.EqualTo(5));
                    Assert.That(active.Select(bidder => bidder.Id).Distinct().Count(), Is.EqualTo(3));
                    var weight = typeof(AuctionManager).GetMethod("ParticipationWeight", BindingFlags.Static | BindingFlags.NonPublic);
                    foreach (var bidder in auction.Opponents)
                        Assert.That(weight.Invoke(null, new object[] { bidder }), Is.EqualTo(10 + bidder.EffectiveInterest));
                    draws.Add(string.Join(",", active.Select(bidder => bidder.Id)));
                };
                Assert.That(auction.StartAuction(), Is.True);
                auction.Tick(100);
                Assert.That(draws, Has.Count.EqualTo(2));
                return string.Join(";", draws.Concat(auction.Results.Select(result => $"{result.winnerId}:{result.finalPrice}")));
            }

            string initial = Run(1729, true);
            Assert.That(Run(1729, true), Is.EqualTo(initial));
            Assert.That(Run(1729, false), Is.EqualTo(initial));
            var selections = Enumerable.Range(1, 8).Select(seed => Run(seed, true).Split(';').Take(2).ToArray())
                .ToArray();
            Assert.That(selections.Select(draws => draws[0]).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(selections.Any(draws => draws[0] != draws[1]), Is.True);
        }

        [Test]
        public void PoolBudgetEligibilityPersistsAndRetryRebuildsPool()
        {
            auction = CreateAuction(count: 2);
            ConfigurePool(1729, 10, 10, 9, 0, 1);
            Assert.That(auction.StartAuction(), Is.True);
            Assert.That(ActivePoolIds(), Is.EqualTo(new[] { "helena", "ferko" }));
            Assert.That(auction.Opponents[0].EffectiveInterest, Is.Zero);
            auction.Tick(AuctionManager.RevealDuration);
            Assert.That(auction.TryPlaceBid("viktor", 10), Is.False);
            Assert.That(auction.TryPlaceBid("helena", 10), Is.True);
            auction.Tick(17);
            Assert.That(auction.Opponents[0].AvailableBudget, Is.Zero);
            Assert.That(ActivePoolIds(), Is.EqualTo(new[] { "ferko" }));
            auction.Tick(30);
            Assert.That(auction.RestartAuction(), Is.True);
            Assert.That(ActivePoolIds(), Is.EqualTo(new[] { "helena", "ferko" }));
            Assert.That(auction.Opponents[0].AvailableBudget, Is.EqualTo(10));

            auction = CreateAuction();
            ConfigurePool(1729, 0, 1, 2, 3, 9);
            BeginBidding();
            Assert.That(ActivePoolIds(), Is.Empty);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
        }

        [Test]
        public void PoolOnlyActiveBiddersSpeakAndChickenOutDoesNotSelectAReplacement()
        {
            auction = CreateAuction(value: 100);
            ConfigurePool(1729, 600, 600, 600, 600, 600);
            foreach (int index in Enumerable.Range(0, 5))
            {
                var profile = GetPersonalitySettings(index);
                profile.FindProperty("intimidationResistance").intValue = 0;
                SetProfile(profile, ItemCategory.Historical, 0, 100);
            }

            SetIntimidation("highBidIntimidation", 100);
            var lines = new List<(string id, string group, string line)>();
            auction.OnAiDialogue += (id, group, line) => lines.Add((id, group, line));
            BeginBidding();
            string[] active = ActivePoolIds();
            Assert.That(lines.Where(line => line.group == "OPENING").Select(line => line.id), Is.EqualTo(active));
            var suppliedLibrary = Newtonsoft.Json.Linq.JObject.Parse(Resources.Load<TextAsset>("Auction/AiDialoguePool").text);
            foreach (var line in lines)
            {
                var profile = suppliedLibrary["profiles"].Single(profile => (string)profile["id"] == line.id);
                var group = profile["groups"].Single(group => (string)group["key"] == "interest.very-low");
                Assert.That(group["lines"].Values<string>(), Does.Contain(line.line));
            }

            Assert.That(auction.TryPlaceBid(active[0], 10), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 60), Is.True);
            Assert.That(lines.Any(line => line.group == "BR-02"), Is.True);
            AIBidder withdrawing = auction.Opponents.First(bidder => bidder.Id == active[0]);
            Decide(withdrawing);
            Assert.That(withdrawing.HasChickenedOut, Is.True);
            Assert.That(ActivePoolIds(), Is.EqualTo(active));
            Assert.That(auction.TryPlaceBid(withdrawing.Id, 70), Is.False);
            Assert.That(lines.Where(line => line.group == "WD-01").Select(line => line.id), Is.EqualTo(new[] { withdrawing.Id }));
            Assert.That(lines.All(line => active.Contains(line.id)), Is.True);
        }

        [Test]
        public void PoolRejectsBidsAboveEstimateWithoutUsingLastBid()
        {
            auction = CreateAuction(value: 10);
            ConfigurePool(1729, 600, 600, 600, 600, 600);
            foreach (int index in Enumerable.Range(0, 5))
                SetProfile(GetPersonalitySettings(index), ItemCategory.Historical, 0, 100);
            BeginBidding();
            var bidder = auction.Opponents.First(opponent => opponent.IsActiveForLot);
            Assert.That(auction.TryGetMaxBid(bidder.Id, out int maxBid), Is.True);
            Assert.That(maxBid, Is.EqualTo(10));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            Assert.That(auction.TryPlaceBid(bidder.Id, 20), Is.False);
            Assert.That(bidder.HasUsedLastBid, Is.False);
        }

        private string[] ActivePoolIds() => auction.Opponents.Where(bidder => bidder.IsActiveForLot)
            .Select(bidder => bidder.Id).ToArray();

        [Test]
        public void PacedOpeningsBidsAndWinnersAreSequentialAndRepeatOnRetry()
        {
            auction = CreateAuction(count: 6, automaticAi: true);
            ConfigurePacedPool(100);
            var interactions = new List<(double time, string lot, string id, string group)>();
            var bids = new List<(double time, string id)>();
            auction.OnAiDialogue += (id, group, _) =>
            {
                if (id != "sale")
                    interactions.Add((AuctionTime(), auction.CurrentItem.ItemId, id, group));
            };
            auction.OnBidPlaced += bid =>
            {
                Assert.That(bid.biddingElapsed, Is.LessThan(AuctionManager.MaximumBiddingDuration));
                bids.Add((AuctionTime(), bid.bidderId));
            };

            Assert.That(auction.StartAuction(), Is.True);
            var openingIds = ActivePoolIds();
            Assert.That(interactions.Select(entry => entry.time), Is.EqualTo(new[] { 0d }));
            auction.Tick(4);
            Assert.That(interactions.Select(entry => entry.time), Is.EqualTo(new[] { 0d, 2d, 4d }));
            Assert.That(interactions.Select(entry => entry.id), Is.EqualTo(openingIds));
            Assert.That(auction.State, Is.EqualTo(AuctionState.Bidding));
            auction.Tick(3);
            Assert.That(bids, Has.Count.EqualTo(1), "Only one of the simultaneously ready bidders may act.");
            auction.Tick(1000);
            Assert.That(auction.Results, Has.Count.EqualTo(6));
            Assert.That(interactions.Zip(interactions.Skip(1), (a, b) => b.time - a.time), Is.All.GreaterThanOrEqualTo(2d));
            Assert.That(bids.Zip(bids.Skip(1), (a, b) => b.time - a.time), Is.All.GreaterThanOrEqualTo(2d));
            Assert.That(interactions.Where(entry => entry.group.StartsWith("BR-")).Select(entry => (entry.time, entry.id)),
                Is.EqualTo(bids));
            Assert.That(interactions.Count(entry => entry.group.StartsWith("WIN-")), Is.EqualTo(6));
            foreach (var lot in interactions.GroupBy(entry => entry.lot))
            {
                Assert.That(lot.Take(3).Select(entry => entry.group), Is.All.EqualTo("OPENING"));
                Assert.That(lot.Count(entry => entry.group == "OPENING"), Is.EqualTo(3));
                Assert.That(lot.Last().group, Does.StartWith("WIN-"));
            }

            var firstRun = interactions.ToArray();
            interactions.Clear();
            bids.Clear();
            Assert.That(auction.RestartAuction(), Is.True);
            auction.Tick(1000);
            Assert.That(interactions, Is.EqualTo(firstRun));
        }

        [Test]
        public void PlayerBidInvalidatesQueuedPriceWithoutWaitingForAiCooldown()
        {
            auction = CreateAuction(automaticAi: true);
            ConfigurePacedPool(100);
            var settings = new SerializedObject(auction);
            settings.FindProperty("aiInteractionInterval").floatValue = 0.5f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(auction.AiInteractionInterval, Is.EqualTo(2f));
            Assert.That(auction.StartAuction(), Is.True);
            auction.Tick(7);
            Assert.That(auction.CurrentPrice, Is.EqualTo(10));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 20), Is.True);
            auction.Tick(2);
            Assert.That(auction.BidHistory.Count(bid => bid.amount == 20), Is.EqualTo(1));
            Assert.That(auction.BidHistory.Single(bid => bid.amount == 20).bidderId, Is.EqualTo(BidderIds.Player));
        }

        [TestCase(true, 2f, 13f, 2f)]
        [TestCase(true, 12.5f, 3f, 0.5f)]
        [TestCase(true, 28f, 2f, 0.5f)]
        [TestCase(false, 2f, 13f, 2f)]
        [TestCase(false, 12.5f, 3f, 2f)]
        public void PacedReactionUsesAdjustedRemainingTime(bool playerFirst, float bidAt, float adjustedRemaining, float delay)
        {
            ConfigurePacedPool(100);
            Assert.That(auction.StartAuction(), Is.True);
            auction.Tick(4);
            if (bidAt == 28f)
            {
                // Model a deadline already extended past the hard cap by late bidding.
                typeof(AuctionManager).GetField("bidDeadline", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(auction, 31d);
            }
            auction.Tick(bidAt);
            var settings = new SerializedObject(auction);
            settings.FindProperty("enableAutomaticAi").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();

            var comments = new List<string>();
            auction.OnAiDialogue += (id, group, _) =>
            {
                if (group.StartsWith("BR-"))
                    comments.Add(id);
            };
            string firstBidder = playerFirst ? BidderIds.Player : ActivePoolIds()[0];
            Assert.That(auction.TryPlaceBid(firstBidder, 10), Is.True);
            comments.Clear();
            Assert.That(auction.RemainingTime, Is.EqualTo(adjustedRemaining));
            auction.Tick(delay - 0.125f);
            Assert.That(auction.BidHistory, Has.Count.EqualTo(1));
            auction.Tick(0.125f);
            Assert.That(auction.BidHistory, Has.Count.EqualTo(2));
            var reaction = auction.BidHistory.Last();
            Assert.That(reaction.bidderId, Is.Not.EqualTo(firstBidder));
            Assert.That(reaction.biddingElapsed, Is.EqualTo((double)bidAt + delay));
            Assert.That(comments, Is.EqualTo(new[] { reaction.bidderId }));
        }

        [Test]
        public void LatePlayerRaiseDoesNotShortenSpacingBetweenAiBids()
        {
            ConfigurePacedPool(100);
            Assert.That(auction.StartAuction(), Is.True);
            auction.Tick(4);
            typeof(AuctionManager).GetField("bidDeadline", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(auction, 31d);
            auction.Tick(28);
            string openingBidder = ActivePoolIds()[0];
            Assert.That(auction.TryPlaceBid(openingBidder, 10), Is.True);
            var settings = new SerializedObject(auction);
            settings.FindProperty("enableAutomaticAi").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            auction.Tick(0.25f);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 20), Is.True);
            auction.Tick(1.75f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Sold));
            Assert.That(auction.BidHistory, Has.Count.EqualTo(2));
            Assert.That(auction.Results.Single().winnerId, Is.EqualTo(BidderIds.Player));
        }

        [Test]
        public void LatestValidPlayerBidReschedulesOthersBeyondTheirExistingCooldown()
        {
            auction = CreateAuction(automaticAi: true);
            ConfigurePacedPool(100);
            Assert.That(auction.StartAuction(), Is.True);
            auction.Tick(6);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            auction.Tick(2);
            Assert.That(auction.BidHistory.Last().biddingElapsed, Is.EqualTo(4d));
            Assert.That(auction.BidHistory.Last().bidderId, Is.Not.EqualTo(BidderIds.Player));
            auction.Tick(1);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 30), Is.True);
            auction.Tick(1);
            Assert.That(auction.BidHistory, Has.Count.EqualTo(3), "The AI cooldown is over but the new player delay is not.");
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 40), Is.False);
            auction.Tick(1);
            Assert.That(auction.BidHistory, Has.Count.EqualTo(4));
            Assert.That(auction.BidHistory.Last().biddingElapsed, Is.EqualTo(7d));
            Assert.That(auction.BidHistory.Last().bidderId, Is.Not.EqualTo(BidderIds.Player));
        }

        [Test]
        public void ChickenOutPreemptsCertainBidAndSpeaksExactlyOnce()
        {
            ConfigurePacedPool(100, resistance: 0);
            SetIntimidation("highBidIntimidation", 100);
            var withdrawals = new List<string>();
            auction.OnAiDialogue += (id, group, _) =>
            {
                if (group == "WD-01")
                    withdrawals.Add(id);
            };
            Assert.That(auction.StartAuction(), Is.True);
            auction.Tick(6);
            string[] active = ActivePoolIds();
            Assert.That(auction.TryPlaceBid(active[0], 10), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 60), Is.True);
            var bidder = auction.Opponents.Single(opponent => opponent.Id == active[1]);
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.False);
            auction.Tick(2);
            Assert.That(bidder.HasChickenedOut, Is.True);
            Assert.That(withdrawals, Is.EqualTo(new[] { bidder.Id }));
            Assert.That(auction.BidHistory, Has.Count.EqualTo(2));
            Assert.That(auction.TryPlaceBid(bidder.Id, 70), Is.False);
            Decide(bidder);
            Assert.That(withdrawals, Has.Count.EqualTo(1));
        }

        [TestCase(100, true)]
        [TestCase(0, false)]
        public void FailedChickenOutStillUsesInterest(int interest, bool bids)
        {
            ConfigurePacedPool(interest);
            SetIntimidation("highBidIntimidation", 100);
            Assert.That(auction.StartAuction(), Is.True);
            auction.Tick(6);
            string[] active = ActivePoolIds();
            Assert.That(auction.TryPlaceBid(active[0], 10), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 60), Is.True);
            auction.Tick(2);
            var bidder = auction.Opponents.Single(opponent => opponent.Id == active[1]);
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.False);
            Assert.That(bidder.IsActiveForLot, Is.True);
            Assert.That(auction.BidHistory.Any(bid => bid.bidderId == bidder.Id), Is.EqualTo(bids));
        }

        [Test]
        public void PendingBidIsDiscardedAtHardCapAndCannotSpeakOnNextLot()
        {
            auction = CreateAuction(count: 2);
            ConfigurePacedPool(100);
            var reactions = new List<string>();
            auction.OnAiDialogue += (_, group, _) =>
            {
                if (group.StartsWith("BR-"))
                    reactions.Add(auction.CurrentItem.ItemId);
            };
            Assert.That(auction.StartAuction(), Is.True);
            string[] active = ActivePoolIds();
            auction.Tick(16.5f);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            for (int step = 1; step <= 6; step++)
            {
                auction.Tick(2.5f);
                string bidderId = step % 2 == 0 ? BidderIds.Player : active[0];
                Assert.That(auction.TryPlaceBid(bidderId, 10 * (step + 1)), Is.True);
            }
            auction.Tick(2.25f);
            Assert.That(auction.BiddingElapsed, Is.EqualTo(29.75d));
            Assert.That(auction.TryPlaceBid(active[0], 80), Is.True);
            Decide(auction.Opponents.Single(bidder => bidder.Id == active[1]));
            int count = reactions.Count;
            auction.Tick(0.25f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Sold));
            Assert.That(auction.Results.Single().finalPrice, Is.EqualTo(80));
            auction.Tick(100);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(reactions, Has.Count.EqualTo(count));
        }

        private double AuctionTime() => (double)typeof(AuctionManager)
            .GetProperty("AuctionElapsed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(auction);

        private void ConfigurePacedPool(int interest, int resistance = 100)
        {
            ConfigurePool(1729, 600, 600, 600, 600, 600);
            var settings = new SerializedObject(auction);
            settings.FindProperty("paceAiInteractions").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            for (int index = 0; index < 5; index++)
            {
                var profile = GetPersonalitySettings(index);
                profile.FindProperty("intimidationResistance").intValue = resistance;
                SetProfile(profile, ItemCategory.Historical, interest, 100);
            }
        }

        private void ConfigurePool(int seed, params int[] budgets)
        {
            var settings = new SerializedObject(auction);
            settings.FindProperty("activeBidderLimit").intValue = 3;
            settings.FindProperty("randomSeed").intValue = seed;
            settings.FindProperty("aiDialogueLibrary").objectReferenceValue = Resources.Load<TextAsset>("Auction/AiDialoguePool");
            var slots = settings.FindProperty("bidders");
            string[] names = { "Helena", "Ferko", "Viktor", "Zuzu", "Grumblewick" };
            slots.arraySize = names.Length;
            for (int index = 0; index < names.Length; index++)
                slots.GetArrayElementAtIndex(index).objectReferenceValue =
                    Resources.Load<AIPersonality>("Auction/Personalities/" + names[index]);
            settings.ApplyModifiedPropertiesWithoutUndo();
            for (int index = 0; index < names.Length; index++)
            {
                var profile = GetPersonalitySettings(index);
                profile.FindProperty("startingBudget").intValue = budgets[index];
                SetProfile(profile, ItemCategory.Historical, index * 25, 100);
            }
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
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 10), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10 + increment), Is.EqualTo(accepted));
            Assert.That(auction.CurrentPrice, Is.EqualTo(accepted ? 10 + increment : 10));
        }

        [TestCase(BidderIds.Player)]
        [TestCase(BidderIds.Collector)]
        [TestCase(BidderIds.Trickster)]
        [TestCase(BidderIds.Analyst)]
        public void LeaderCannotOutbidItself(string bidder)
        {
            BeginBidding();
            Assert.That(auction.TryPlaceBid(bidder, 10), Is.True);
            Assert.That(auction.TryPlaceBid(bidder, 20), Is.False);
            Assert.That(auction.BidHistory.Count, Is.EqualTo(1));
        }

        [Test]
        public void BidAboveBudgetIsRejected()
        {
            auction = CreateAuction(budget: 19);
            BeginBidding();
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 25), Is.False);
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 10), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 20), Is.False);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(19));
        }

        [Test]
        public void EstimatesRespectAccuracyBiasAndSeed()
        {
            var evaluator = typeof(AIBidder).Assembly.GetType("AuctionGame.Auction.AIEvaluation");
            var estimate = evaluator.GetMethod("Estimate");
            int Sample(int accuracy, int bias, System.Random random) =>
                (int)estimate.Invoke(null, new object[] { 1000, accuracy, bias, random });

            var low = new System.Random(42);
            var repeat = new System.Random(42);
            var higher = new System.Random(42);
            var under = new System.Random(42);
            double averageError = 0;
            for (int sample = 0; sample < 256; sample++)
            {
                int over = Sample(20, 100, low);
                averageError += over - 1000;
                Assert.That(over, Is.InRange(1000, 1800));
                Assert.That(Sample(20, 100, repeat), Is.EqualTo(over));
                Assert.That(Sample(20, 0, under), Is.InRange(200, 1000));
                Assert.That(Sample(80, 100, higher), Is.InRange(1000, 1200));
            }

            Assert.That(averageError / 256, Is.GreaterThan(600));
            Assert.That(Sample(100, 100, low), Is.EqualTo(1000));
        }

        [TestCase(false, 90, 30)]
        [TestCase(true, 90, 85)]
        public void CategoryProfileSelectsIndependentMaxima(bool multiple, int interest, int accuracy)
        {
            var profile = GetPersonalitySettings(0);
            SetProfile(profile, ItemCategory.Art, 90, 30);
            SetProfile(profile, ItemCategory.Historical, 40, 85);
            var item = CreateItem("lot", 400);
            if (multiple)
                SetCategories(item, ItemCategory.Art, ItemCategory.Historical);
            else
                SetCategories(item, ItemCategory.Art);

            Assert.That(auction.StartAuction(new[] { item }, CreateDatabase(item)), Is.True);
            auction.Tick(2);
            var bidder = auction.Opponents[0];
            Assert.That(bidder.EffectiveInterest, Is.EqualTo(interest));
            Assert.That(bidder.EffectiveAccuracy, Is.EqualTo(accuracy));
            int estimated = bidder.EstimatedValue;
            auction.TryPlaceBid(BidderIds.Player, 10);
            auction.Tick(9);
            Assert.That(bidder.EstimatedValue, Is.EqualTo(estimated));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrEmptyCategoriesUseZeroInterestAndAccuracy(bool empty)
        {
            var profile = GetPersonalitySettings(0);
            profile.FindProperty("categoryProfiles").arraySize = 0;
            SetProfile(profile, ItemCategory.Art, 90, 30);
            var item = CreateItem("lot", 400);
            if (empty)
                SetCategories(item);
            Assert.That(auction.StartAuction(new[] { item }, CreateDatabase(item)), Is.True);
            auction.Tick(2);
            Assert.That(auction.Opponents[0].EffectiveInterest, Is.Zero);
            Assert.That(auction.Opponents[0].EffectiveAccuracy, Is.Zero);
        }

        [Test]
        public void ScheduledDecisionsOccurEveryThreeSeconds()
        {
            auction = CreateAuction(automaticAi: true);
            ConfigureBidders(100, 100, 100);
            BeginBidding();
            auction.Tick(2.5f);
            Assert.That(auction.BidHistory, Is.Empty);
            auction.Tick(0.5f);
            Assert.That(auction.BidHistory.Select(bid => bid.amount), Is.EqualTo(new[] { 10, 20, 30 }));
            Assert.That(auction.BidHistory.All(bid => bid.biddingElapsed == 3), Is.True);
            auction.Tick(3);
            Assert.That(auction.BidHistory.Skip(3).Select(bid => bid.biddingElapsed), Is.EqualTo(new[] { 6d, 6d, 6d }));
        }

        [Test]
        public void PlayerBidDelaysAiDecisionsAndResetsTheirSchedule()
        {
            auction = CreateAuction(automaticAi: true);
            ConfigureBidders(100, 100, 100);
            BeginBidding();
            auction.Tick(1);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 110), Is.False);
            Assert.That(auction.BidHistory, Is.Empty);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            Assert.That(auction.BidHistory.Count, Is.EqualTo(1));
            auction.Tick(0.25f);
            Assert.That(auction.BidHistory.Count, Is.EqualTo(1));
            auction.Tick(1.75f);
            Assert.That(auction.BidHistory.Select(bid => bid.amount), Is.EqualTo(new[] { 10, 20, 30, 40 }));
            var reactionTimes = auction.BidHistory.Skip(1).Select(bid => bid.biddingElapsed).ToArray();
            Assert.That(reactionTimes.All(time => time >= 1.5 && time <= 3), Is.True);
            Assert.That(reactionTimes, Is.Unique);
            auction.Tick(1);
            Assert.That(auction.BidHistory.Count, Is.EqualTo(4));
            auction.Tick(2);
            Assert.That(auction.BidHistory[4].biddingElapsed, Is.EqualTo(reactionTimes[0] + 3));
        }

        [Test]
        public void PlayerBidDoesNotForceAnAiResponseWhenInterestFails()
        {
            auction = CreateAuction(automaticAi: true);
            ConfigureBidders(0, 100, 100);
            BeginBidding();
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            auction.Tick(2);
            Assert.That(auction.BidHistory.Select(bid => bid.bidderId), Is.EqualTo(new[] { BidderIds.Player }));
        }

        [Test]
        public void LastBidAllowsStandardIncrementAboveEstimateAndIsOnlyConsumedOnAcceptance()
        {
            auction = CreateAuction(value: 10);
            ConfigureBidders(100, 100, 100);
            BeginBidding();
            var bidder = auction.Opponents[0];
            Assert.That(auction.TryPlaceBid(bidder.Id, 10), Is.True);
            Assert.That(bidder.HasUsedLastBid, Is.False);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 60), Is.True);
            Assert.That(auction.TryPlaceBid(bidder.Id, 61), Is.False);
            Assert.That(bidder.HasUsedLastBid, Is.False);
            Decide(bidder);
            Assert.That(auction.CurrentPrice, Is.EqualTo(70));
            Assert.That(bidder.HasUsedLastBid, Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 80), Is.True);
            Assert.That(auction.TryPlaceBid(bidder.Id, 90), Is.False);
            auction.Tick(17);
            Assert.That(auction.RestartAuction(), Is.True);
            auction.Tick(2);
            Assert.That(auction.Opponents[0].HasUsedLastBid, Is.False);
        }

        [TestCase(BidderIds.Player)]
        [TestCase(BidderIds.Trickster)]
        public void FailedInterestCanWithdrawFromPlayerOrAiBidAndResetsNextLot(string opposingBidder)
        {
            auction = CreateAuction(count: 2, value: 10);
            ConfigureBidders(0, 100, 0);
            SetIntimidation("highBidIntimidation", 100);
            BeginBidding();
            var bidder = auction.Opponents[0];
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.False);
            Assert.That(auction.BidHistory, Is.Empty);
            Assert.That(auction.TryPlaceBid(BidderIds.Analyst, 10), Is.True);
            Assert.That(auction.TryPlaceBid(opposingBidder, 60), Is.True);
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.True);
            Assert.That(bidder.HasUsedLastBid, Is.False);
            Assert.That(auction.TryPlaceBid(bidder.Id, 70), Is.False);
            Assert.That(bidder.AvailableBudget, Is.EqualTo(600));
            auction.Tick(19);
            Assert.That(bidder.HasChickenedOut, Is.False);
            auction.Tick(17);
            Assert.That(auction.RestartAuction(), Is.True);
            auction.Tick(2);
            Assert.That(auction.Opponents[0].HasChickenedOut, Is.False);
        }

        [TestCase(0, 100)]
        [TestCase(100, 0)]
        public void IntimidationEventIsHandledOnceAndSuccessfulInterestNeverIntimidates(int interest, int resistance)
        {
            ConfigureBidders(interest, 100, resistance);
            SetIntimidation("highBidIntimidation", 100);
            BeginBidding();
            var bidder = auction.Opponents[0];
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 10), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 60), Is.True);
            long eventSequence = auction.BidHistory.Last().sequence;
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.False);
            var sequence = typeof(AIBidder).GetField("lastHandledBidSequence", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(sequence.GetValue(bidder), Is.EqualTo(eventSequence));
            if (interest == 100)
            {
                Assert.That(auction.HighestBidderId, Is.EqualTo(bidder.Id));
                return;
            }

            var profile = new SerializedObject(bidder.Personality);
            profile.FindProperty("intimidationResistance").intValue = 0;
            profile.ApplyModifiedPropertiesWithoutUndo();
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.False);
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 110), Is.True);
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.True);
        }

        [TestCase(10, 0.15)]
        [TestCase(25, 0.35)]
        [TestCase(50, 0.65)]
        [TestCase(0, 0)]
        public void IntimidationUsesConfiguredIncrementStrength(int increment, double expected)
        {
            var method = typeof(AuctionManager).GetMethod("GetIntimidationChance", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method.Invoke(auction, new object[] { increment }), Is.EqualTo(expected));
        }

        [Test]
        public void UnaffordableBidDoesNotConsumeLastBidOrRollIntimidation()
        {
            auction = CreateAuction(value: 10);
            ConfigureBidders(0, 100, 0);
            var profile = GetPersonalitySettings(0);
            profile.FindProperty("startingBudget").intValue = 59;
            profile.ApplyModifiedPropertiesWithoutUndo();
            SetIntimidation("highBidIntimidation", 100);
            BeginBidding();
            var bidder = auction.Opponents[0];
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 10), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 60), Is.True);
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.False);
            Assert.That(bidder.HasUsedLastBid, Is.False);
            Assert.That(auction.BidHistory.Count, Is.EqualTo(2));
        }

        private void Decide(AIBidder bidder) =>
            typeof(AIBidder).GetMethod("Decide", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bidder, new object[] { auction });

        private void SetIntimidation(string property, int chance)
        {
            var settings = new SerializedObject(auction);
            settings.FindProperty(property).intValue = chance;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void DebugLogIncludesValuationAcceptedBidAndAiDecisionWithTickAndTime()
        {
            auction = CreateAuction(automaticAi: true);
            ConfigureBidders(100, 100, 100);

            var settings = new SerializedObject(auction);
            settings.FindProperty("enableAuctionDebugLog").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();

            var messages = new List<string>();
            void Capture(string message, string stackTrace, LogType type)
            {
                if (message.StartsWith("[Auction Debug]", StringComparison.Ordinal))
                    messages.Add(message);
            }

            Application.logMessageReceived += Capture;
            try
            {
                BeginBidding();
                Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
                auction.Tick(5);
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }

            Assert.That(messages.Any(message => message.Contains("tick 1") && message.Contains("true value=$1000")), Is.True);
            Assert.That(messages.Count(message => message.Contains("evaluation | estimate=") && message.Contains("Accuracy=")), Is.EqualTo(3));
            Assert.That(messages.Any(message => message.Contains("Bid accepted | Ty") && message.Contains("→ $10 |")), Is.True);
            Assert.That(messages.Any(message => message.Contains("tick 2") && message.Contains("t=") &&
                message.Contains("attempting") && message.Contains("Interest roll=")), Is.True);
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

            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            Assert.That(auction.RemainingTime, Is.EqualTo(3));
            Assert.That(published, Is.EqualTo(1));
        }

        [Test]
        public void HardCapOverridesRepeatedLateBids()
        {
            BeginBidding();
            auction.Tick(14);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            for (int index = 1; index <= 7; index++)
            {
                auction.Tick(2);
                string bidder = index % 2 == 1 ? BidderIds.Collector : BidderIds.Player;
                Assert.That(auction.TryPlaceBid(bidder, 10 + index * 10), Is.True);
            }

            auction.Tick(1);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 90), Is.True);
            Assert.That(auction.RemainingTime, Is.EqualTo(1));
            auction.Tick(1);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Sold));
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 100), Is.False);
            Assert.That(auction.Results.Single().finalPrice, Is.EqualTo(90));
        }

        [Test]
        public void ExpiredUnbidItemHasEmptyWinnerAndZeroPrice()
        {
            BeginBidding();
            auction.Tick(15);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Unsold));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.False);
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
                Assert.That(auction.TryPlaceBid(BidderIds.Collector, 20), Is.False);
            };
            auction.OnAuctionPhaseFinished += () => finished++;

            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(600));
            Assert.That(auction.Economy.Purchases, Is.Empty);
            auction.Tick(15);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(590));
            Assert.That(auction.Economy.Purchases.Single().purchasePrice, Is.EqualTo(10));
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
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 20), Is.True);
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
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 10), Is.True);
            Assert.That(auction.BidHistory.Single().comment, Is.Not.Empty);
        }

        [TestCase(14.75, true)]
        [TestCase(15, false)]
        public void AiCanExtendBiddingBeforeDeadlineButLosesExactTie(double decisionTime, bool expectedBid)
        {
            auction = CreateAuction(automaticAi: true);
            ConfigureBidders(100, 100, 100);
            BeginBidding();
            // Force only the deadline-adjacent decision.
            const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
            var opponents = (Array)typeof(AuctionManager).GetField("opponents", privateInstance).GetValue(auction);
            for (int index = 0; index < opponents.Length; index++)
            {
                object opponent = opponents.GetValue(index);
                opponent.GetType().GetProperty("NextDecision", privateInstance).SetValue(opponent, index == 0 ? decisionTime : 30d);
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
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            auction.Tick(15);
            Assert.That(auction.Economy.RecordPurchase(loss, 70), Is.True);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(520));
            Assert.That(auction.Economy.CalculateProfit(), Is.EqualTo(120));
            Assert.That(auction.Economy.HasReachedTarget(120), Is.True);
            Assert.That(auction.Economy.HasReachedTarget(121), Is.False);
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
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.True);
            auction.Tick(19);
            Assert.That(auction.TryPlaceBid(BidderIds.Collector, 10), Is.True);
            auction.Tick(17);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 110), Is.False);

            var report = new AuctionReport(auction.Economy.Purchases, 391);
            Assert.That(report.Purchases.Single().ItemName, Is.EqualTo(playerLot.ItemName));
            Assert.That(report.TotalProfit, Is.EqualTo(390));
            Assert.That(report.Succeeded, Is.False);
            auction.Tick(100);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(590));
            Assert.That(auction.Opponents[0].AvailableBudget, Is.EqualTo(590));
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
            Assert.That(report.TotalProfit, Is.EqualTo(390));
        }

        [Test]
        public void BidderArraySupportsAdditionalSlotsWithTheirOwnRuntimeIdentity()
        {
            var config = new SerializedObject(auction);
            var bidders = config.FindProperty("bidders");
            Object personality = bidders.GetArrayElementAtIndex(0).objectReferenceValue;
            bidders.arraySize = 4;
            bidders.GetArrayElementAtIndex(3).objectReferenceValue = personality;
            config.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(auction.StartAuction(), Is.True);
            Assert.That(auction.Opponents.Count, Is.EqualTo(4));
            Assert.That(auction.Opponents.Select(opponent => opponent.Id), Is.Unique);
            var fourth = auction.Opponents[3];
            Assert.That(fourth.Personality, Is.SameAs(personality));
            Assert.That(auction.GetBidderDisplayName(fourth.Id), Is.EqualTo("Zberateľ"));
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

        private void ConfigureBidders(int interest, int accuracy, int resistance)
        {
            for (int index = 0; index < 3; index++)
            {
                var profile = GetPersonalitySettings(index);
                profile.FindProperty("intimidationResistance").intValue = resistance;
                SetProfile(profile, ItemCategory.Historical, interest, accuracy);
            }
        }

        private static void SetProfile(SerializedObject profile, ItemCategory category, int interest, int accuracy)
        {
            var entries = profile.FindProperty("categoryProfiles");
            int index = entries.arraySize;
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("category").enumValueIndex == (int)category)
                {
                    index = i;
                    break;
                }
            }
            if (index == entries.arraySize)
                entries.arraySize++;
            var entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("category").enumValueIndex = (int)category;
            entry.FindPropertyRelative("interest").intValue = interest;
            entry.FindPropertyRelative("accuracy").intValue = accuracy;
            profile.ApplyModifiedPropertiesWithoutUndo();
        }

        private SerializedObject GetPersonalitySettings(int index)
        {
            var settings = new SerializedObject(auction);
            var slots = settings.FindProperty("bidders");
            Assert.That(slots.arraySize, Is.GreaterThan(index));
            var field = slots.GetArrayElementAtIndex(index);
            var personality = (AIPersonality)field.objectReferenceValue;
            if (!personality || AssetDatabase.Contains(personality))
            {
                Assert.That(personality, Is.Not.Null);
                personality = Object.Instantiate(personality);
                objects.Add(personality);
                field.objectReferenceValue = personality;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }

            return new SerializedObject(personality);
        }

        private AuctionItem CreateItem(string id, int value,
            ItemCategory category = ItemCategory.Historical)
        {
            var item = ScriptableObject.CreateInstance<AuctionItem>();
            objects.Add(item);
            var data = new SerializedObject(item);
            data.FindProperty("itemId").stringValue = id;
            data.FindProperty("itemName").stringValue = id;
            data.FindProperty("trueValue").intValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
            SetCategories(item, category);
            return item;
        }
    }
}
