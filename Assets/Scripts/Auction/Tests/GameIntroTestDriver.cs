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
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AuctionGame.Auction.Tests
{
    public sealed class IntroNewsEffectsTests
    {
        [UnityTest]
        public IEnumerator IntegratedIntroNewsAndBothCalls()
        {
            yield return new EnterPlayMode();
            GameIntroTestDriver.LoadIntegratedScene();
            yield return null;
            yield return GameIntroTestDriver.OpenNewsFromIntro();
            yield return GameIntroTestDriver.ReadBroadcast();

            var intro = Object.FindFirstObjectByType<GameIntroFlow>();
            intro.ShowEndCall(false, _ => { });
            yield return GameIntroTestDriver.CheckRingLines("SALE Telephone");
            GameObject.Find("Pick Up SALE Call").GetComponent<Button>().onClick.Invoke();
            Assert.That(GameObject.Find("Telephone Ring Lines"), Is.Null);
            Assert.That(GameObject.Find("SALE Line"), Is.Not.Null);
            yield return new ExitPlayMode();
        }
    }

    internal static class GameIntroTestDriver
    {
        internal static void LoadIntegratedScene(int? dailySeed = null, bool campaign = false)
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
                if (!data.FindProperty("useCampaign").boolValue)
                    throw new System.InvalidOperationException("The integrated scene must enable the campaign.");
                data.FindProperty("useCampaign").boolValue = campaign;
                data.FindProperty("useValueCappedGeneration").boolValue = dailySeed.HasValue || campaign;
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
            AssertSprite("Logo");
            var play = GameObject.Find("PLAY").GetComponent<Button>();
            Canvas.ForceUpdateCanvases();
            var dust = GameObject.Find("Paper Dust").GetComponentsInChildren<Image>();
            Assert.That(dust.Length, Is.InRange(4, 8));
            Assert.That(dust.All(speck => !speck.raycastTarget), Is.True);
            var press = play.GetComponent<BidButtonPress>();
            var playRect = (RectTransform)play.transform;
            Vector2 edge = RectTransformUtility.WorldToScreenPoint(null, playRect.TransformPoint(
                new Vector2(playRect.rect.xMax - 2, playRect.rect.center.y)));
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left, position = edge
            };
            play.interactable = false;
            press.OnPointerDown(pointer);
            Assert.That(playRect.localScale, Is.EqualTo(Vector3.one));
            play.interactable = true;
            press.OnPointerDown(pointer);
            Assert.That(playRect.localScale.x, Is.EqualTo(.94f).Within(.001f));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits[0].gameObject, Is.SameAs(play.gameObject));
            press.OnPointerUp(pointer);
            play.onClick.Invoke();
            play.onClick.Invoke();
            Assert.That(playRect.localScale, Is.EqualTo(Vector3.one));
            Assert.That(GameObject.Find("Paper Dust"), Is.Null);
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.IncomingCall));

            AssertSprite("Incoming Telephone");
            Assert.That(GameObject.Find("Incoming Telephone").GetComponent<TelephonePresentation>(), Is.Not.Null);
            var source = intro.GetComponent<AudioSource>();
            Assert.That(source.clip, Is.Not.Null);
            Assert.That(source.loop, Is.True);

            var phone = GameObject.Find("Incoming Telephone");
            yield return CheckRingLines("Incoming Telephone");
            Click("Pick Up Phone");
            Assert.That(GameObject.Find("Telephone Ring Lines"), Is.Null);
            Assert.That(source.loop, Is.False);
            AssertSprite("Picked Up Telephone");
            Assert.That(phone.activeInHierarchy, Is.False);
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.SaleConversation));
            var messages = GameObject.Find("Messages").transform;
            Assert.That(messages.childCount, Is.EqualTo(1));
            Click("Advance Conversation");
            Assert.That(messages.childCount, Is.EqualTo(1), "Pickup and advance in the same frame count as one tap.");
            for (int count = 2; count <= 3; count++)
            {
                yield return null;
                Click("Advance Conversation");
                Assert.That(messages.childCount, Is.EqualTo(count));
            }

            var preparation = intro.GetComponent<PreparationPhase>();
            string finalMessage = messages.GetChild(2).GetComponentInChildren<TMP_Text>().text;
            var culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            Assert.That(finalMessage, Does.Contain("$" + preparation.StartingBudget.ToString("N0", culture)));
            Assert.That(finalMessage, Does.Contain("$" + preparation.ProfitTarget.ToString("N0", culture)));
            Assert.That(intro.GetComponents<AudioSource>(), Has.Length.EqualTo(1));

            yield return null;
            Click("Advance Conversation");
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.TransitionToNews));
            float deadline = Time.realtimeSinceStartup + 5;
            while (intro.CurrentStage != GameIntroFlow.Stage.Completed && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.Completed));
            var newsAudio = GameObject.Find("Breaking News").GetComponent<AudioSource>();
            Assert.That(newsAudio.clip, Is.Not.Null);
            Assert.That(newsAudio.clip.name, Is.EqualTo("NewsIntroJingle"));
            Assert.That(newsAudio.isPlaying, Is.True);
            Assert.That(GameObject.Find("Continue News"), Is.Not.Null);
            Assert.That(GameObject.Find("RetroTVFrame"), Is.Null);
            Assert.That(GameObject.Find("NewsBodyText"), Is.Null);
            AssertSprite("Reporter");
            Assert.That(GameObject.Find("SALE briefing"), Is.Null);
            var headline = GameObject.Find("BreakingNewsHeader").GetComponent<CanvasGroup>();
            Assert.That(headline.alpha, Is.LessThan(1));
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(headline.alpha, Is.EqualTo(1));
            Assert.That(headline.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(GameObject.Find("Presenter Dialogue"), Is.Not.Null);
            Vector3[] reporterCorners = new Vector3[4];
            ((RectTransform)GameObject.Find("Reporter").transform).GetWorldCorners(reporterCorners);
            Assert.That(RectTransformUtility.WorldToScreenPoint(null, reporterCorners[0]).y,
                Is.EqualTo(0).Within(1));
        }

        internal static IEnumerator CheckRingLines(string phoneName)
        {
            var phone = GameObject.Find(phoneName);
            var lines = phone.transform.Find("Telephone Ring Lines").GetComponent<CanvasGroup>();
            Assert.That(lines.transform.childCount, Is.EqualTo(2));
            Assert.That(lines.GetComponentsInChildren<Image>().All(stroke => !stroke.raycastTarget), Is.True);
            float deadline = Time.realtimeSinceStartup + 1;
            while (lines.alpha <= .1f && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(lines.alpha, Is.GreaterThan(.1f));
        }

        internal static IEnumerator ReadBroadcast(System.Action duringOpening = null)
        {
            var preparation = Object.FindFirstObjectByType<PreparationPhase>();
            var auction = preparation.GetComponent<AuctionManager>();
            var stage = Object.FindFirstObjectByType<SceneIntroOutro>();
            var camera = Camera.main;
            var position = camera.transform.localPosition;
            var rotation = camera.transform.localRotation;
            float fov = camera.fieldOfView;
            var stageSettings = new SerializedObject(stage);
            var curtain = (Transform)stageSettings.FindProperty("leftCurtain").objectReferenceValue;
            var curtainPosition = curtain.localPosition;
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
                var previous = view.CurrentCard;
                ExecuteEvents.Execute(tap.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                tap.onClick.Invoke();
                if (previous != null)
                {
                    Assert.That(view.CurrentCard, Is.SameAs(previous));
                    yield return null;
                    tap.onClick.Invoke();
                    float transitionDeadline = Time.realtimeSinceStartup + 2;
                    var staticOverlay = GameObject.Find("TV Static").GetComponent<RawImage>();
                    bool sawStatic = false;
                    while (Time.realtimeSinceStartup < transitionDeadline)
                    {
                        sawStatic |= staticOverlay.enabled;
                        if (view.CurrentCard == card && !staticOverlay.enabled && sawStatic)
                            break;
                        yield return null;
                    }
                    Assert.That(sawStatic, Is.True);
                    Assert.That(staticOverlay.enabled, Is.False);
                    yield return new WaitForSecondsRealtime(.08f);
                }
                Assert.That(view.CurrentCard, Is.SameAs(card));
                Assert.That(GameObject.Find("Reporter"), Is.Null);
                Assert.That(GameObject.Find("RetroTVFrame").GetComponent<Image>().sprite, Is.Not.Null);
                var image = GameObject.Find("TVScreenContent").GetComponent<Image>();
                Assert.That(image.sprite == card.Image, Is.True, card.Id);
                Assert.That(image.preserveAspect, Is.True);
                Assert.That(image.gameObject, Is.Not.SameAs(GameObject.Find("RetroTVFrame")));
                var scanlines = GameObject.Find("TV Scanlines").GetComponent<RawImage>();
                Assert.That(scanlines.raycastTarget, Is.False);
                Assert.That(scanlines.GetComponentInParent<RectMask2D>(), Is.SameAs(
                    image.GetComponentInParent<RectMask2D>()));
                Assert.That(GameObject.Find("TV Static").GetComponent<RawImage>().raycastTarget, Is.False);
                var label = GameObject.Find("NewsBodyText").GetComponent<TMP_Text>();
                Assert.That(label.text, Is.EqualTo(card.Body));
                Assert.That(label.GetComponentInParent<BidderSpeechBubble>(), Is.Not.Null);
                Canvas.ForceUpdateCanvases();
                label.ForceMeshUpdate();
                Assert.That(label.isTextOverflowing, Is.False, card.Id);
                Assert.That(view.GetComponentsInChildren<ScrollRect>(), Is.Empty);
                Assert.That(view.GetComponentsInChildren<Button>(), Has.Length.EqualTo(1));
                Assert.That(view.GetComponentsInChildren<TMP_Text>().Length,
                    Is.EqualTo(card.Image ? 4 : 5));
            }

            yield return null;
            int introductions = 0;
            int dialogueLines = 0;
            auction.OnItemChanged += CountIntroduction;
            auction.OnAiDialogue += CountDialogue;
            tap.onClick.Invoke();
            Assert.That(GameObject.Find("Continue News"), Is.Null);
            var start = GameObject.Find("Start Auction").GetComponent<Button>();
            Assert.That(auction.State, Is.EqualTo(AuctionState.Opening));
            Assert.That(auction.RemainingTime, Is.Zero);
            Assert.That(introductions, Is.Zero);
            Assert.That(dialogueLines, Is.Zero);
            var hud = GameObject.Find("AuctionPanel").GetComponent<CanvasGroup>();
            Assert.That(hud.alpha, Is.Zero);
            Assert.That(hud.interactable, Is.False);
            Assert.That(hud.blocksRaycasts, Is.False);
            var visual = GameObject.Find("AuctionItemVisual").GetComponent<SpriteRenderer>();
            Assert.That(visual.sprite, Is.SameAs(auction.CurrentItem.Sprite));
            Assert.That(visual.forceRenderingOff, Is.True);
            Assert.That(curtain.localPosition.x, Is.EqualTo(stageSettings.FindProperty("curtainClosedX").floatValue));
            var closePosition = camera.transform.localPosition;
            Assert.That(closePosition, Is.Not.EqualTo(position));

            yield return null;
            Canvas.ForceUpdateCanvases();
            var corner = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = new Vector2(Screen.width * .03f, Screen.height * .94f)
            };
            var cornerHits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(corner, cornerHits);
            Assert.That(cornerHits, Is.Not.Empty);
            Assert.That(cornerHits[0].gameObject, Is.SameAs(start.gameObject));
            ExecuteEvents.Execute(start.gameObject, corner, ExecuteEvents.pointerClickHandler);
            start.onClick.Invoke();
            Assert.That(GameObject.Find("BreakingNewsCanvas"), Is.Null);
            auction.Tick(60f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Opening));
            Assert.That(auction.BidHistory, Is.Empty);
            Assert.That(auction.TryPlaceBid(BidderIds.Player, 10), Is.False);
            Assert.That(auction.TryPlaceBid(auction.Opponents[0].Id, 10), Is.False);
            yield return null;
            yield return null;
            Assert.That(visual.forceRenderingOff, Is.False);
            Assert.That(camera.transform.localPosition, Is.Not.EqualTo(closePosition));
            Assert.That(curtain.localPosition.x, Is.GreaterThan(stageSettings.FindProperty("curtainClosedX").floatValue));
            Assert.That(auction.State, Is.EqualTo(AuctionState.Opening));
            Assert.That(introductions, Is.Zero);
            Assert.That(dialogueLines, Is.Zero);
            Assert.That(auction.RemainingTime, Is.Zero);
            Assert.That(hud.alpha, Is.Zero);
            duringOpening?.Invoke();

            float deadline = Time.realtimeSinceStartup + 5f;
            while (auction.State == AuctionState.Opening && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            Assert.That(introductions, Is.EqualTo(1));
            Assert.That(camera.transform.localPosition, Is.EqualTo(position));
            Assert.That(camera.transform.localRotation, Is.EqualTo(rotation));
            Assert.That(camera.fieldOfView, Is.EqualTo(fov));
            Assert.That(curtain.localPosition, Is.EqualTo(curtainPosition));
            auction.OnItemChanged -= CountIntroduction;
            auction.OnAiDialogue -= CountDialogue;
            tap.onClick.Invoke();
            Assert.That(view.CurrentCard, Is.Null);

            void CountIntroduction(AuctionItem _) => introductions++;
            void CountDialogue(string bidder, string category, string line) => dialogueLines++;
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
            if (report.Report != null)
            {
                Assert.That(GameObject.Find("AuctionPanel"), Is.Null);
                Assert.That(GameObject.Find("SALE Report").GetComponentInParent<Canvas>().targetDisplay, Is.Zero);
                Assert.That(GameObject.Find(report.Report.Succeeded ? "Continue Button" : "Retry Button"), Is.Not.Null);
                if (success.HasValue)
                    Assert.That(report.Report.Succeeded, Is.EqualTo(success.Value));
                yield break;
            }
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.EndIncomingCall));
            Assert.That(report.Report, Is.Null);
            Assert.That(GameObject.Find("SALE Report"), Is.Null);
            AssertSprite("SALE Telephone");
            Assert.That(GameObject.Find("SALE Telephone").GetComponent<TelephonePresentation>(), Is.Not.Null);
            Assert.That(intro.GetComponent<AudioSource>().loop, Is.True);
            Assert.That(GameObject.Find("AuctionPanel"), Is.Null);
            Click("Pick Up SALE Call");
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.EndConversation));
            Assert.That(intro.GetComponent<AudioSource>().loop, Is.False);
            var history = GameObject.Find("SALE Dialogue").GetComponent<ScrollRect>().content;
            var line = history.GetChild(0).GetComponentInChildren<TMP_Text>();
            if (success.HasValue)
                Assert.That(line.text, Does.StartWith(success.Value ? "Ale, ale, ale" : "To bola bieda"));
            string first = line.text;
            Click("Advance SALE Call");
            Assert.That(history.childCount, Is.EqualTo(1), "Pickup and an advance in the same frame count as one tap.");
            for (int index = 0; index < 2; index++)
            {
                yield return null;
                float oldPosition = line.rectTransform.position.y;
                Click("Advance SALE Call");
                Click("Advance SALE Call");
                Assert.That(history.childCount, Is.EqualTo(index + 2));
                Assert.That(line.text, Is.EqualTo(first));
                Assert.That(line.rectTransform.position.y, Is.GreaterThan(oldPosition));
                Assert.That(report.Report, Is.Null);
            }
            yield return null;
            Click("Advance SALE Call");
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.EndTransition));
            float deadline = Time.realtimeSinceStartup + 5;
            while (GameObject.Find("Intro Fade") && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(intro.CurrentStage, Is.EqualTo(GameIntroFlow.Stage.EndReport));
            Assert.That(GameObject.Find("SALE Telephone"), Is.Null);
            Assert.That(GameObject.Find("SALE Report").GetComponentInParent<Canvas>().targetDisplay, Is.Zero);
            Assert.That(report.Report, Is.Not.Null);
            if (report.Report.Purchases.Count > 0)
            {
                var profit = GameObject.Find("Celkový zisk Value").GetComponent<TMP_Text>();
                var content = GameObject.Find("Purchases").GetComponent<ScrollRect>().content;
                Assert.That(profit.alpha, Is.Zero);
                Assert.That(content.GetChild(content.childCount - 1).GetComponent<CanvasGroup>().alpha, Is.LessThan(1));
                deadline = Time.realtimeSinceStartup + 5;
                while (profit.alpha == 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(profit.alpha, Is.GreaterThan(0));
            }
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
                Assert.That(GameObject.Find("Empty State").GetComponent<TMP_Text>().text, Is.EqualTo("Žiadne nákupy"));
                Assert.That(report.TotalProfit, Is.Zero);
                return;
            }

            var scroll = GameObject.Find("Purchases").GetComponent<ScrollRect>();
            Assert.That(scroll.vertical && !scroll.horizontal, Is.True);
            Assert.That(scroll.content.childCount, Is.EqualTo(report.Purchases.Count));
            if (scroll.content.rect.height > scroll.viewport.rect.height)
                Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(0).Within(.001f));
            for (int index = 0; index < report.Purchases.Count; index++)
            {
                var purchase = report.Purchases[index];
                var row = scroll.content.GetChild(index);
                Assert.That(row.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1));
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
