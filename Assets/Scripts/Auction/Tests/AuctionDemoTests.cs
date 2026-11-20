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
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            var managerSettings = new SerializedObject(auction);
            var slots = managerSettings.FindProperty("bidders");
            Assert.That(slots.arraySize, Is.EqualTo(3));
            Assert.That(auction.Opponents.Select(opponent => opponent.Personality.Name),
                Is.EqualTo(new[] { "Collector", "Trickster", "Analyst" }));
            for (int index = 0; index < slots.arraySize; index++)
                Assert.That(auction.Opponents[index].Personality,
                    Is.SameAs(slots.GetArrayElementAtIndex(index).objectReferenceValue));
            auction.enabled = false;

            var opening = GameObject.Find("BID Button").GetComponent<Button>();
            Assert.That(opening.interactable, Is.False);
            auction.Tick(2);
            Assert.That(opening.interactable, Is.True);
            ExecuteEvents.Execute(opening.gameObject, new PointerEventData(eventSystem),
                ExecuteEvents.pointerClickHandler);
            Assert.That(auction.HighestBidderId, Is.EqualTo(BidderIds.Player));
            Assert.That(auction.CurrentPrice, Is.EqualTo(90));
            Assert.That(opening.gameObject.activeSelf, Is.False);
            foreach (string increment in new[] { "+$10", "+$25", "+$50" })
                Assert.That(GameObject.Find(increment + " Button").GetComponent<Button>().interactable, Is.False);

            auction.Tick(1000);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(auction.Results.Count, Is.EqualTo(6));
            Assert.That(auction.Results.Select(result => result.itemId).Distinct().Count(), Is.EqualTo(6));
            Assert.That(auction.Results.Single(result => result.itemId == "tin-crown").sold, Is.False);
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(600));
            Assert.That(auction.Economy.CalculateProfit(), Is.Zero);

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

            GameObject.Find("Retry Button").GetComponent<Button>().onClick.Invoke();
            Assert.That(reportUI.Report, Is.Null);
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            Assert.That(GameObject.Find("AuctionPanel"), Is.Not.Null);
            Assert.That(auction.Opponents.All(opponent => opponent.AvailableBudget == 600), Is.True);
            BuyProfitableDemoLots(auction);

            auction.Tick(1000);
            yield return null;
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            var successfulReport = reportUI.Report;
            Assert.That(successfulReport, Is.Not.Null.And.Not.SameAs(report));
            Assert.That(successfulReport.Succeeded, Is.True);
            Assert.That(successfulReport.Purchases.Count, Is.EqualTo(2));
            Assert.That(successfulReport.TotalProfit, Is.EqualTo(330));
            var labels = GameObject.Find("SALE Report").GetComponentsInChildren<TMP_Text>().Select(label => label.text);
            Assert.That(labels, Does.Contain("SUCCESS").And.Contain("Royal Wax Seal").And.Contain("GOOD BUY")
                .And.Contain("$250").And.Contain("Total profit  $330")
                .And.Contain("Výborne. Dosiahol si požadovaný zisk. Zdá sa, že máš na obchod dobrý nos."));
            Assert.That(GameObject.Find("Retry Button"), Is.Null);

            GameObject.Find("Continue Button").GetComponent<Button>().onClick.Invoke();
            Assert.That(GameObject.Find("Level Completion"), Is.Not.Null);
            Assert.That(GameObject.Find("SALE Report"), Is.Null);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));

            yield return new ExitPlayMode();
        }

        private static void BuyProfitableDemoLots(AuctionManager auction)
        {
            var config = new SerializedObject(auction);
            config.FindProperty("enableAutomaticAi").boolValue = false;
            config.ApplyModifiedPropertiesWithoutUndo();
            auction.OnStateChanged += state =>
            {
                if (state == AuctionState.Bidding && auction.CurrentItem.ItemId is "royal-seal" or "observatory-map")
                    Assert.That(auction.TryPlaceBid(BidderIds.Player, auction.CurrentItem.StartingPrice), Is.True);
            };
        }
    }
}
