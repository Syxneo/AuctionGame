using System.Collections;
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

                auction.Tick(AuctionManager.RevealDuration);
                Click("+$10 Button");
                Assert.That(auction.CurrentPrice, Is.EqualTo(10));
                // The leading bidder cannot raise their own bid.
                if (lot == lots[0])
                {
                    Assert.That(auction.TryPlaceBid(auction.Opponents[0].Id, 20), Is.True);
                    Click("+$25 Button");
                    Assert.That(auction.CurrentPrice, Is.EqualTo(45));
                    Assert.That(auction.TryPlaceBid(auction.Opponents[0].Id, 55), Is.True);
                    Click("+$50 Button");
                    Assert.That(auction.CurrentPrice, Is.EqualTo(105));
                    Assert.That(auction.HighestBidderId, Is.EqualTo(BidderIds.Player));
                }

                auction.Tick(auction.RemainingTime + 2.01f);
            }

            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(visual.enabled, Is.False);
            Assert.That(visual.sprite, Is.Null);
            yield return new ExitPlayMode();
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
