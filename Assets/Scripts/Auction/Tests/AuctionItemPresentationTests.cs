using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AuctionGame.Auction.Tests
{
    public sealed class AuctionItemPresentationTests
    {
        [UnityTest]
        public IEnumerator IntegratedAuctionShowsEachLotOnThePodium()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/AuctionIntegrated.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var auction = Object.FindFirstObjectByType<AuctionManager>();
            var camera = Camera.main;
            var holder = GameObject.Find("Item Holder").GetComponent<MeshRenderer>();
            var visual = GameObject.Find("AuctionItemVisual").GetComponent<SpriteRenderer>();
            var hudImage = GameObject.Find("ItemImage").GetComponent<Image>();
            var level = AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>("Assets/AuctionMvp/MvpAuctionLevel.asset");
            var presenter = Object.FindFirstObjectByType<BidderDialoguePresenter>();
            var presenterData = new SerializedObject(presenter);
            Assert.That(presenterData.FindProperty("auction").objectReferenceValue, Is.SameAs(auction));
            var dialogueCanvas = (Canvas)presenterData.FindProperty("canvas").objectReferenceValue;
            Assert.That(dialogueCanvas.targetDisplay, Is.Zero);
            Assert.That(hudImage.GetComponentInParent<Canvas>(true).targetDisplay, Is.Zero);
            var bindings = presenterData.FindProperty("bidders");
            Assert.That(bindings.arraySize, Is.EqualTo(5));
            var secondaryWasActive = Display.displays.Length > 1 && Display.displays[1].active;
            var managerData = new SerializedObject(auction);
            managerData.FindProperty("enableAutomaticAi").boolValue = false;
            managerData.ApplyModifiedPropertiesWithoutUndo();
            Object.FindFirstObjectByType<SceneIntroOutro>().SkipToEnd();

            Assert.That(camera.targetDisplay, Is.Zero);
            Assert.That(visual.enabled, Is.False);
            Assert.That(hudImage.enabled, Is.False);
            Assert.That(visual.GetComponentInParent<Canvas>(), Is.Null);

            Click("Read NEWSSSS");
            foreach (var entry in level.GetNewspaperEntries())
                Click("Continue News");
            Click("Start Auction");

            var lots = level.CreateRun();
            Assert.That(auction.Opponents.Select(bidder => bidder.Personality.Name),
                Is.EqualTo(new[] { "Helena", "Ferko", "Viktor", "Zuzu", "Grumblewick" }));
            Assert.That(auction.Opponents.Select(bidder => bidder.StartingBudget), Is.All.EqualTo(600));
            foreach (AuctionItem lot in lots)
            {
                Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
                Assert.That(auction.CurrentItem, Is.SameAs(lot));
                Assert.That(visual.sprite, Is.SameAs(lot.Sprite));
                Assert.That(visual.enabled, Is.True);
                Assert.That(hudImage.enabled, Is.False);
                Assert.That(visual.bounds.min.y - holder.bounds.max.y, Is.EqualTo(0.03f).Within(0.001f));
                Assert.That(visual.bounds.center.x, Is.EqualTo(holder.bounds.center.x).Within(0.001f));
                Assert.That(visual.bounds.center.z, Is.EqualTo(holder.bounds.center.z).Within(0.001f));
                Assert.That(visual.transform.localScale.x, Is.EqualTo(visual.transform.localScale.y));
                Vector3 toCamera = camera.transform.position - visual.bounds.center;
                toCamera.y = 0f;
                Assert.That(Vector3.Dot(-visual.transform.forward, toCamera.normalized), Is.GreaterThan(0.999f));
                AssertInPortrait(camera, visual);
                CheckBidderOpacity(auction, bindings);

                auction.Tick(AuctionManager.RevealDuration);
                if (lot == lots[0])
                {
                    yield return null;
                    Assert.That(dialogueCanvas.GetComponentsInChildren<BidderSpeechBubble>(), Has.Length.EqualTo(3));
                    Click("+$10 Button");
                    Assert.That(auction.CurrentPrice, Is.EqualTo(10));
                    var opponent = auction.Opponents.First(bidder => bidder.IsActiveForLot && bidder.EstimatedValue >= 115);
                    Assert.That(auction.TryPlaceBid(opponent.Id, 20), Is.True);
                    Click("+$25 Button");
                    Assert.That(auction.CurrentPrice, Is.EqualTo(45));
                    Assert.That(auction.TryPlaceBid(opponent.Id, 55), Is.True);
                    Click("+$50 Button");
                    Assert.That(auction.CurrentPrice, Is.EqualTo(105));
                    Assert.That(auction.HighestBidderId, Is.EqualTo(BidderIds.Player));
                    Assert.That(auction.TryPlaceBid(opponent.Id, 115), Is.True);
                    var withdrawing = auction.Opponents.First(bidder => bidder.IsActiveForLot && bidder != opponent);
                    typeof(AIBidder).GetProperty(nameof(AIBidder.HasChickenedOut)).SetValue(withdrawing, true);
                    typeof(AuctionManager).GetMethod("NotifyChickenOut", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(auction, new object[] { withdrawing });
                    CheckBidderOpacity(auction, bindings);
                }

                auction.Tick(auction.RemainingTime + 2.01f);
            }

            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(visual.enabled, Is.False);
            Assert.That(visual.sprite, Is.Null);
            Assert.That(auction.Results, Has.Count.EqualTo(6));
            Assert.That(GameObject.Find("SALE Report"), Is.Not.Null);
            Click("Retry Button");
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            Click("Read NEWSSSS");
            foreach (var entry in level.GetNewspaperEntries())
                Click("Continue News");
            Click("Start Auction");
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
            Assert.That(auction.Opponents.Select(bidder => bidder.AvailableBudget), Is.All.EqualTo(600));
            CheckBidderOpacity(auction, bindings);
            Assert.That(Display.displays.Length > 1 && Display.displays[1].active, Is.EqualTo(secondaryWasActive));
            yield return new ExitPlayMode();
        }

        private static void CheckBidderOpacity(AuctionManager auction, SerializedProperty bindings)
        {
            Assert.That(auction.Opponents.Count(bidder => bidder.IsActiveForLot), Is.EqualTo(3));
            for (int index = 0; index < bindings.arraySize; index++)
            {
                var binding = bindings.GetArrayElementAtIndex(index);
                var bidder = auction.Opponents[index];
                Assert.That(binding.FindPropertyRelative("bidderId").stringValue, Is.EqualTo(bidder.Id));
                var sprite = (SpriteRenderer)binding.FindPropertyRelative("sprite").objectReferenceValue;
                Assert.That(sprite && sprite.sprite && sprite.enabled && sprite.gameObject.activeInHierarchy, Is.True);
                var camera = Camera.main;
                camera.aspect = 9f / 16f;
                Vector3 head = camera.WorldToViewportPoint(new Vector3(sprite.bounds.center.x, sprite.bounds.max.y,
                    sprite.bounds.center.z));
                camera.ResetAspect();
                Assert.That(head.z, Is.GreaterThan(camera.nearClipPlane), bidder.Id);
                Assert.That(head.x, Is.InRange(0f, 1f), bidder.Id);
                Assert.That(head.y, Is.InRange(0f, 1f), bidder.Id);
                Assert.That(sprite.color.a, Is.EqualTo(bidder.IsActiveForLot && !bidder.HasChickenedOut ? 1f : 0.4f)
                    .Within(0.001f));
            }
        }

        private static void AssertInPortrait(Camera camera, SpriteRenderer visual)
        {
            Bounds bounds = visual.sprite.bounds;
            foreach (float aspect in new[] { 9f / 16f, 9f / 20f })
            {
                camera.aspect = aspect;
                foreach (float x in new[] { bounds.min.x, bounds.max.x })
                foreach (float y in new[] { bounds.min.y, bounds.max.y })
                {
                    Vector3 point = camera.WorldToViewportPoint(
                        visual.transform.TransformPoint(x, y, bounds.center.z));
                    Assert.That(point.z, Is.GreaterThan(camera.nearClipPlane));
                    Assert.That(point.x, Is.InRange(0f, 1f));
                    Assert.That(point.y, Is.InRange(0f, 1f));
                }
            }
            camera.ResetAspect();
        }

        private static void Click(string name)
        {
            var button = GameObject.Find(name)?.GetComponent<Button>();
            Assert.That(button != null && button.interactable, Is.True, name);
            button.onClick.Invoke();
        }
    }
}
