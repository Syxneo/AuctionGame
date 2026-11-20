using System.Collections;
using System.Linq;
using NUnit.Framework;
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
        public IEnumerator DemoStartsInPlayModeAndCompletesAfterPlayerUsesBidButton()
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

            yield return new ExitPlayMode();
        }
    }
}
