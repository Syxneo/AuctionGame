using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AuctionGame.Auction.Tests
{
    public sealed class AuctionJuiceTests
    {
        [UnityTest]
        public IEnumerator IntegratedAuctionPresentation()
        {
            yield return new EnterPlayMode();
            GameIntroTestDriver.LoadIntegratedScene();
            yield return null;

            var auction = Object.FindFirstObjectByType<AuctionManager>();
            var item = Object.FindFirstObjectByType<AuctionItemSpritePresenter>();
            var dialogue = Object.FindFirstObjectByType<BidderDialoguePresenter>();
            var sale = Object.FindFirstObjectByType<SaleAuctioneer>();
            GameObject.Find("Game Intro Canvas").SetActive(false);
            Set(auction, "enableAutomaticAi", false);
            Set(auction, "paceAiInteractions", false);
            Set(item, "shimmerInterval", new Vector2(0.65f, 0.65f));
            Set(dialogue, "hopHeight", 0f);
            Set(dialogue, "enableDialogueUi", false);

            float previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            try
            {
                Assert.That(item.transform.Find("Podium Idle Sparkle"), Is.Null);
                Object.FindFirstObjectByType<SceneIntroOutro>().SkipToEnd();
                var level = AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>("Assets/AuctionMvp/MvpAuctionLevel.asset");
                Assert.That(auction.StartAuction(level.CreateRun(), level.Database), Is.True);
                Assert.That(item.transform.Find("Podium Idle Sparkle"), Is.Null);
                auction.Tick(AuctionManager.RevealDuration);
                Assert.That(auction.State, Is.EqualTo(AuctionState.Bidding));
                yield return null;

                // A physical press preserves edge hit testing and only the original Button clicks.
                Canvas.ForceUpdateCanvases();
                foreach (int amount in new[] { 10, 25, 50 })
                {
                    var button = GameObject.Find($"+${amount} Button").GetComponent<Button>();
                    var rect = (RectTransform)button.transform;
                    var savedScale = rect.localScale;
                    var edge = RectTransformUtility.WorldToScreenPoint(null,
                        rect.TransformPoint(new Vector2(rect.rect.xMax - 1f, rect.rect.center.y)));
                    var pointer = new PointerEventData(EventSystem.current)
                    {
                        button = PointerEventData.InputButton.Left, position = edge
                    };
                    int bids = auction.BidHistory.Count;
                    ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                    Assert.That(rect.localScale.x, Is.EqualTo(savedScale.x * 0.94f).Within(0.001f));
                    Assert.That(RectTransformUtility.RectangleContainsScreenPoint(rect, edge, null,
                        button.targetGraphic.raycastPadding), Is.True);
                    Assert.That(auction.BidHistory.Count, Is.EqualTo(bids));
                    ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                    if (amount == 50)
                        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                    Assert.That(auction.BidHistory.Count, Is.EqualTo(bids + (amount == 50 ? 1 : 0)));
                    yield return new WaitForSecondsRealtime(0.2f);
                    Assert.That(rect.localScale, Is.EqualTo(savedScale));
                }

                // Accepted bids affect the authoritative bidder and leave the remaining clock alone.
                var bidder = AuctionItemPresentationTests.FindBidderWithLimit(auction, 60);
                var target = BidderSprite(dialogue, bidder.Id);
                var other = BidderSprite(dialogue, auction.Opponents.First(x => x != bidder).Id);
                var restPosition = target.transform.localPosition;
                var otherPosition = other.transform.localPosition;
                float remaining = auction.RemainingTime;
                Assert.That(auction.TryPlaceBid(bidder.Id, 60), Is.True);
                ShowReactionAt(dialogue, bidder.Id, 0.15f);
                Assert.That(target.transform.localPosition.y, Is.GreaterThan(restPosition.y + 0.08f));
                Assert.That(other.transform.localPosition, Is.EqualTo(otherPosition));
                Assert.That(auction.RemainingTime, Is.EqualTo(remaining));
                Capture("Bidder emphasis");

                // Withdrawal uses the same appearance as other inactive bidders and reacts once.
                var withdrawing = auction.Opponents.First(x => x != bidder && x.IsActiveForLot);
                var withdrawnSprite = BidderSprite(dialogue, withdrawing.Id);
                var withdrawnRest = withdrawnSprite.transform.localPosition;
                var inactive = auction.Opponents.First(x => !x.IsActiveForLot);
                var inactiveSprite = BidderSprite(dialogue, inactive.Id);
                withdrawing.Withdraw();
                auction.NotifyChickenOut(withdrawing);
                ShowReactionAt(dialogue, withdrawing.Id, 0.15f);
                Assert.That(withdrawnSprite.transform.localPosition.y, Is.LessThan(withdrawnRest.y - 0.05f));
                Assert.That(withdrawnSprite.sharedMaterial, Is.EqualTo(inactiveSprite.sharedMaterial));
                Assert.That(withdrawnSprite.color, Is.EqualTo(inactiveSprite.color));
                ShowReactionAt(dialogue, withdrawing.Id, 0.31f);
                auction.NotifyChickenOut(withdrawing);
                RenderCharacters(dialogue);
                Assert.That(withdrawnSprite.transform.localPosition, Is.EqualTo(withdrawnRest));

                // The regular countdown label returns to its original look above the threshold.
                var bubble = (BidderSpeechBubble)sale.GetType().GetField("bubble", PrivateInstance).GetValue(sale);
                var label = bubble.Label;
                Color normalColor = label.color;
                Vector3 normalScale = label.rectTransform.localScale;
                auction.Tick(auction.RemainingTime - 2.5f);
                Set(sale, "started", Time.unscaledTimeAsDouble - 0.25);
                sale.GetType().GetMethod("LateUpdate", PrivateInstance).Invoke(sale, null);
                Assert.That(label.color, Is.EqualTo(normalColor));
                Assert.That(label.rectTransform.localScale.x, Is.GreaterThan(normalScale.x));
                Capture("Timer danger");
                typeof(AuctionManager).GetField("bidDeadline", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(auction, auction.BiddingElapsed + 5d);
                auction.Tick(0.01f);
                yield return null;
                Assert.That(label.color, Is.EqualTo(normalColor));
                Assert.That(label.rectTransform.localScale, Is.EqualTo(normalScale));

                // The pooled effect stays local to the displayed item and leaves Unity RNG alone.
                var rngState = Random.state;
                double spawnTime = (double)item.GetType().GetField("nextSparkle", PrivateInstance).GetValue(item);
                item.GetType().GetMethod("UpdateShimmer", PrivateInstance).Invoke(item, new object[] { spawnTime });
                var sparkle = item.transform.Find("Podium Idle Sparkle").GetComponent<SpriteRenderer>();
                Assert.That(sparkle.enabled, Is.True);
                Assert.That(Random.state, Is.EqualTo(rngState));
                auction.Tick(auction.RemainingTime + 0.01f);
                Assert.That(sparkle.enabled, Is.False);
            }
            finally
            {
                Time.timeScale = previousTimeScale;
            }
            yield return new ExitPlayMode();
        }

        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void ShowReactionAt(BidderDialoguePresenter presenter, string id, float elapsed)
        {
            var bindings = (System.Array)presenter.GetType().GetField("bidders", PrivateInstance).GetValue(presenter);
            object binding = bindings.Cast<object>().Single(entry =>
                (string)entry.GetType().GetField("bidderId").GetValue(entry) == id);
            var start = binding.GetType().GetProperty("ReactionStarted");
            Assert.That((double)start.GetValue(binding), Is.Not.EqualTo(double.NegativeInfinity));
            start.SetValue(binding, Time.unscaledTimeAsDouble - elapsed);
            RenderCharacters(presenter);
        }

        private static void RenderCharacters(BidderDialoguePresenter presenter) =>
            presenter.GetType().GetMethod("UpdateCharacters", PrivateInstance)
                .Invoke(presenter, new object[] { Time.timeAsDouble });

        private static void Set(Object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static SpriteRenderer BidderSprite(BidderDialoguePresenter presenter, string id)
        {
            var bidders = new SerializedObject(presenter).FindProperty("bidders");
            for (int i = 0; i < bidders.arraySize; i++)
            {
                var bidder = bidders.GetArrayElementAtIndex(i);
                if (bidder.FindPropertyRelative("bidderId").stringValue == id)
                    return (SpriteRenderer)bidder.FindPropertyRelative("sprite").objectReferenceValue;
            }
            throw new System.ArgumentException(id);
        }

        private static void Capture(string caption)
        {
            string directory = System.Environment.GetEnvironmentVariable("AUCTION_JUICE_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory))
                return;

            Camera camera = Camera.main;
            var render = RenderTexture.GetTemporary(720, 1280, 24);
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            var overlays = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(canvas => canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var texture = new Texture2D(720, 1280, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = render;
                foreach (Canvas canvas in overlays)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = camera.nearClipPlane + 0.1f;
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = render;
                texture.ReadPixels(new Rect(0, 0, 720, 1280), 0, 0);
                File.WriteAllBytes(Path.Combine(directory, caption + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                foreach (Canvas canvas in overlays)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.worldCamera = null;
                }
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(render);
                Object.Destroy(texture);
            }
        }
    }
}
