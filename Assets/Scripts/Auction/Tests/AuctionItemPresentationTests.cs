using System.Collections;
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
    public sealed class AuctionItemPresentationTests
    {
        [UnityTest]
        public IEnumerator IntegratedAuctionShowsEachLotOnThePodium()
        {
            yield return new EnterPlayMode();
            GameIntroTestDriver.LoadIntegratedScene();
            yield return null;

            var auction = Object.FindFirstObjectByType<AuctionManager>();
            VerifyAuctionAudio(auction);
            var camera = Camera.main;
            var holder = GameObject.Find("Item Holder").GetComponent<MeshRenderer>();
            var visual = GameObject.Find("AuctionItemVisual").GetComponent<SpriteRenderer>();
            var sale = GameObject.Find("SALE_Character").GetComponent<SaleAuctioneer>();
            var saleSprite = sale.GetComponent<SpriteRenderer>();
            Vector3 saleRestPosition = sale.transform.localPosition;
            Quaternion saleRestRotation = sale.transform.localRotation;
            Vector3 saleRestScale = sale.transform.localScale;
            var hudImage = GameObject.Find("ItemImage").GetComponent<Image>();
            var hudGroup = GameObject.Find("AuctionPanel").GetComponent<CanvasGroup>();
            var level = AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>("Assets/AuctionMvp/MvpAuctionLevel.asset");
            var managerData = new SerializedObject(auction);
            managerData.FindProperty("enableAutomaticAi").boolValue = false;
            managerData.FindProperty("paceAiInteractions").boolValue = false;
            managerData.ApplyModifiedPropertiesWithoutUndo();
            Object.FindFirstObjectByType<BidderDialoguePresenter>().enabled = false;
            var stage = Object.FindFirstObjectByType<SceneIntroOutro>();
            stage.SkipToEnd();
            var stageData = new SerializedObject(stage);
            var left = (Transform)stageData.FindProperty("leftCurtain").objectReferenceValue;
            var right = (Transform)stageData.FindProperty("rightCurtain").objectReferenceValue;
            var basePosition = camera.transform.localPosition;
            var baseRotation = camera.transform.localRotation;
            float baseFov = camera.fieldOfView;
            var leftOpen = left.localPosition;
            var rightOpen = right.localPosition;
            float closedX = stageData.FindProperty("curtainClosedX").floatValue;

            Assert.That(camera.targetDisplay, Is.Zero);
            Assert.That(visual.enabled, Is.False);
            Assert.That(saleSprite.enabled, Is.False);
            Assert.That(saleSprite.sprite.name, Is.EqualTo("SALE_Idle"));
            Assert.That(hudImage.enabled, Is.False);
            Assert.That(visual.GetComponentInParent<Canvas>(), Is.Null);

            yield return GameIntroTestDriver.OpenNewsFromIntro();
            yield return GameIntroTestDriver.ReadBroadcast();

            var lots = level.CreateRun();
            for (int lotIndex = 0; lotIndex < lots.Count; lotIndex++)
            {
                AuctionItem lot = lots[lotIndex];
                Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));
                Assert.That(saleSprite.enabled, Is.True);
                Assert.That(auction.CurrentItem, Is.SameAs(lot));
                Assert.That(visual.sprite, Is.SameAs(lot.Sprite));
                Assert.That(visual.enabled, Is.True);
                Assert.That(hudImage.enabled, Is.False);
                var vertices = GetWorldVertices(visual);
                Assert.That(vertices.Min(vertex => vertex.y) - holder.bounds.max.y,
                    Is.EqualTo(0.03f).Within(0.001f));
                Assert.That((vertices.Min(vertex => vertex.x) + vertices.Max(vertex => vertex.x)) * 0.5f,
                    Is.EqualTo(holder.bounds.center.x).Within(0.001f));
                Assert.That((vertices.Min(vertex => vertex.z) + vertices.Max(vertex => vertex.z)) * 0.5f,
                    Is.EqualTo(holder.bounds.center.z).Within(0.001f));
                Assert.That(visual.transform.localScale.x, Is.EqualTo(visual.transform.localScale.y));
                Vector3 toCamera = camera.transform.position - visual.transform.parent.position;
                toCamera.y = 0f;
                Assert.That(Vector3.Dot(-visual.transform.forward, toCamera.normalized), Is.GreaterThan(0.999f));
                AssertInPortrait(camera, vertices);
                AssertSalePlacement(camera, saleSprite, visual);

                int openingCount = auction.Opponents.Count(opponent => opponent.IsActiveForLot);
                if (lotIndex == 0)
                {
                    yield return null;
                    var bubble = GameObject.Find("SALE Bubble").GetComponent<BidderSpeechBubble>();
                    Assert.That(bubble.GetComponentInChildren<TMP_Text>().text, Does.Contain(lot.ItemName));
                    Assert.That(bubble.Group.blocksRaycasts, Is.False);
                    Assert.That(bubble.GetComponentsInChildren<Graphic>().Any(graphic => graphic.raycastTarget), Is.False);
                }
                auction.Tick(Mathf.Max(AuctionManager.RevealDuration, (openingCount - 1) * auction.AiInteractionInterval));
                Assert.That(saleSprite.enabled, Is.True);
                if (lotIndex == 0)
                {
                    yield return null;
                    var bubble = GameObject.Find("SALE Bubble");
                    Assert.That(bubble.GetComponentInChildren<TMP_Text>().text, Does.Contain("Ponuky, prosím!"));
                    yield return new WaitForSecondsRealtime(2.85f);
                }
                bool sold = lot != lots[1];
                if (lotIndex == 2)
                    Assert.That(auction.TryPlaceBid(FindBidderWithLimit(auction, 10).Id, 10), Is.True);
                else if (sold)
                    Click("+$10 Button");
                Assert.That(auction.CurrentPrice, Is.EqualTo(sold ? 10 : 0));
                // The leading bidder cannot raise their own bid.
                if (lot == lots[0])
                {
                    string opponent = FindBidderWithLimit(auction, 55).Id;
                    auction.Tick(2.5f);
                    Assert.That(auction.TryPlaceBid(opponent, 20), Is.True);
                    Click("+$25 Button");
                    Assert.That(auction.CurrentPrice, Is.EqualTo(45));
                    yield return null;
                    var saleBidBubble = GameObject.Find("SALE Bubble");
                    Assert.That(saleBidBubble.GetComponentInChildren<TMP_Text>().text, Does.Contain("Odvážna ponuka"));
                    Assert.That(auction.TryPlaceBid(BidderIds.Player, 45), Is.False);
                    auction.Tick(2.5f);
                    Assert.That(auction.TryPlaceBid(opponent, 55), Is.True);
                    Click("+$50 Button");
                    Assert.That(saleBidBubble.GetComponentInChildren<TMP_Text>().text, Does.Contain("Odvážna ponuka"));
                    Assert.That(auction.CurrentPrice, Is.EqualTo(105));
                    Assert.That(auction.HighestBidderId, Is.EqualTo(BidderIds.Player));
                    AssertWithdrawalPreservesSprite(auction);
                }

                auction.Tick(auction.RemainingTime + 1000f);
                StartWaitingCinematic(auction);
                Assert.That(auction.State, Is.EqualTo(sold ? AuctionState.Sold : AuctionState.Unsold));
                Assert.That(auction.CurrentItem, Is.SameAs(lot));
                Assert.That(auction.RemainingTime, Is.Zero);
                Assert.That(auction.TryPlaceBid(auction.Opponents[0].Id, 10), Is.False);
                Assert.That(hudGroup.alpha, Is.Zero);
                Assert.That(hudGroup.interactable, Is.False);
                Assert.That(hudGroup.blocksRaycasts, Is.False);
                Assert.That(GameObject.Find("SALE Bubble"), Is.Null);
                Assert.That(sale.ResultLine, Is.Not.Empty.And.Not.Null);
                if (lotIndex == 2)
                    Assert.That(sale.ResultLine, Is.EqualTo(auction.SaleLineForAiWin));
                var result = auction.Results.Last();
                int purchases = auction.Economy.Purchases.Count;
                int budget = auction.Economy.AvailableBudget;

                AdvanceMotion(stage, 0.7f);
                var podiumPosition = visual.transform.parent.position;
                var closePosition = podiumPosition + stageData.FindProperty("closeUpOffset").vector3Value;
                Assert.That(Vector3.Distance(camera.transform.position, closePosition), Is.LessThan(0.001f));
                Assert.That(camera.fieldOfView, Is.EqualTo(stageData.FindProperty("closeUpFieldOfView").floatValue));
                AssertInPortrait(camera, GetWorldVertices(visual), 0.38f, 0.89f);

                AdvanceMotion(stage, 0.71f);
                Assert.That(left.localPosition.x, Is.EqualTo(closedX));
                Assert.That(right.localPosition.x, Is.EqualTo(-closedX));
                var canvas = GameObject.Find("Auction Result Overlay").GetComponent<Canvas>();
                Assert.That(canvas.enabled, Is.True);
                Assert.That(canvas.GetComponentsInChildren<Graphic>()
                    .Where(graphic => graphic.raycastTarget).Select(graphic => graphic.name),
                    Is.EqualTo(new[] { "Continue Result" }));
                Assert.That(GameObject.Find("Result Item Sprite").GetComponent<Image>().sprite, Is.SameAs(lot.Sprite));
                Assert.That(visual.sprite, Is.SameAs(lot.Sprite));
                Assert.That(GameObject.Find("Result Outcome").GetComponent<TMP_Text>().text,
                    Is.EqualTo(sold ? "PREDANÉ" : "NEPREDANÉ"));
                Assert.That(GameObject.Find("SALE Commentary").GetComponent<TMP_Text>().text,
                    Is.EqualTo("SALE • " + sale.ResultLine));
                if (sold)
                {
                    Assert.That(GameObject.Find("Result Winner").GetComponent<TMP_Text>().text,
                        Is.EqualTo(auction.GetBidderDisplayName(result.winnerId)));
                    Assert.That(GameObject.Find("Result Price").GetComponent<TMP_Text>().text,
                        Is.EqualTo($"${result.finalPrice}"));
                }
                else
                {
                    Assert.That(GameObject.Find("Result Winner"), Is.Null);
                    Assert.That(GameObject.Find("Result Price"), Is.Null);
                }

                auction.Tick(1000f);
                Assert.That(auction.CurrentItem, Is.SameAs(lot));
                Assert.That(auction.GetComponent<AuctionReportUI>().Report, Is.Null);
                Assert.That(auction.Economy.Purchases.Count, Is.EqualTo(purchases));
                Assert.That(auction.Economy.AvailableBudget, Is.EqualTo(budget));

                AdvanceMotion(stage, 10f);
                Assert.That(canvas.enabled, Is.True);
                Assert.That(auction.CurrentItem, Is.SameAs(lot));
                Click("Continue Result");
                AdvanceMotion(stage, 0.5f);
                Assert.That(canvas.enabled, Is.False);
                Assert.That(hudGroup.alpha, Is.Zero);
                Assert.That(left.localPosition.x, Is.InRange(closedX + 0.01f, leftOpen.x - 0.01f));
                Assert.That(right.localPosition.x, Is.InRange(rightOpen.x + 0.01f, -closedX - 0.01f));
                Assert.That(camera.fieldOfView, Is.InRange(45f, 63f));
                // The next lot is swapped in the moment the curtain reopen (intro) starts,
                // not only after the whole result cinematic has finished.
                auction.Tick(0.01f);
                Assert.That(auction.CurrentItem, lotIndex + 1 < lots.Count ? Is.SameAs(lots[lotIndex + 1]) : Is.Null);
                if (lot == lots[1])
                    stage.enabled = false;
                else
                    AdvanceMotion(stage, 0.6f);
                Assert.That(camera.transform.localPosition, Is.EqualTo(basePosition));
                Assert.That(camera.transform.localRotation, Is.EqualTo(baseRotation));
                Assert.That(camera.fieldOfView, Is.EqualTo(baseFov));
                Assert.That(left.localPosition, Is.EqualTo(leftOpen));
                Assert.That(right.localPosition, Is.EqualTo(rightOpen));
                Assert.That(stage.IsPlaying, Is.False);
                Assert.That(hudGroup.alpha, Is.EqualTo(1f));
                Assert.That(hudGroup.interactable, Is.True);
                Assert.That(hudGroup.blocksRaycasts, Is.True);
                stage.enabled = true;
                Assert.That(auction.CurrentItem, lotIndex + 1 < lots.Count ? Is.SameAs(lots[lotIndex + 1]) : Is.Null);
            }

            Assert.That(auction.State, Is.EqualTo(AuctionState.Finished));
            Assert.That(visual.enabled, Is.False);
            Assert.That(saleSprite.enabled, Is.False);
            Assert.That(sale.CurrentPose, Is.EqualTo(SaleAuctioneer.Pose.Idle));
            Assert.That(sale.transform.localPosition, Is.EqualTo(saleRestPosition));
            Assert.That(sale.transform.localRotation, Is.EqualTo(saleRestRotation));
            Assert.That(sale.transform.localScale, Is.EqualTo(saleRestScale));
            Assert.That(visual.sprite, Is.Null);
            Assert.That(auction.Results, Has.Count.EqualTo(6));
            yield return GameIntroTestDriver.OpenFinalReport();
            Assert.That(auction.GetComponent<AuctionReportUI>().Report, Is.Not.Null);
            Assert.That(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            yield return new ExitPlayMode();
        }

        private static void AssertSalePlacement(Camera camera, SpriteRenderer sale, SpriteRenderer item)
        {
            try
            {
                var resolutions = new[] { new Vector2Int(720, 1280), new Vector2Int(1080, 1920),
                    new Vector2Int(720, 1600), new Vector2Int(1080, 2340) };
                foreach (Vector2Int resolution in resolutions)
                {
                    camera.aspect = resolution.x / (float)resolution.y;
                    Vector3[] salePoints = GetWorldVertices(sale).Select(camera.WorldToViewportPoint).ToArray();
                    Vector3[] itemPoints = GetWorldVertices(item).Select(camera.WorldToViewportPoint).ToArray();
                    Assert.That(salePoints.Min(point => point.x), Is.GreaterThan(itemPoints.Max(point => point.x)),
                        resolution.ToString());
                    Assert.That(salePoints.Max(point => point.x), Is.LessThan(1f), resolution.ToString());
                    Assert.That(salePoints.Min(point => point.y), Is.GreaterThan(0.43f), resolution.ToString());
                    Assert.That(salePoints.Max(point => point.y), Is.LessThan(0.72f), resolution.ToString());
                }
            }
            finally
            {
                camera.ResetAspect();
            }
        }

        private static void VerifyAuctionAudio(AuctionManager auction)
        {
            var ambience = Object.FindFirstObjectByType<AuctionHallAmbience>(FindObjectsInactive.Include);
            Assert.That(ambience, Is.Not.Null, "The auction ambience component must be in the scene.");
            var audio = ambience.gameObject;
            var ambienceData = new SerializedObject(ambience);
            Assert.That(ambienceData.FindProperty("auction").objectReferenceValue, Is.SameAs(auction));
            var tracks = ambienceData.FindProperty("tracks");
            Assert.That(tracks.arraySize, Is.EqualTo(2));
            var sources = Enumerable.Range(0, tracks.arraySize)
                .Select(index => (AudioSource)tracks.GetArrayElementAtIndex(index).objectReferenceValue).ToArray();
            Assert.That(sources.All(source => source && source.clip), Is.True, "Every ambient source must have a clip.");
            Assert.That(sources.Select(source => source.clip.name),
                Is.EquivalentTo(new[] { "auction_ambient", "auction_ambient_talking" }));
            Assert.That(sources.All(source => source.loop && !source.playOnAwake), Is.True);
            var click = new SerializedObject(audio.GetComponent<ButtonClickAudio>());
            var dialogue = new SerializedObject(Object.FindFirstObjectByType<BidderDialoguePresenter>());
            Assert.That(click.FindProperty("click").objectReferenceValue.name, Is.EqualTo("button_click"));
            Assert.That(dialogue.FindProperty("dialogueCue").objectReferenceValue, Is.Not.Null);
            Assert.That(dialogue.FindProperty("dialogueAudio").objectReferenceValue,
                Is.Not.SameAs(click.FindProperty("effects").objectReferenceValue));
        }

        private static void AssertWithdrawalPreservesSprite(AuctionManager auction)
        {
            var bidder = auction.Opponents.First(opponent => opponent.IsActiveForLot);
            var presenter = Object.FindFirstObjectByType<BidderDialoguePresenter>();
            var bindings = new SerializedObject(presenter).FindProperty("bidders");
            var binding = Enumerable.Range(0, bindings.arraySize).Select(bindings.GetArrayElementAtIndex).First(entry =>
                entry.FindPropertyRelative("bidderId").stringValue == bidder.Id);
            var sprite = (SpriteRenderer)binding.FindPropertyRelative("sprite").objectReferenceValue;
            var update = typeof(BidderDialoguePresenter).GetMethod("UpdateCharacters",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object[] time = { 0.75d };
            update.Invoke(presenter, time);
            var material = sprite.sharedMaterial;
            var color = sprite.color;
            var position = sprite.transform.localPosition;
            var rotation = sprite.transform.localRotation;
            typeof(AIBidder).GetProperty(nameof(AIBidder.HasChickenedOut)).SetValue(bidder, true);
            update.Invoke(presenter, time);
            Assert.That(sprite.sharedMaterial, Is.SameAs(material));
            Assert.That(sprite.color, Is.EqualTo(color));
            Assert.That(sprite.transform.localPosition, Is.EqualTo(position));
            Assert.That(sprite.transform.localRotation, Is.EqualTo(rotation));
        }

        internal static void AdvanceMotion(SceneIntroOutro stage, float seconds)
        {
            typeof(SceneIntroOutro).GetMethod("AdvanceResultMotion", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(stage, new object[] { seconds });
        }

        internal static void StartWaitingCinematic(AuctionManager auction) =>
            typeof(AuctionResultCinematic).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(auction.GetComponent<AuctionResultCinematic>(), null);

        internal static AIBidder FindBidderWithLimit(AuctionManager auction, int amount) =>
            auction.Opponents.First(bidder => auction.TryGetMaxBid(bidder.Id, out int maxBid) && maxBid >= amount);

        private static Vector3[] GetWorldVertices(SpriteRenderer visual) =>
            visual.sprite.vertices.Select(vertex => visual.transform.TransformPoint(vertex)).ToArray();

        private static void AssertInPortrait(Camera camera, Vector3[] vertices, float minY = 0f, float maxY = 1f)
        {
            foreach (float aspect in new[] { 9f / 16f, 9f / 20f })
            {
                camera.aspect = aspect;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = camera.WorldToViewportPoint(vertex);
                    Assert.That(point.z, Is.GreaterThan(camera.nearClipPlane));
                    Assert.That(point.x, Is.InRange(0f, 1f));
                    Assert.That(point.y, Is.InRange(minY, maxY));
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
