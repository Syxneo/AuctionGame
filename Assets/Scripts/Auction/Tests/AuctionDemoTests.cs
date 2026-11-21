using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AuctionGame.Auction.Tests
{
    public sealed class AuctionDemoTests
    {
        [UnityTest]
        public IEnumerator DemoShowsFailureThenRetriesAndContinuesAfterSuccess()
        {
            const string scenePath = "Assets/Scenes/AuctionDemo.unity";
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath), Is.Not.Null);
            yield return new EnterPlayMode();

            EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            for (int frame = 0; frame < 120 && !GameObject.Find("Read NEWSSSS"); frame++)
                yield return null;
            Assert.That(GameObject.Find("Read NEWSSSS") != null, Is.True, "The SALE briefing must be ready.");

            var auction = GameObject.Find("AuctionSystem").GetComponent<AuctionManager>();
            var eventSystem = EventSystem.current;
            Assert.That(auction, Is.Not.Null);
            Assert.That(eventSystem, Is.Not.Null);
            Assert.That(eventSystem.currentInputModule, Is.Not.Null);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            var preparation = auction.GetComponent<PreparationPhase>();
            Assert.That(preparation != null, Is.True);
            var levelField = typeof(PreparationPhase).GetField("level", BindingFlags.Instance | BindingFlags.NonPublic);
            var level = (MvpAuctionLevel)levelField.GetValue(preparation);
            Assert.That(level != null, Is.True);
            ReadNews(level);
            Click("Start Auction");
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            VerifyAuctionRuns(auction);

            auction.Tick(1000);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            CollectionAssert.AreEqual(level.CreateRun().Select(item => item.ItemId),
                auction.Results.Select(result => result.itemId));
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(level.StartingBudget));
            Assert.That(auction.Economy.CalculateProfit(), Is.Zero);
            Assert.That(GameObject.Find("Auction Newspaper"), Is.Null);

            var reportUI = auction.GetComponent<AuctionReportUI>();
            var report = reportUI.Report;
            Assert.That(report.Purchases, Is.Empty);
            Assert.That(report.Succeeded, Is.False);
            Assert.That(report.TargetProfit, Is.EqualTo(level.ProfitTarget));
            Assert.That(GameObject.Find("AuctionPanel"), Is.Null);
            Assert.That(GameObject.Find("SALE Report").GetComponentsInChildren<TMP_Text>().Select(label => label.text),
                Does.Contain("Nekúpil si nič. Bez rizika však nepríde ani zisk."));
            auction.Tick(1000);
            Assert.That(reportUI.Report, Is.SameAs(report));
            yield return null;
            Assert.That(Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None)
                .Count(rect => rect.name == "SALE Report"), Is.EqualTo(1));

            Click("Retry Button");
            Assert.That(reportUI.Report, Is.Null);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            Assert.That(GameObject.Find("AuctionPanel"), Is.Not.Null);
            Assert.That(GameObject.Find("SALE briefing"), Is.Not.Null);
            Assert.That(auction.Opponents, Is.Empty);
            Assert.That(auction.CurrentItem, Is.Null);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(level.StartingBudget));
            Assert.That(auction.Economy.Purchases, Is.Empty);
            Assert.That(auction.Results, Is.Empty);
            ReadNews(level);
            Click("Start Auction");
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            Assert.That(auction.CurrentItem, Is.SameAs(level.CreateRun()[0]));
            Assert.That(auction.Opponents, Has.Count.EqualTo(3));
            Assert.That(auction.Opponents.Select(opponent => opponent.AvailableBudget),
                Is.All.EqualTo(level.AiStartingBudget));
            auction.Tick(AuctionManager.RevealDuration);
            Assert.That(auction.CurrentPrice, Is.Zero);
            Assert.That(auction.HighestBidderId, Is.Empty);
            Assert.That(auction.BidHistory, Is.Empty);
            Assert.That(GameObject.Find("AuctionPanel").GetComponentsInChildren<TMP_Text>().Select(label => label.text),
                Does.Contain("Aktuálna ponuka  $0").And.Contain("Zatiaľ žiadne ponuky"));
            var openButtons = new[] { "+$10 Button", "+$25 Button", "+$50 Button" };
            foreach (string button in openButtons)
                Assert.That(GameObject.Find(button).GetComponent<Button>().interactable, Is.True);
            Click("+$10 Button");
            Assert.That(auction.CurrentPrice, Is.EqualTo(10));
            Assert.That(auction.HighestBidderId, Is.EqualTo(BidderIds.Player));
            BuyRemainingProfitableMvpLots(auction);

            auction.Tick(1000);
            yield return null;
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            var successfulReport = reportUI.Report;
            Assert.That(successfulReport, Is.Not.Null.And.Not.SameAs(report));
            Assert.That(successfulReport.Succeeded, Is.True);
            Assert.That(successfulReport.Purchases.Count, Is.EqualTo(3));
            Assert.That(successfulReport.TotalProfit, Is.EqualTo(780));
            var labels = GameObject.Find("SALE Report").GetComponentsInChildren<TMP_Text>().Select(label => label.text);
            Assert.That(labels, Does.Contain("ÚSPECH").And.Contain(successfulReport.Purchases[0].ItemName).And.Contain("DOBRÝ NÁKUP")
                .And.Contain("$10").And.Contain("Celkový zisk  $780")
                .And.Contain("Výborne. Dosiahol si požadovaný zisk. Zdá sa, že máš na obchod dobrý nos."));
            Assert.That(GameObject.Find("Retry Button"), Is.Null);

            GameObject.Find("Continue Button").GetComponent<Button>().onClick.Invoke();
            Assert.That(GameObject.Find("Level Completion"), Is.Not.Null);
            Assert.That(GameObject.Find("SALE Report"), Is.Null);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));

            yield return new ExitPlayMode();
        }

        private static void Click(string name)
        {
            var button = GameObject.Find(name)?.GetComponent<Button>();
            Assert.That(button != null, Is.True, $"Expected active button '{name}'.");
            button.onClick.Invoke();
        }

        private static void ReadNews(MvpAuctionLevel level)
        {
            Click("Read NEWSSSS");
            Assert.That(GameObject.Find("NEWSSSS presentation"), Is.Not.Null);
            foreach (NewspaperEntry entry in level.GetNewspaperEntries())
            {
                Assert.That(GameObject.Find("Headline").GetComponent<TMP_Text>().text, Is.EqualTo(entry.Headline));
                Click("Continue News");
            }
            Assert.That(GameObject.Find("NEWSSSS presentation"), Is.Null);
        }

        private static void VerifyAuctionRuns(AuctionManager auction)
        {
            Assert.That(GameObject.Find("NEWSSSS presentation"), Is.Null);
            auction.Tick(5);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Bidding));
            Assert.That(auction.BiddingElapsed, Is.GreaterThanOrEqualTo(3));
            var nextDecision = typeof(AIBidder).GetProperty("NextDecision",
                BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (AIBidder bidder in auction.Opponents)
                Assert.That(nextDecision.GetValue(bidder), Is.EqualTo(6d));
        }

        private static void BuyRemainingProfitableMvpLots(AuctionManager auction)
        {
            var automaticAi = typeof(AuctionManager).GetField("enableAutomaticAi", BindingFlags.Instance | BindingFlags.NonPublic);
            automaticAi.SetValue(auction, false);
            auction.OnStateChanged += state =>
            {
                if (state == AuctionState.Bidding && auction.CurrentItem.ItemId is "art_venus_a" or "pop_cd_a")
                    Click("+$10 Button");
            };
        }
    }
}
