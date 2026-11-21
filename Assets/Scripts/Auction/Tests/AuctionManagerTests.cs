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
            auction.TryPlaceBid(BidderIds.Player, 100);
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

        [TestCase(1f)]
        [TestCase(1.5f)]
        public void OpeningAndReactionQueuesUseTheConfiguredDelay(float delay)
        {
            auction = CreateAuction(automaticAi: true, reactionDelay: delay == 1.5f ? null : delay);
            ConfigureBidders(100, 100, 100);
            BeginBidding();
            var order = ReadReactionOrder();
            auction.Tick(delay - 0.25f);
            Assert.That(auction.BidHistory, Is.Empty);
            auction.Tick(0.25f);
            Assert.That(auction.BidHistory.Single().bidderId, Is.EqualTo(order[0].Id));
            Assert.That(auction.BidHistory.Single().amount, Is.EqualTo(100));
            auction.Tick(3 * delay);
            Assert.That(auction.BidHistory.Select(bid => bid.biddingElapsed),
                Is.EqualTo(Enumerable.Range(1, 4).Select(index => (double)index * delay)));
            Assert.That(auction.BidHistory.Select(bid => bid.bidderId),
                Is.EqualTo(new[] { order[0].Id, order[1].Id, order[0].Id, order[1].Id }));
            Assert.That(auction.BidHistory.Select(bid => bid.amount), Is.EqualTo(new[] { 100, 110, 120, 130 }));
        }

        [Test]
        public void DeclinedOpeningAdvancesInShuffledOrderAndDoesNotRepeat()
        {
            auction = CreateAuction(automaticAi: true);
            ConfigureBidders(0, 100, 100);
            BeginBidding();
            var order = ReadReactionOrder();
            var settings = new SerializedObject(order[1].Personality);
            SetProfile(settings, ItemCategory.Historical, 100, 100);
            // Effective Interest is fixed at reveal, so apply the changed profile on a fresh run.
            auction.Tick(18);
            Assert.That(auction.Results.Single().sold, Is.False);
            Assert.That(auction.RestartAuction(), Is.True);
            auction.Tick(3.75f);
            Assert.That(auction.BidHistory, Is.Empty);
            auction.Tick(0.25f);
            Assert.That(auction.BidHistory.Single().bidderId, Is.EqualTo(order[1].Id));
            Assert.That(auction.BidHistory.Single().biddingElapsed, Is.EqualTo(2));
        }

        [Test]
        public void PlayerBidReplacesPendingQueueButInvalidBidDoesNot()
        {
            auction = CreateAuction(automaticAi: true);
            ConfigureBidders(100, 100, 100);
            BeginBidding();
            auction.Tick(0.75f);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 110), Is.False);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
            auction.Tick(0.25f);
            Assert.That(auction.BidHistory.Count, Is.EqualTo(1));
            auction.Tick(0.75f);
            Assert.That(auction.BidHistory.Last().biddingElapsed, Is.EqualTo(1.75));
            auction.Tick(0.75f);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 115), Is.False);
            auction.Tick(0.25f);
            Assert.That(auction.BidHistory.Last().biddingElapsed, Is.EqualTo(2.75));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 130), Is.True);
            auction.Tick(0.75f);
            Assert.That(auction.BidHistory.Count, Is.EqualTo(4));
            auction.Tick(0.25f);
            Assert.That(auction.BidHistory.Last().biddingElapsed, Is.EqualTo(3.75));
        }

        [Test]
        public void DeclinedReactionsAreExhaustedWithoutPolling()
        {
            auction = CreateAuction(automaticAi: true);
            ConfigureBidders(0, 100, 100);
            BeginBidding();
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
            auction.Tick(14);
            Assert.That(auction.BidHistory.Select(bid => bid.bidderId), Is.EqualTo(new[] { BidderIds.Player }));
            Assert.That(typeof(AuctionManager).GetField("reactionIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(auction), Is.EqualTo(3));
            auction.Tick(1);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Sold));
        }

        private AIBidder[] ReadReactionOrder() =>
            ((AIBidder[])typeof(AuctionManager).GetField("reactionOrder", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(auction)).ToArray();

        [Test]
        public void NormalDeadlineCancelsAReactionDueAtTheSameTime()
        {
            auction = CreateAuction(automaticAi: true);
            ConfigureBidders(100, 100, 100);
            BeginBidding();
            typeof(AuctionManager).GetField("nextReactionAt", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(auction, 15d);
            auction.Tick(15);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Unsold));
            Assert.That(auction.BidHistory, Is.Empty);
        }

        [Test]
        public void BidderCanMatchEstimateButCannotBidAboveIt()
        {
            auction = CreateAuction(value: 100);
            ConfigureBidders(100, 100, 100);
            BeginBidding();
            var bidder = auction.Opponents[0];
            Assert.That(auction.TryPlaceBid(bidder.Id, 100), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 110), Is.True);
            Assert.That(auction.TryPlaceBid(bidder.Id, 120), Is.False);
            Decide(bidder);
            Assert.That(auction.CurrentPrice, Is.EqualTo(110));
            Assert.That(bidder.AvailableBudget, Is.EqualTo(600));
        }

        [Test]
        public void BidderCannotOpenAboveItsEstimate()
        {
            auction = CreateAuction(value: 90);
            ConfigureBidders(100, 100, 100);
            BeginBidding();
            var bidder = auction.Opponents[0];
            Assert.That(auction.TryPlaceBid(bidder.Id, 100), Is.False);
            Decide(bidder);
            Assert.That(auction.BidHistory, Is.Empty);
        }

        [TestCase(BidderIds.Player)]
        [TestCase(BidderIds.Trickster)]
        public void FailedInterestCanWithdrawFromPlayerOrAiBidAndResetsNextLot(string opposingBidder)
        {
            auction = CreateAuction(count: 2);
            ConfigureBidders(0, 100, 0);
            SetIntimidation("highBidIntimidation", 100);
            BeginBidding();
            var bidder = auction.Opponents[0];
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.False);
            Assert.That(auction.BidHistory, Is.Empty);
            Assert.That(auction.TryPlaceBid(BidderIds.Analyst, 100), Is.True);
            Assert.That(auction.TryPlaceBid(opposingBidder, 150), Is.True);
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.True);
            Assert.That(auction.TryPlaceBid(bidder.Id, 160), Is.False);
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
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 100), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 150), Is.True);
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
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 200), Is.True);
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
        public void UnaffordableBidDoesNotRollIntimidation()
        {
            auction = CreateAuction();
            ConfigureBidders(0, 100, 0);
            var profile = GetPersonalitySettings(0);
            profile.FindProperty("startingBudget").intValue = 149;
            profile.ApplyModifiedPropertiesWithoutUndo();
            SetIntimidation("highBidIntimidation", 100);
            BeginBidding();
            var bidder = auction.Opponents[0];
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 100), Is.True);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 150), Is.True);
            Decide(bidder);
            Assert.That(bidder.HasChickenedOut, Is.False);
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
                Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
                auction.Tick(5);
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }

            Assert.That(messages.Any(message => message.Contains("tick 1") && message.Contains("true value=$1000")), Is.True);
            Assert.That(messages.Count(message => message.Contains("evaluation | estimate=") && message.Contains("Accuracy=")), Is.EqualTo(3));
            Assert.That(messages.Any(message => message.Contains("Bid accepted | You") && message.Contains("$100")), Is.True);
            Assert.That(messages.Any(message => message.Contains("tick 2") && message.Contains("t=") &&
                message.Contains("attempting") && message.Contains("Bid chance=100%") &&
                message.Contains("Interest roll=")), Is.True);
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
            var small = RunAiAuction(0.125f, dialogueEnabled: false);
            Assert.That(large.bids.Count, Is.GreaterThan(6));
            Assert.That(large.bids, Is.EqualTo(small.bids));
            Assert.That(large.results, Is.EqualTo(small.results));
            Assert.That(large.estimates, Is.EqualTo(small.estimates));
        }

        [Test]
        public void OpeningDialogueIgnoresPriceEstimateAndBudget()
        {
            List<string> OpenWith(int value, int price, int budget)
            {
                auction = CreateAuction(value: value);
                ConfigureBidders(75, 100, 100);
                var profile = GetPersonalitySettings(0);
                profile.FindProperty("startingBudget").intValue = budget;
                profile.ApplyModifiedPropertiesWithoutUndo();
                return CaptureDialogue(() =>
                {
                    Assert.That(auction.StartAuction(), Is.True);
                    var item = new SerializedObject(auction.CurrentItem);
                    item.FindProperty("startingPrice").intValue = price;
                    item.ApplyModifiedPropertiesWithoutUndo();
                    auction.Tick(2);
                });
            }

            var affordable = OpenWith(400, 100, 600);
            var blocked = OpenWith(0, 200, 0);
            Assert.That(affordable.Count, Is.EqualTo(3));
            Assert.That(blocked, Is.EqualTo(affordable));
        }

        [TestCase(0, 24, "Tento predmet ma vôbec neoslovuje.", "Toto je nuda aj na moje pomery.", "Predmet má pre mňa minimálnu prioritu.")]
        [TestCase(25, 49, "Nie je to práve môj vkus.", "Heh, už som videl aj zaujímavejšie veci.", "Môj záujem o tento predmet je obmedzený.")]
        [TestCase(50, 74, "Tento kus by moju zbierku pekne doplnil.", "Toto by mohla byť celkom dobrá zábava.", "Predmet zodpovedá mojim preferenciám.")]
        [TestCase(75, 100, "Takýto predmet vo svojej zbierke jednoducho chcem.", "Okej, toto sa mi fakt páči.", "Tento predmet ma výrazne zaujíma.")]
        public void OpeningDialogueInterestBandsUseThreeVariantsPerBidder(int low, int high,
            string collector, string trickster, string analyst)
        {
            string[] OpenLots(int interest)
            {
                auction = CreateAuction(count: 4);
                ConfigureBidders(interest, 100, 100);
                return CaptureDialogue(() =>
                {
                    BeginBidding();
                    auction.Tick(1000);
                }).Where(line => line.Contains("[OPENING]")).ToArray();
            }

            var lowLines = OpenLots(low);
            Assert.That(OpenLots(high), Is.EqualTo(lowLines));
            Assert.That(lowLines.Length, Is.EqualTo(12));
            foreach (var (name, example) in new[] { ("Collector", collector), ("Trickster", trickster), ("Analyst", analyst) })
            {
                var lines = lowLines.Where(line => line.Contains($"[{name}]")).Select(line => line.Substring(line.IndexOf('„'))).ToArray();
                Assert.That(lines.Take(3), Is.Unique);
                Assert.That(lines.Take(3), Does.Contain($"„{example}“"));
                Assert.That(lines[3], Is.Not.EqualTo(lines[2]));
            }
        }

        [Test]
        public void DialogueOpeningAndSaleShuffleBagAreIndependentAcrossLots()
        {
            auction = CreateAuction(count: 5);
            var messages = CaptureDialogue(() =>
            {
                BeginBidding();
                auction.Tick(1000);
                auction.Tick(1000);
            });
            Assert.That(messages.Count(line => line.Contains("[OPENING]")), Is.EqualTo(15));
            var sales = messages.Where(line => line.Contains("[Dražobník] [SALE-02]"))
                .Select(line => line.Substring(line.IndexOf('„'))).ToArray();
            Assert.That(sales.Length, Is.EqualTo(5));
            Assert.That(sales.Take(4), Is.Unique);
            Assert.That(sales[4], Is.Not.EqualTo(sales[3]));
        }

        [Test]
        public void DialogueUsesFinalWinnerBudgetAndAnnouncesPlayerSalesSeparately()
        {
            auction = CreateAuction(count: 2);
            ConfigureBidders(100, 100, 100);
            var profile = GetPersonalitySettings(0);
            profile.FindProperty("startingBudget").intValue = 110;
            profile.ApplyModifiedPropertiesWithoutUndo();
            var messages = CaptureDialogue(() =>
            {
                BeginBidding();
                Assert.That(auction.TryPlaceBid(BidderIds.Collector, 100), Is.True);
                auction.Tick(19);
                Assert.That(auction.Opponents[0].AvailableBudget, Is.EqualTo(10));
                Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
                auction.Tick(17);
            });
            Assert.That(messages.Count(line => line.Contains("[Collector] [WIN-04]")), Is.EqualTo(1));
            Assert.That(messages.Count(line => line.Contains("[Dražobník] [SALE-01]")), Is.EqualTo(1));
        }

        [Test]
        public void DialogueOnlyConfirmsWithdrawalAfterChickenOut()
        {
            ConfigureBidders(0, 100, 0);
            SetIntimidation("highBidIntimidation", 100);
            var messages = CaptureDialogue(() =>
            {
                BeginBidding();
                var bidder = auction.Opponents[0];
                Decide(bidder);
                Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 100), Is.True);
                Decide(bidder);
                Assert.That(bidder.HasChickenedOut, Is.False);
                Assert.That(auction.TryPlaceBid(BidderIds.Player, 150), Is.True);
                Decide(bidder);
                Decide(bidder);
            });
            Assert.That(messages.Count(line => line.Contains("[Collector] [WD-01]")), Is.EqualTo(1));
            Assert.That(messages.Any(line => line.Contains("[WD-02]") || line.Contains("[WD-03]")), Is.False);
        }

        [TestCase(600, "BR-04")]
        [TestCase(105, "BR-04/05")]
        public void DialogueReportsTheNextBidLimitOnceWithoutDeclaringWithdrawal(int budget, string category)
        {
            auction = CreateAuction(value: 100);
            ConfigureBidders(100, 100, 100);
            var profile = GetPersonalitySettings(0);
            profile.FindProperty("startingBudget").intValue = budget;
            profile.ApplyModifiedPropertiesWithoutUndo();
            var messages = CaptureDialogue(() =>
            {
                BeginBidding();
                Assert.That(auction.TryPlaceBid(BidderIds.Player, 110), Is.False);
                Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
                Decide(auction.Opponents[0]);
                auction.Tick(5);
            });
            Assert.That(messages.Count(line => line.Contains("[BR-")), Is.EqualTo(1));
            Assert.That(messages.Any(line => line.Contains($"[Collector] [{category}]")), Is.True);
            Assert.That(messages.Any(line => line.Contains("[WD-")), Is.False);
        }

        private static List<string> CaptureDialogue(Action action)
        {
            var messages = new List<string>();
            void Capture(string message, string stackTrace, LogType type)
            {
                if (message.StartsWith("[AI Dialogue]", StringComparison.Ordinal))
                    messages.Add(message);
            }

            Application.logMessageReceived += Capture;
            try
            {
                action();
                return messages;
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }
        }

        [TestCase(0.5f)]
        [TestCase(1.5f)]
        public void HardCapCancelsReactionsDueAtOrAfterThirtySeconds(float delay)
        {
            ConfigureBidders(100, 100, 100);
            BeginBidding();
            auction.Tick(14);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 100), Is.True);
            for (int index = 1; index <= 7; index++)
            {
                auction.Tick(2);
                string bidder = index % 2 == 1 ? BidderIds.Collector : BidderIds.Player;
                Assert.That(auction.TryPlaceBid(bidder, 100 + index * 10), Is.True);
            }

            auction.Tick(1.5f);
            var settings = new SerializedObject(auction);
            settings.FindProperty("enableAutomaticAi").boolValue = true;
            settings.FindProperty("aiReactionDelay").floatValue = delay;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 180), Is.True);
            int acceptedBids = auction.BidHistory.Count;
            auction.Tick(0.5f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Sold));
            Assert.That(auction.RemainingTime, Is.Zero);
            Assert.That(auction.TryPlaceBid(BidderIds.Trickster, 190), Is.False);
            Assert.That(auction.BidHistory.Count, Is.EqualTo(acceptedBids));
            Assert.That(auction.Results.Single().finalPrice, Is.EqualTo(180));
            auction.Tick(100);
            Assert.That(auction.Results.Count, Is.EqualTo(1));
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
            Assert.That(auction.GetBidderDisplayName(fourth.Id), Is.EqualTo("Collector"));
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

        private (List<(string, string, int, double)> bids, string[] results, List<int> estimates) RunAiAuction(
            float step, bool dialogueEnabled = true)
        {
            var manager = CreateAuction(count: 6, automaticAi: true);
            var settings = new SerializedObject(manager);
            settings.FindProperty("enableAiDialogueLog").boolValue = dialogueEnabled;
            settings.ApplyModifiedPropertiesWithoutUndo();
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

        private AuctionManager CreateAuction(int count = 1, int budget = 600, int value = 1000,
            bool automaticAi = false, float? reactionDelay = 1)
        {
            var gameObject = new GameObject("Auction test");
            objects.Add(gameObject);
            var manager = gameObject.AddComponent<AuctionManager>();
            var config = new SerializedObject(manager);
            config.FindProperty("enableAutomaticAi").boolValue = automaticAi;
            if (reactionDelay.HasValue)
                config.FindProperty("aiReactionDelay").floatValue = reactionDelay.Value;
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
            data.FindProperty("startingPrice").intValue = 100;
            data.FindProperty("trueValue").intValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
            SetCategories(item, category);
            return item;
        }
    }
}
