using System.Collections;
using System.Linq;
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
            yield return null;

            var auction = Object.FindFirstObjectByType<AuctionManager>();
            var eventSystem = EventSystem.current;
            Assert.That(auction, Is.Not.Null);
            Assert.That(eventSystem, Is.Not.Null);
            Assert.That(eventSystem.currentInputModule, Is.Not.Null);
            var level = AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>("Assets/AuctionMvp/MvpAuctionLevel.asset");
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            var preparation = Object.FindFirstObjectByType<PreparationPhase>();
            Assert.That(preparation, Is.Not.Null);
            var newspaper = preparation.GetComponentInChildren<NewspaperView>(true);
            Click("Open Newspaper");
            Assert.That(newspaper.IsOpen, Is.True);
            Click("Close Newspaper");
            Click("Start Auction");
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            VerifyAuctionRunsWithNewspaperOpen(auction, newspaper);

            float timeScale = Time.timeScale;
            try
            {
                Time.timeScale = 20;
                float deadline = Time.realtimeSinceStartup + 30;
                while (auction.State != AuctionState.Finished && Time.realtimeSinceStartup < deadline)
                    yield return null;
            }
            finally
            {
                Time.timeScale = timeScale;
            }

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
            Assert.That(report.TargetProfit, Is.EqualTo(300));
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
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            Assert.That(GameObject.Find("AuctionPanel"), Is.Not.Null);
            Assert.That(auction.Opponents.All(opponent => opponent.AvailableBudget == 600), Is.True);
            Assert.That(auction.CurrentItem, Is.SameAs(level.CreateRun()[0]));
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(level.StartingBudget));
            Assert.That(auction.Economy.Purchases, Is.Empty);
            Assert.That(auction.Results, Is.Empty);
            Click("Auction Newspaper");
            Assert.That(newspaper.IsOpen, Is.True);
            Click("Close Newspaper");
            BuyProfitableMvpLots(auction);

            auction.Tick(1000);
            yield return null;
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            var successfulReport = reportUI.Report;
            Assert.That(successfulReport, Is.Not.Null.And.Not.SameAs(report));
            Assert.That(successfulReport.Succeeded, Is.True);
            Assert.That(successfulReport.Purchases.Count, Is.EqualTo(2));
            Assert.That(successfulReport.TotalProfit, Is.EqualTo(365));
            var labels = GameObject.Find("SALE Report").GetComponentsInChildren<TMP_Text>().Select(label => label.text);
            Assert.That(labels, Does.Contain("SUCCESS").And.Contain(successfulReport.Purchases[0].ItemName).And.Contain("GOOD BUY")
                .And.Contain("$150").And.Contain("Total profit  $365")
                .And.Contain("Výborne. Dosiahol si požadovaný zisk. Zdá sa, že máš na obchod dobrý nos."));
            Assert.That(GameObject.Find("Retry Button"), Is.Null);

            GameObject.Find("Continue Button").GetComponent<Button>().onClick.Invoke();
            Assert.That(GameObject.Find("Level Completion"), Is.Not.Null);
            Assert.That(GameObject.Find("SALE Report"), Is.Null);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));

            yield return new ExitPlayMode();
        }

        private static void Click(string name) => GameObject.Find(name).GetComponent<Button>().onClick.Invoke();

        private static void VerifyAuctionRunsWithNewspaperOpen(AuctionManager auction, NewspaperView newspaper)
        {
            Click("Auction Newspaper");
            Assert.That(newspaper.IsOpen, Is.True);
            auction.Tick(5);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Bidding));
            Assert.That(auction.BiddingElapsed, Is.GreaterThanOrEqualTo(3));
            var nextDecision = typeof(AIBidder).GetProperty("NextDecision",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (AIBidder bidder in auction.Opponents)
                Assert.That(nextDecision.GetValue(bidder), Is.EqualTo(6d));
            Click("Close Newspaper");
        }

        private static void BuyProfitableMvpLots(AuctionManager auction)
        {
            var config = new SerializedObject(auction);
            config.FindProperty("enableAutomaticAi").boolValue = false;
            config.ApplyModifiedPropertiesWithoutUndo();
            auction.OnStateChanged += state =>
            {
                if (state == AuctionState.Bidding && auction.CurrentItem.ItemId is "clay-tablet" or "blue-horse")
                    Assert.That(auction.TryPlaceBid(BidderIds.Player, auction.CurrentItem.StartingPrice), Is.True);
            };
        }
    }
}
