using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AuctionGame.Auction.Tests
{
    public sealed class AuctionHudTests
    {
        [UnityTest]
        public IEnumerator IntegratedHandFollowsAiLeadAndHidesOnPlayerBid()
        {
            yield return new EnterPlayMode();
            GameIntroTestDriver.LoadIntegratedScene();
            yield return null;

            var auction = Object.FindFirstObjectByType<AuctionManager>();
            var settings = new SerializedObject(auction);
            settings.FindProperty("enableAutomaticAi").boolValue = false;
            settings.FindProperty("paceAiInteractions").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var level = AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>("Assets/AuctionMvp/MvpAuctionLevel.asset");
            var dialogueScaler = GameObject.Find("DialogueCanvas").GetComponent<CanvasScaler>();
            Assert.That(dialogueScaler.referenceResolution, Is.EqualTo(new Vector2(480, 300)));
            Object.FindFirstObjectByType<SceneIntroOutro>().SkipToEnd();
            var hands = AssertHandsConfigured();
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
            Assert.That(auction.StartAuction(level.CreateRun(), level.Database), Is.True);
            OpenLot(auction);
            yield return null;
            var dialogueCanvas = GameObject.Find("DialogueCanvas").transform;
            var salePrice = dialogueCanvas.Find("SALE Price").GetComponentInChildren<TMP_Text>(true);
            Assert.That(salePrice.text, Is.EqualTo("$0"));

            string bidderId = AuctionItemPresentationTests.FindBidderWithLimit(auction, 10).Id;
            Assert.That(auction.TryPlaceBid(bidderId, 10), Is.True);
            Assert.That(hands.Single(hand => hand.enabled).sprite.name, Is.EqualTo("AuctionHand_" + bidderId));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 20), Is.True);
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
            yield return null;
            Assert.That(salePrice.text, Is.EqualTo("$20"));
            auction.Tick(auction.RemainingTime - 2.9f);
            var saleText = dialogueCanvas.Find("SALE Bubble").GetComponentInChildren<TMP_Text>(true);
            Assert.That(saleText.text, Is.EqualTo("$20 po prvé!"));
            auction.Tick(1f);
            Assert.That(saleText.text, Is.EqualTo("$20 po druhé!"));
            auction.Tick(1f);
            Assert.That(saleText.text, Is.EqualTo("$20 po tretie!"));
            Assert.That(auction.TryPlaceBid(bidderId, 30), Is.True);
            Assert.That(saleText.text, Is.EqualTo("Óóó, nová cena je $30!"));
            auction.Tick(0.3f);
            Assert.That(saleText.text, Is.EqualTo("Óóó, nová cena je $30!"));
            auction.Tick(0.4f);
            Assert.That(saleText.text, Is.EqualTo("$30 po prvé!"));
            auction.Tick(0.5f);
            Assert.That(saleText.text, Is.EqualTo("$30 po druhé!"));
            auction.Tick(1f);
            Assert.That(saleText.text, Is.EqualTo("$30 po tretie!"));
            auction.Tick(auction.RemainingTime + 0.01f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Sold));
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator IntegratedHudTracksBidsAndPlayerPurchasesThroughRetry()
        {
            yield return new EnterPlayMode();
            GameIntroTestDriver.LoadIntegratedScene();
            yield return null;

            var auction = Object.FindFirstObjectByType<AuctionManager>();
            var settings = new SerializedObject(auction);
            settings.FindProperty("enableAutomaticAi").boolValue = false;
            settings.FindProperty("paceAiInteractions").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Object.FindFirstObjectByType<BidderDialoguePresenter>().enabled = false;
            var stage = Object.FindFirstObjectByType<SceneIntroOutro>();
            stage.SkipToEnd();

            var dock = (RectTransform)GameObject.Find("Bid Dock").transform;
            Assert.That(GameObject.Find("Lot Name"), Is.Null);
            Canvas.ForceUpdateCanvases();
            Assert.That(GameObject.Find("Timer"), Is.Null);
            Assert.That(GameObject.Find("Timer Progress"), Is.Null);
            Assert.That(dock.name, Is.EqualTo("Bid Dock"));
            Assert.That(dock.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(dock.anchorMax, Is.EqualTo(Vector2.right));
            Assert.That(dock.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(dock.sizeDelta, Is.EqualTo(new Vector2(0f, 347f)));
            Assert.That(GameObject.Find("Opponent Names"), Is.Null);
            Assert.That(Camera.main.targetDisplay, Is.Zero);
            Assert.That(Camera.main.fieldOfView, Is.EqualTo(64f));

            yield return StartFromNews();
            Assert.That(auction.Opponents.Count(opponent => opponent.IsActiveForLot), Is.EqualTo(3));
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(1000));
            OpenLot(auction);
            yield return null;
            var podiumBubble = GameObject.Find("DialogueCanvas").transform.Find("Podium Item Name");
            Assert.That(dock.Cast<Transform>().Where(child => child.gameObject.activeSelf)
                .Select(child => child.name), Is.EquivalentTo(new[] { "+$10 Button", "+$25 Button", "+$50 Button", "Purchased Items", "Player Budget" }));
            Assert.That(podiumBubble, Is.Not.Null);
            Assert.That(podiumBubble.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo(auction.CurrentItem.ItemName));
            foreach (var bidder in auction.Opponents)
            {
                var bidderBubble = podiumBubble.parent.Find(bidder.Id + " Bubble");
                Assert.That(bidderBubble, Is.Not.Null);
                Assert.That(bidderBubble.GetSiblingIndex(), Is.GreaterThan(podiumBubble.GetSiblingIndex()));
                Assert.That(bidderBubble.GetComponentInChildren<Canvas>(true), Is.Null);
            }
            Assert.That(GameObject.Find("Current Bid"), Is.Null);
            Assert.That(GameObject.Find("Leading Bidder"), Is.Null);
            Assert.That(GameObject.Find("Latest Bid"), Is.Null);
            var hands = AssertHandsConfigured();
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
            Assert.That(Caption("+$10 Button"), Is.EqualTo("$10"));
            Assert.That(Caption("+$25 Button"), Is.EqualTo("$25"));
            Assert.That(Caption("+$50 Button"), Is.EqualTo("$50"));
            auction.Tick(5f);
            Assert.That(CloseLot(auction).sold, Is.False);

            var first = auction.CurrentItem;
            OpenLot(auction);
            Click("+$50 Button");
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
            Assert.That(Caption("+$10 Button"), Is.EqualTo("+$10"));
            Assert.That(CloseLot(auction).winnerId, Is.EqualTo(BidderIds.Player));
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
            var budgetLabel = dock.Find("Player Budget").GetComponent<TMP_Text>();
            Assert.That(budgetLabel.text, Is.EqualTo("Rozpočet  $950"));
            var purchaseBar = dock.Find("Purchased Items");
            Assert.That(purchaseBar.GetComponentsInChildren<Image>().Where(icon => icon.enabled).Select(icon => icon.sprite),
                Is.EqualTo(new[] { first.Sprite }));
            Assert.That(auction.Economy.Purchases.Select(purchase => purchase.item), Is.EqualTo(new[] { first }));

            OpenLot(auction);
            string opponent = AuctionItemPresentationTests.FindBidderWithLimit(auction, 10).Id;
            auction.Tick(auction.AiInteractionInterval);
            Assert.That(auction.TryPlaceBid(opponent, 10), Is.True);
            Assert.That(hands.Single(hand => hand.enabled).sprite.name, Is.EqualTo("AuctionHand_" + opponent));
            Assert.That(CloseLot(auction).winnerId, Is.EqualTo(opponent));
            Assert.That(auction.Economy.Purchases.Select(purchase => purchase.item), Is.EqualTo(new[] { first }));

            OpenLot(auction);
            Assert.That(CloseLot(auction).sold, Is.False);
            Assert.That(auction.Economy.Purchases.Select(purchase => purchase.item), Is.EqualTo(new[] { first }));

            var second = auction.CurrentItem;
            OpenLot(auction);
            Click("+$25 Button");
            Assert.That(CloseLot(auction).winnerId, Is.EqualTo(BidderIds.Player));
            Assert.That(budgetLabel.text, Is.EqualTo("Rozpočet  $925"));
            Assert.That(auction.Economy.Purchases.Select(purchase => purchase.item), Is.EqualTo(new[] { first, second }));
            OpenLot(auction);
            Assert.That(CloseLot(auction).sold, Is.False);

            yield return GameIntroTestDriver.OpenFinalReport(false);
            var report = auction.GetComponent<AuctionReportUI>().Report;
            Assert.That(report.TargetProfit, Is.EqualTo(50));
            Assert.That(report.Succeeded, Is.False);
            Click("Retry Button");
            var sale = GameObject.Find("SALE_Character").GetComponent<SaleAuctioneer>();
            Assert.That(sale.CurrentPose, Is.EqualTo(SaleAuctioneer.Pose.Idle));
            Assert.That(sale.GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(GameObject.Find("SALE Bubble"), Is.Null);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            Assert.That(auction.Economy.Purchases, Is.Empty);
            Assert.That(stage.IsPlaying, Is.False);
            Assert.That(Camera.main.transform.localPosition, Is.EqualTo(new Vector3(0f, 14.4f, 25f)));
            Assert.That(Camera.main.fieldOfView, Is.EqualTo(64f));
            Assert.That(GameObject.Find("Auction Result Overlay").GetComponent<Canvas>().enabled, Is.False);
            yield return StartFromNews();
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));

            OpenLot(auction);
            Click("+$10 Button");
            CloseLot(auction);
            while (auction.State == AuctionState.ItemReveal)
            {
                OpenLot(auction);
                CloseLot(auction);
            }
            yield return GameIntroTestDriver.OpenFinalReport(true);
            Assert.That(auction.GetComponent<AuctionReportUI>().Report.Succeeded, Is.True);
            Click("Continue Button");
            Assert.That(GameObject.Find("Level Completion"), Is.Not.Null);

            yield return new ExitPlayMode();
        }

        private static SpriteRenderer[] AssertHandsConfigured()
        {
            var presenter = Object.FindFirstObjectByType<AuctionBidPaddle>();
            Assert.That(presenter.GetComponent<Canvas>(), Is.Null);
            var config = new SerializedObject(presenter);
            var bindings = config.FindProperty("bidderHands");
            Assert.That(bindings.arraySize, Is.EqualTo(5));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BiddersGroup.prefab");
            var hands = new SpriteRenderer[bindings.arraySize];
            for (int index = 0; index < bindings.arraySize; index++)
            {
                var binding = bindings.GetArrayElementAtIndex(index);
                string id = binding.FindPropertyRelative("bidderId").stringValue;
                var character = (SpriteRenderer)binding.FindPropertyRelative("character").objectReferenceValue;
                var hand = character.transform.Find("Raised Bid Hand").GetComponent<SpriteRenderer>();
                var prefabHand = prefab.transform.Find(character.name + "/Raised Bid Hand").GetComponent<SpriteRenderer>();
                Assert.That(hand.sprite, Is.SameAs(prefabHand.sprite));
                Assert.That(prefabHand.enabled, Is.False);
                Assert.That(hand.sprite.name, Is.EqualTo("AuctionHand_" + id));
                Assert.That(hand.sortingLayerID, Is.EqualTo(character.sortingLayerID));
                Assert.That(hand.sortingOrder, Is.LessThan(character.sortingOrder));
                hands[index] = hand;
            }
            return hands;
        }

        private static IEnumerator StartFromNews()
        {
            yield return GameIntroTestDriver.OpenNewsFromIntro();
            yield return GameIntroTestDriver.ReadBroadcast();
        }

        private static void OpenLot(AuctionManager auction)
        {
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            int openingCount = auction.PacesAiInteractions
                ? auction.Opponents.Count(opponent => opponent.IsActiveForLot) : 0;
            auction.Tick(Mathf.Max(AuctionManager.RevealDuration, (openingCount - 1) * auction.AiInteractionInterval));
            if (auction.State == AuctionState.ItemReveal)
                auction.Tick(auction.AiInteractionInterval);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Bidding));
        }

        private static AuctionResult CloseLot(AuctionManager auction)
        {
            auction.Tick(auction.RemainingTime + 1000f);
            AuctionResult result = auction.Results.Last();
            AuctionItemPresentationTests.StartWaitingCinematic(auction);
            var stage = Object.FindFirstObjectByType<SceneIntroOutro>();
            AuctionItemPresentationTests.AdvanceMotion(stage, 1.41f);
            var ribbon = GameObject.Find("Result Ribbon").GetComponent<Image>();
            var stamp = ribbon.transform.Find("SOLD Sticker").GetComponent<Image>();
            Assert.That(stamp.enabled, Is.EqualTo(result.sold));
            Assert.That(ribbon.color.r > ribbon.color.b, Is.EqualTo(result.sold));
            Click("Continue Result");
            AuctionItemPresentationTests.AdvanceMotion(stage, 3f);
            auction.Tick(0.01f);
            return result;
        }

        private static string Caption(string name) => GameObject.Find(name).GetComponentInChildren<TMP_Text>().text;

        private static void Click(string name)
        {
            var button = GameObject.Find(name)?.GetComponent<Button>();
            Assert.That(button && button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }
    }
}
