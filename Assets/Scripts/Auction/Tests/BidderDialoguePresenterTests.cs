using System.Collections;
using System.Collections.Generic;
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
    public sealed class BidderDialoguePresenterTests
    {
        [UnityTest]
        public IEnumerator HallBindsRealSpritesAndPresentsOpeningAndPriorities()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/AuctionHallDebug.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var presenter = Object.FindFirstObjectByType<BidderDialoguePresenter>();
            Assert.That(presenter, Is.Not.Null);
            var settings = new SerializedObject(presenter);
            var auction = (AuctionManager)settings.FindProperty("auction").objectReferenceValue;
            var canvas = (Canvas)settings.FindProperty("canvas").objectReferenceValue;
            var bubblePrefab = (BidderSpeechBubble)settings.FindProperty("bubblePrefab").objectReferenceValue;
            Assert.That(AssetDatabase.GetAssetPath(bubblePrefab), Is.EqualTo("Assets/Prefabs/BidderSpeechBubble.prefab"));
            var bindings = settings.FindProperty("bidders");
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(canvas.targetDisplay, Is.Zero);
            var hallCamera = (Camera)settings.FindProperty("sceneCamera").objectReferenceValue;
            Assert.That(hallCamera.targetDisplay, Is.Zero);
            var debugCanvas = GameObject.Find("AuctionDebugCanvasCanvas").GetComponent<Canvas>();
            Assert.That(debugCanvas.targetDisplay, Is.EqualTo(1));
            Assert.That(debugCanvas.isActiveAndEnabled, Is.True);
            Assert.That(GameObject.Find("Auction Camera"), Is.Null);
            bool secondaryWasActive = SecondaryDisplayActive();
            Assert.That(bindings.arraySize, Is.EqualTo(3));
            CheckBindings(bindings);
            Object.FindFirstObjectByType<SceneIntroOutro>().SkipToEnd();
            var managerSettings = new SerializedObject(auction);
            managerSettings.FindProperty("enableAutomaticAi").boolValue = false;
            managerSettings.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            var preparation = new SerializedObject(auction.GetComponent<PreparationPhase>());
            var level = (MvpAuctionLevel)preparation.FindProperty("level").objectReferenceValue;
            GameObject.Find("Read NEWSSSS").GetComponent<Button>().onClick.Invoke();
            foreach (var entry in level.GetNewspaperEntries())
                GameObject.Find("Continue News").GetComponent<Button>().onClick.Invoke();
            var start = GameObject.Find("Start Auction").GetComponent<Button>();
            Assert.That(start.GetComponentInParent<Canvas>().targetDisplay, Is.Zero);
            start.onClick.Invoke();
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            auction.Tick(2);
            yield return null;
            VerifyDebugDisplayIgnoresPrimaryPointer(debugCanvas);
            Assert.That(Visible(canvas).Length, Is.EqualTo(3));
            VerifyPrefabStyling(bubblePrefab, Visible(canvas)[0]);
            VerifyLayout(canvas);
            VerifyGlyphs(canvas);
            VerifyMotionAndAnimation(presenter, canvas, bindings);

            Emit(auction, "collector", "WD-01", "Potvrdené odstúpenie.");
            Emit(auction, "collector", "BR-01", "Táto hláška je slabšia.");
            Emit(auction, "trickster", "BR-01", "Reakcia na cenu.");
            Emit(auction, "analyst", "BR-02", "Ďalšia reakcia na cenu.");
            Render(presenter, Time.unscaledTimeAsDouble + 0.21);
            Assert.That(Text(canvas, "collector"), Is.EqualTo("Potvrdené odstúpenie."));
            Assert.That(Text(canvas, "trickster"), Is.EqualTo("Reakcia na cenu."));
            Assert.That(Text(canvas, "analyst"), Is.EqualTo("Ďalšia reakcia na cenu."));

            // Reset transient messages via the next lot, then check the ordinary line cap.
            auction.Tick(17);
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            Assert.That(Visible(canvas), Is.Empty);
            VerifyOrdinaryCapAndSale(auction);
            yield return null;
            Assert.That(Visible(canvas).Length, Is.EqualTo(2));
            VerifyUiDoesNotChangeAuction(presenter, auction);
            Assert.That(SecondaryDisplayActive(), Is.EqualTo(secondaryWasActive));

            yield return new ExitPlayMode();
        }

        private static bool SecondaryDisplayActive() => Display.displays.Length > 1 && Display.displays[1].active;

        private static void VerifyDebugDisplayIgnoresPrimaryPointer(Canvas debugCanvas)
        {
            var button = GameObject.Find("+$10 Button").GetComponent<Button>();
            Assert.That(button.GetComponentInParent<Canvas>(), Is.SameAs(debugCanvas));
            Assert.That(button.IsInteractable(), Is.True);
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current)
            {
                displayIndex = 0,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
            var hits = new List<RaycastResult>();
            debugCanvas.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Assert.That(hits, Is.Empty);
        }

        private static void CheckBindings(SerializedProperty bindings)
        {
            string[] ids = { "collector", "trickster", "analyst" };
            string[] sprites = { "Character4", "Character3", "Character1" };
            for (int index = 0; index < ids.Length; index++)
            {
                var entry = bindings.GetArrayElementAtIndex(index);
                var sprite = (SpriteRenderer)entry.FindPropertyRelative("sprite").objectReferenceValue;
                Assert.That(entry.FindPropertyRelative("bidderId").stringValue, Is.EqualTo(ids[index]));
                Assert.That(sprite, Is.Not.Null);
                Assert.That(sprite.name, Is.EqualTo(sprites[index]));
                Assert.That(sprite.transform.parent.name, Is.EqualTo("BiddersGroup"));
            }
        }

        private static BidderSpeechBubble[] Visible(Canvas canvas) => canvas.GetComponentsInChildren<BidderSpeechBubble>();

        private static void VerifyPrefabStyling(BidderSpeechBubble prefab, BidderSpeechBubble instance)
        {
            var source = new SerializedObject(prefab);
            var runtime = new SerializedObject(instance);
            foreach (string part in new[] { "background", "pointer" })
            {
                var sourceImage = (Image)source.FindProperty(part).objectReferenceValue;
                var runtimeImage = (Image)runtime.FindProperty(part).objectReferenceValue;
                Assert.That(sourceImage.sprite, Is.Not.Null);
                Assert.That(runtimeImage.sprite, Is.SameAs(sourceImage.sprite));
                Assert.That(runtimeImage.raycastTarget, Is.False);
            }
        }

        private static string Text(Canvas canvas, string id) =>
            canvas.transform.Find(id + " Bubble").GetComponentInChildren<TMP_Text>().text;

        private static void VerifyGlyphs(Canvas canvas)
        {
            var font = Visible(canvas)[0].GetComponentInChildren<TMP_Text>().font;
            const string glyphs = "áčďéíľĺňóôŕšťúýž";
            Assert.That(font.HasCharacters(glyphs, out uint[] missing, searchFallbacks: true, tryAddCharacter: true),
                Is.True, string.Join(", ", missing ?? System.Array.Empty<uint>()));
        }

        private static void VerifyLayout(Canvas canvas)
        {
            var bounds = Visible(canvas).Select(bubble =>
            {
                var rect = bubble.GetComponent<RectTransform>();
                return new Rect(rect.anchoredPosition - rect.sizeDelta / 2, rect.sizeDelta);
            }).ToArray();
            Rect safe = ((RectTransform)canvas.transform).rect;
            for (int index = 0; index < bounds.Length; index++)
            {
                Assert.That(bounds[index].xMin, Is.GreaterThanOrEqualTo(-safe.width / 2));
                Assert.That(bounds[index].xMax, Is.LessThanOrEqualTo(safe.width / 2));
                Assert.That(bounds[index].yMin, Is.GreaterThanOrEqualTo(-safe.height / 2));
                Assert.That(bounds[index].yMax, Is.LessThanOrEqualTo(safe.height / 2));
                for (int other = index + 1; other < bounds.Length; other++)
                    Assert.That(bounds[index].Overlaps(bounds[other]), Is.False);
            }
        }

        private static void VerifyMotionAndAnimation(BidderDialoguePresenter presenter, Canvas canvas,
            SerializedProperty bindings)
        {
            double now = Time.unscaledTimeAsDouble;
            Render(presenter, now + 0.25);
            var bubble = canvas.transform.Find("collector Bubble").GetComponent<RectTransform>();
            Assert.That(bubble.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1));
            Assert.That(bubble.localScale, Is.EqualTo(Vector3.one));
            var sprite = (SpriteRenderer)bindings.GetArrayElementAtIndex(0)
                .FindPropertyRelative("sprite").objectReferenceValue;
            Vector3 original = sprite.transform.position;
            Vector2 before = bubble.anchoredPosition;
            sprite.transform.position += Vector3.left;
            for (int frame = 0; frame < 30; frame++)
                Render(presenter, now + 0.5);
            Assert.That(Vector2.Distance(before, bubble.anchoredPosition), Is.GreaterThan(1));
            sprite.transform.position = original;
        }

        private static void Render(BidderDialoguePresenter presenter, double time) =>
            typeof(BidderDialoguePresenter).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(presenter, new object[] { time, 0.05f });

        private static void VerifyOrdinaryCapAndSale(AuctionManager auction)
        {
            Emit(auction, "collector", "BR-01", "Prvá reakcia.");
            Emit(auction, "trickster", "BR-03", "Druhá reakcia.");
            Emit(auction, "analyst", "BR-04", "Tretia reakcia.");
            Emit(auction, "trickster", "SALE-01", "Predané!");
        }

        private static void VerifyUiDoesNotChangeAuction(BidderDialoguePresenter presenter, AuctionManager auction)
        {
            auction.Tick(1000);
            var manager = new SerializedObject(auction);
            manager.FindProperty("enableAutomaticAi").boolValue = true;
            manager.ApplyModifiedPropertiesWithoutUndo();
            var settings = new SerializedObject(presenter);

            string[] Run(bool visible)
            {
                settings.FindProperty("enableDialogueUi").boolValue = visible;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(auction.RestartAuction(), Is.True);
                var bids = new List<string>();
                void Record(BidRecord bid) => bids.Add($"{bid.itemId}:{bid.bidderId}:{bid.amount}:{bid.biddingElapsed:R}");
                auction.OnBidPlaced += Record;
                auction.Tick(1000);
                auction.OnBidPlaced -= Record;
                Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
                Assert.That(bids, Is.Not.Empty);
                return bids.Concat(auction.Results.Select(result => $"{result.itemId}:{result.winnerId}:{result.finalPrice}")).ToArray();
            }

            Assert.That(Run(false), Is.EqualTo(Run(true)));
        }

        private static void Emit(AuctionManager auction, string bidder, string category, string line) =>
            typeof(AuctionManager).GetMethod("PublishAiDialogue", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(auction, new object[] { bidder, category, line });
    }
}
