using System.Collections;
using System.Linq;
using System.Reflection;
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
            var hands = AssertHandsConfigured();
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
            Assert.That(auction.StartAuction(level.CreateRun(), level.Database), Is.True);
            OpenLot(auction);

            string bidderId = AuctionItemPresentationTests.FindBidderWithLimit(auction, 10).Id;
            Assert.That(auction.TryPlaceBid(bidderId, 10), Is.True);
            Assert.That(hands.Single(hand => hand.enabled).sprite.name, Is.EqualTo("AuctionHand_" + bidderId));
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 20), Is.True);
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
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

            var bar = Object.FindFirstObjectByType<AuctionPurchaseBar>();
            var dock = (RectTransform)bar.transform.parent;
            var strip = (RectTransform)bar.transform;
            var icons = bar.GetComponentsInChildren<Image>();
            var timer = Label("Timer");
            var lotName = Label("Lot Name");
            var progress = GameObject.Find("Timer Progress").GetComponent<RectTransform>();
            Canvas.ForceUpdateCanvases();
            Assert.That(timer.rectTransform.TransformPoint(timer.rectTransform.rect.min).y,
                Is.GreaterThan(lotName.rectTransform.TransformPoint(lotName.rectTransform.rect.max).y));
            Assert.That(dock.name, Is.EqualTo("Bid Dock"));
            Assert.That(dock.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(dock.anchorMax, Is.EqualTo(Vector2.right));
            Assert.That(dock.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(dock.sizeDelta, Is.EqualTo(new Vector2(0f, 620f)));
            AssertDockBackgroundExtendsPastSafeArea(dock);
            Assert.That(strip.anchoredPosition.y + strip.rect.yMax, Is.LessThan(dock.rect.height));
            Assert.That(bar.GetComponentsInChildren<Graphic>().All(graphic => !graphic.raycastTarget), Is.True);
            Assert.That(icons, Has.Length.EqualTo(6));
            Assert.That(icons.All(icon => icon.preserveAspect), Is.True);
            Assert.That(GameObject.Find("Opponent Names"), Is.Null);
            Assert.That(Camera.main.targetDisplay, Is.Zero);
            Assert.That(Camera.main.fieldOfView, Is.EqualTo(64f));
            AssertIcons(icons);

            yield return StartFromNews();
            Assert.That(auction.Opponents.Count(opponent => opponent.IsActiveForLot), Is.EqualTo(3));
            Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(1000));
            OpenLot(auction);
            Assert.That(timer.text, Is.EqualTo("15s"));
            Assert.That(progress.anchorMax.x, Is.EqualTo(1f));
            Assert.That(lotName.text, Is.EqualTo(auction.CurrentItem.ItemName));
            Assert.That(GameObject.Find("Current Bid"), Is.Null);
            Assert.That(GameObject.Find("Leading Bidder"), Is.Null);
            Assert.That(GameObject.Find("Latest Bid"), Is.Null);
            var hands = AssertHandsConfigured();
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
            Assert.That(Caption("+$10 Button"), Is.EqualTo("$10"));
            Assert.That(Caption("+$25 Button"), Is.EqualTo("$25"));
            Assert.That(Caption("+$50 Button"), Is.EqualTo("$50"));
            auction.Tick(5f);
            Assert.That(timer.text, Is.EqualTo("10s"));
            Assert.That(progress.anchorMax.x, Is.EqualTo(2f / 3f).Within(0.001f));
            Assert.That(CloseLot(auction).sold, Is.False);
            AssertIcons(icons);

            var first = auction.CurrentItem;
            OpenLot(auction);
            Click("+$50 Button");
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
            Assert.That(Caption("+$10 Button"), Is.EqualTo("+$10"));
            AssertIcons(icons);
            Assert.That(CloseLot(auction).winnerId, Is.EqualTo(BidderIds.Player));
            Assert.That(hands.Count(hand => hand.enabled), Is.Zero);
            Assert.That(Label("Budget").text, Is.EqualTo("Rozpočet  $950"));
            AssertIcons(icons, first.Sprite);

            OpenLot(auction);
            string opponent = AuctionItemPresentationTests.FindBidderWithLimit(auction, 10).Id;
            auction.Tick(auction.AiInteractionInterval);
            Assert.That(auction.TryPlaceBid(opponent, 10), Is.True);
            Assert.That(hands.Single(hand => hand.enabled).sprite.name, Is.EqualTo("AuctionHand_" + opponent));
            Assert.That(CloseLot(auction).winnerId, Is.EqualTo(opponent));
            AssertIcons(icons, first.Sprite);

            OpenLot(auction);
            Assert.That(CloseLot(auction).sold, Is.False);
            AssertIcons(icons, first.Sprite);

            var second = auction.CurrentItem;
            OpenLot(auction);
            Click("+$25 Button");
            Assert.That(CloseLot(auction).winnerId, Is.EqualTo(BidderIds.Player));
            Assert.That(Label("Budget").text, Is.EqualTo("Rozpočet  $925"));
            AssertIcons(icons, first.Sprite, second.Sprite);
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
            AssertIcons(icons);
            Assert.That(stage.IsPlaying, Is.False);
            Assert.That(Camera.main.transform.localPosition, Is.EqualTo(new Vector3(0f, 14.4f, 25f)));
            Assert.That(Camera.main.fieldOfView, Is.EqualTo(64f));
            Assert.That(GameObject.Find("Auction Result Overlay").GetComponent<Canvas>().enabled, Is.False);
            yield return StartFromNews();
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            AssertIcons(icons);

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

        private static void AssertDockBackgroundExtendsPastSafeArea(RectTransform dock)
        {
            var safeArea = dock.GetComponentInParent<AuctionHudSafeArea>();
            var panel = (RectTransform)safeArea.transform.parent;
            var background = dock.Find("Bid Dock Background").GetComponent<Image>();
            Assert.That(background.raycastTarget, Is.False);

            var screenSize = new Vector2(720f, 1280f);
            var insets = Rect.MinMaxRect(24f, 70f, 678f, 1250f);
            var apply = typeof(AuctionHudSafeArea).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic);
            var corners = new Vector3[4];
            Vector2 panelSize = panel.rect.size;
            try
            {
                apply.Invoke(safeArea, new object[] { insets, screenSize, panelSize });
                Vector3 panelBottomLeft = panel.TransformPoint(panel.rect.min);
                Vector3 panelTopRight = panel.TransformPoint(panel.rect.max);
                dock.GetWorldCorners(corners);
                float dockTop = corners[1].y;
                Assert.That(corners[0].y, Is.GreaterThan(panelBottomLeft.y));

                background.rectTransform.GetWorldCorners(corners);
                Assert.That(corners[0].x, Is.EqualTo(panelBottomLeft.x).Within(0.01f));
                Assert.That(corners[0].y, Is.EqualTo(panelBottomLeft.y).Within(0.01f));
                Assert.That(corners[2].x, Is.EqualTo(panelTopRight.x).Within(0.01f));
                Assert.That(corners[1].y, Is.EqualTo(dockTop).Within(0.01f));
            }
            finally
            {
                apply.Invoke(safeArea, new object[] { Screen.safeArea, new Vector2(Screen.width, Screen.height), panelSize });
                Canvas.ForceUpdateCanvases();
            }
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
            Click("Continue Result");
            AuctionItemPresentationTests.AdvanceMotion(stage, 3f);
            auction.Tick(0.01f);
            return result;
        }

        private static void AssertIcons(Image[] icons, params Sprite[] expected)
        {
            for (int index = 0; index < icons.Length; index++)
            {
                Sprite sprite = index < expected.Length ? expected[index] : null;
                Assert.That(icons[index].enabled, Is.EqualTo(sprite != null));
                Assert.That(icons[index].sprite, Is.SameAs(sprite));
            }
        }

        private static TMP_Text Label(string name) => GameObject.Find(name).GetComponent<TMP_Text>();
        private static string Caption(string name) => GameObject.Find(name).GetComponentInChildren<TMP_Text>().text;

        private static void Click(string name)
        {
            var button = GameObject.Find(name)?.GetComponent<Button>();
            Assert.That(button && button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }
    }
}
