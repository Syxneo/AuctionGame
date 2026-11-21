using System.Linq;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AuctionGame.Auction.Tests
{
    internal static class GameIntroTestDriver
    {
        internal static void LoadIntegratedScene(int? dailySeed = null)
        {
            SceneManager.sceneLoaded += ConfigureRun;
            EditorSceneManager.LoadSceneInPlayMode(DailyItemGenerationTests.IntegratedScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));

            void ConfigureRun(Scene scene, LoadSceneMode mode)
            {
                SceneManager.sceneLoaded -= ConfigureRun;
                var preparation = Object.FindFirstObjectByType<PreparationPhase>();
                var data = new SerializedObject(preparation);
                if (!data.FindProperty("useValueCappedGeneration").boolValue)
                    throw new System.InvalidOperationException("The integrated scene must use daily generation.");
                data.FindProperty("useValueCappedGeneration").boolValue = dailySeed.HasValue;
                data.FindProperty("useFixedDailySeed").boolValue = dailySeed.HasValue;
                data.FindProperty("fixedDailySeed").intValue = dailySeed.GetValueOrDefault();
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal static IEnumerator OpenNewsFromIntro()
        {
            var intro = Object.FindFirstObjectByType<GameIntroFlow>();
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.StartScreen));
            Assert.That(GameObject.Find("NEWSSSS presentation"), Is.Null);
            Assert.That(GameObject.Find("SALE briefing"), Is.Null);
            Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            intro.SendMessage("OnApplicationFocus", true);
            Assert.That(GameObject.Find("Parchment").GetComponent<RawImage>().texture, Is.Not.Null);
            var play = GameObject.Find("PLAY").GetComponent<Button>();
            play.onClick.Invoke();
            play.onClick.Invoke();
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.IncomingCall));

            AssertSprite("Incoming Telephone");
            var source = intro.GetComponent<AudioSource>();
            Assert.That(source.clip, Is.Not.Null);
            Assert.That(source.loop, Is.True);

            var phone = GameObject.Find("Incoming Telephone").GetComponent<RectTransform>();
            for (int frame = 0; frame < 15 && Mathf.Abs(phone.anchoredPosition.x) < .1f; frame++)
                yield return null;
            Assert.That(Mathf.Abs(phone.anchoredPosition.x), Is.GreaterThanOrEqualTo(.1f));
            Assert.That(phone.anchoredPosition.y, Is.Zero);

            intro.SendMessage("OnApplicationPause", true);
            Assert.That(phone.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(source.clip, Is.Null);
            intro.SendMessage("OnApplicationFocus", false);
            intro.SendMessage("OnApplicationPause", false);
            Assert.That(source.clip, Is.Null);
            intro.SendMessage("OnApplicationFocus", true);
            Assert.That(source.loop, Is.True);

            Click("Pick Up Phone");
            Assert.That(source.clip, Is.Null);
            Assert.That(source.loop, Is.False);
            AssertSprite("Picked Up Telephone");
            Assert.That(phone.gameObject.activeInHierarchy, Is.False);
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.SaleConversation));
            var messages = GameObject.Find("Messages").transform;
            Assert.That(messages.childCount, Is.Zero);
            for (int count = 1; count <= 3; count++)
            {
                Click("Advance Conversation");
                Assert.That(messages.childCount, Is.EqualTo(count));
            }

            var data = new SerializedObject(intro.GetComponent<PreparationPhase>());
            var level = (MvpAuctionLevel)data.FindProperty("level").objectReferenceValue;
            string finalMessage = messages.GetChild(2).GetComponentInChildren<TMP_Text>().text;
            var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            Assert.That(finalMessage, Does.Contain("$" + level.StartingBudget.ToString("N0", culture)));
            Assert.That(finalMessage, Does.Contain("$" + level.ProfitTarget.ToString("N0", culture)));
            Assert.That(intro.GetComponents<AudioSource>(), Has.Length.EqualTo(1));

            Click("Advance Conversation");
            Click("Advance Conversation");
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.TransitionToNews));
            float deadline = Time.realtimeSinceStartup + 5;
            while (intro.CurrentStage != GameIntroFlow.Stage.Completed && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.Completed));
            Assert.That(GameObject.Find("Continue News"), Is.Not.Null);
            Assert.That(GameObject.Find("RetroTVFrame"), Is.Null);
            Assert.That(GameObject.Find("NewsBodyText"), Is.Null);
            AssertSprite("Reporter");
            Assert.That(GameObject.Find("SALE briefing"), Is.Null);
        }

        internal static IEnumerator ReadBroadcast()
        {
            var preparation = Object.FindFirstObjectByType<PreparationPhase>();
            var cards = preparation.SelectedNews;
            Assert.That(cards.Count, Is.EqualTo(cards.Count(card => card.Type == NewsType.Item) + 3));
            var view = preparation.GetComponentInChildren<BreakingNewsView>();
            var tap = GameObject.Find("Continue News").GetComponent<Button>();
            var safe = (RectTransform)GameObject.Find("News Safe Area").transform;
            Assert.That(safe.anchorMin, Is.EqualTo(new Vector2(
                Screen.safeArea.xMin / Screen.width, Screen.safeArea.yMin / Screen.height)));
            Assert.That(safe.anchorMax, Is.EqualTo(new Vector2(
                Screen.safeArea.xMax / Screen.width, Screen.safeArea.yMax / Screen.height)));
            foreach (var card in cards)
            {
                yield return null;
                Assert.That(GameObject.Find("Start Auction"), Is.Null);
                preparation.RequestStart();
                Canvas.ForceUpdateCanvases();
                var pointer = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left,
                    position = new Vector2(Screen.width * .03f, Screen.height * .94f)
                };
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, hits);
                Assert.That(hits, Is.Not.Empty);
                Assert.That(hits[0].gameObject, Is.SameAs(tap.gameObject));
                ExecuteEvents.Execute(tap.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                tap.onClick.Invoke();
                Assert.That(view.CurrentCard, Is.SameAs(card));
                Assert.That(GameObject.Find("Reporter"), Is.Null);
                Assert.That(GameObject.Find("RetroTVFrame").GetComponent<Image>().sprite, Is.Not.Null);
                var image = GameObject.Find("TVScreenContent").GetComponent<Image>();
                Assert.That(image.sprite == card.Image, Is.True, card.Id);
                Assert.That(image.preserveAspect, Is.True);
                Assert.That(image.gameObject, Is.Not.SameAs(GameObject.Find("RetroTVFrame")));
                var label = GameObject.Find("NewsBodyText").GetComponent<TMP_Text>();
                Assert.That(label.text, Is.EqualTo(card.Body));
                Canvas.ForceUpdateCanvases();
                label.ForceMeshUpdate();
                Assert.That(label.isTextOverflowing, Is.False, card.Id);
                Assert.That(view.GetComponentsInChildren<ScrollRect>(), Is.Empty);
                Assert.That(view.GetComponentsInChildren<Button>(), Has.Length.EqualTo(1));
                Assert.That(view.GetComponentsInChildren<TMP_Text>().Length,
                    Is.EqualTo(card.Image ? 4 : 5));
            }

            yield return null;
            tap.onClick.Invoke();
            Assert.That(GameObject.Find("Continue News"), Is.Null);
            Click("Start Auction");
            Assert.That(GameObject.Find("BreakingNewsCanvas"), Is.Null);
            tap.onClick.Invoke();
            Assert.That(view.CurrentCard, Is.Null);
        }

        private static void AssertSprite(string name)
        {
            var target = GameObject.Find(name);
            Assert.That(target, Is.Not.Null, name);
            Assert.That(target.GetComponent<Image>().sprite, Is.Not.Null, name);
        }

        internal static IEnumerator OpenFinalReport(bool? success = null)
        {
            var intro = Object.FindFirstObjectByType<GameIntroFlow>();
            var report = intro.GetComponent<AuctionReportUI>();
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.EndIncomingCall));
            Assert.That(report.Report, Is.Null);
            Assert.That(GameObject.Find("SALE Report"), Is.Null);
            AssertSprite("SALE Telephone");
            Assert.That(intro.GetComponent<AudioSource>().loop, Is.True);
            Assert.That(GameObject.Find("AuctionPanel"), Is.Null);
            Click("Pick Up SALE Call");
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.EndConversation));
            Assert.That(intro.GetComponent<AudioSource>().loop, Is.False);
            var line = GameObject.Find("SALE Line").GetComponent<TMP_Text>();
            if (success.HasValue)
                Assert.That(line.text, Does.StartWith(success.Value ? "Well, well, well" : "You did terrible"));
            string first = line.text;
            Click("Advance SALE Call");
            Assert.That(line.text, Is.EqualTo(first), "Pickup and an advance in the same frame count as one tap.");
            for (int index = 0; index < 3; index++)
            {
                yield return null;
                string before = line.text;
                Click("Advance SALE Call");
                if (index < 2)
                {
                    Click("Advance SALE Call");
                    yield return new WaitForSecondsRealtime(.25f);
                    Assert.That(line.text, Is.Not.EqualTo(before));
                    Assert.That(report.Report, Is.Null);
                }
            }
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.EndTransition));
            float deadline = Time.realtimeSinceStartup + 5;
            while (GameObject.Find("Intro Fade") && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.EndReport));
            Assert.That(GameObject.Find("SALE Telephone"), Is.Null);
            Assert.That(GameObject.Find("SALE Report").GetComponentInParent<Canvas>().targetDisplay, Is.Zero);
            Assert.That(report.Report, Is.Not.Null);
            VerifyReportRows(report.Report);
            VerifyPortraitLayout();
        }

        private static void VerifyPortraitLayout()
        {
            var report = (RectTransform)GameObject.Find("SALE Report").transform;
            Assert.That(GameObject.Find("Report Parchment").GetComponent<Image>().sprite.name,
                Is.EqualTo("SaleReportParchment"));
            var outline = report.Find("Report Frame/Ink Outline").GetComponent<Image>();
            Assert.That(outline.sprite.name, Is.EqualTo("SaleReportFrame"));
            Assert.That(outline.type, Is.EqualTo(Image.Type.Sliced));
            var reportAction = (report.Find("Continue Button") ?? report.Find("Retry Button")).GetComponent<Image>();
            Assert.That(reportAction.sprite.name, Is.EqualTo("SaleReportButton"));
            var safe = (RectTransform)report.parent;
            var oldMin = safe.anchorMin;
            var oldMax = safe.anchorMax;
            var oldOffsetMin = safe.offsetMin;
            var oldOffsetMax = safe.offsetMax;
            try
            {
                foreach (var screen in new[] { new Vector2(720, 1280), new Vector2(1080, 1920),
                    new Vector2(720, 1600), new Vector2(1080, 2340) })
                {
                    safe.anchorMin = safe.anchorMax = new Vector2(.5f, .5f);
                    safe.sizeDelta = new Vector2(720, 720 * screen.y / screen.x) - new Vector2(36, 84);
                    safe.anchoredPosition = Vector2.zero;
                    Canvas.ForceUpdateCanvases();
                    var frame = (RectTransform)report.Find("Report Frame");
                    var action = (RectTransform)(report.Find("Continue Button") ?? report.Find("Retry Button"));
                    var area = safe.rect;
                    var frameMin = safe.InverseTransformPoint(frame.TransformPoint(frame.rect.min));
                    var frameMax = safe.InverseTransformPoint(frame.TransformPoint(frame.rect.max));
                    var actionBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(safe, action);
                    Assert.That(frameMin.x, Is.GreaterThanOrEqualTo(area.xMin));
                    Assert.That(frameMax.x, Is.LessThanOrEqualTo(area.xMax));
                    Assert.That(actionBounds.min.y, Is.GreaterThanOrEqualTo(area.yMin));
                    Assert.That(actionBounds.max.y, Is.LessThan(frameMin.y));
                }
            }
            finally
            {
                safe.anchorMin = oldMin;
                safe.anchorMax = oldMax;
                safe.offsetMin = oldOffsetMin;
                safe.offsetMax = oldOffsetMax;
                Canvas.ForceUpdateCanvases();
            }
        }

        private static void VerifyReportRows(AuctionReport report)
        {
            if (report.Purchases.Count == 0)
            {
                Assert.That(GameObject.Find("Empty State").GetComponent<TMP_Text>().text, Is.EqualTo("No items purchased."));
                Assert.That(report.TotalProfit, Is.Zero);
                return;
            }

            var scroll = GameObject.Find("Purchases").GetComponent<ScrollRect>();
            Assert.That(scroll.vertical && !scroll.horizontal, Is.True);
            Assert.That(scroll.content.childCount, Is.EqualTo(report.Purchases.Count));
            for (int index = 0; index < report.Purchases.Count; index++)
            {
                var purchase = report.Purchases[index];
                var row = scroll.content.GetChild(index);
                if (purchase.Sprite)
                    Assert.That(row.Find("Item sprite").GetComponent<Image>().sprite, Is.SameAs(purchase.Sprite));
                Assert.That(row.Find("Your Bid").GetComponent<TMP_Text>().text, Is.EqualTo("$" + purchase.PurchasePrice));
                Assert.That(row.Find("Worth").GetComponent<TMP_Text>().text, Is.EqualTo("$" + purchase.TrueValue));
                string expectedProfit = purchase.Profit > 0 ? "+$" + purchase.Profit : purchase.Profit < 0
                    ? "-$" + -purchase.Profit : "$0";
                Assert.That(row.Find("Profit").GetComponent<TMP_Text>().text, Is.EqualTo(expectedProfit));
            }
        }

        private static void Click(string name)
        {
            var button = GameObject.Find(name)?.GetComponent<Button>();
            Assert.That(button && button.IsInteractable(), Is.True, name);
            button.onClick.Invoke();
        }
    }
}
