using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class PreparationPhaseTests
    {
        private const string ScenePath = "Assets/Scenes/AuctionDemo.unity";
        private const string LevelPath = "Assets/AuctionMvp/MvpAuctionLevel.asset";

        [Test]
        public void EditionKeepsApprovedHeadlinesAndTypeOrder()
        {
            var level = AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>(LevelPath);
            var expected = new[]
            {
                ("art_venus", "Venuša bez rúk sa vynorila zo súkromnej zbierky.", "bieleho mramoru"),
                ("arch_chalice", "Zlatý kalich bez uší.", "tromi červenými kameňmi"),
                ("arch_sword", "Rímsky meč s rovnou čepeľou.", "rovnou čepeľou a ostrou špičkou"),
                ("art_vase", "Modrá amfora z potopenej lode.", "bielymi vlnami"),
                ("pop_cd", "Prvé vydanie bez škrabanca.", "bez jedinej praskliny"),
                ("silly_tears", "Slzy nepriateľov pod korkom.", "korkovou zátkou")
            };
            var entries = level.GetNewspaperEntries();
            Assert.That(entries.Select(entry => (entry.ItemType.TypeId, entry.Headline)),
                Is.EqualTo(expected.Select(article => (article.Item1, article.Item2))));
            for (int index = 0; index < entries.Count; index++)
                Assert.That(entries[index].BodyText, Does.Contain(expected[index].Item3));
        }

        [Test]
        public void EditionUsesAuthoredEntriesAndSkipsInvalidRows()
        {
            var level = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>(LevelPath));
            try
            {
                var data = new SerializedObject(level);
                var variants = data.FindProperty("variants");
                var type = level.Variants[0].ItemType;
                variants.GetArrayElementAtIndex(1).objectReferenceValue =
                    level.Database.GetVariantsByType(type).Single(item => item != level.Variants[0]);
                var entries = data.FindProperty("newspaperEntries");
                entries.arraySize = 0;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(level.GetNewspaperEntries(), Is.Empty);

                entries.arraySize = 4;
                SetEntry(entries, 0, type, "First", "Body");
                SetEntry(entries, 1, null, "No type", "Body");
                SetEntry(entries, 2, type, "No body", " ");
                SetEntry(entries, 3, level.Variants[2].ItemType, "Second", "Body");
                data.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(level.CreateRun().Count(lot => lot.ItemType == type), Is.EqualTo(2));
                Assert.That(level.GetNewspaperEntries().Select(entry => entry.Headline),
                    Is.EqualTo(new[] { "First", "Second" }));
                entries.arraySize = 1;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(level.GetNewspaperEntries().Single().Headline, Is.EqualTo("First"));
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        [UnityTest]
        public IEnumerator PreparationRequiresForwardNewsBeforeAuctionAndLeavesBiddingUncovered()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var auction = Object.FindFirstObjectByType<AuctionManager>();
            var preparation = Object.FindFirstObjectByType<PreparationPhase>();
            var level = AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>(LevelPath);
            var newspaper = preparation.GetComponentInChildren<NewspaperView>(true);
            var read = GameObject.Find("Read NEWSSSS").GetComponent<Button>();
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            Assert.That(Text("Starting budget"), Does.Contain($"${level.StartingBudget:N0}"));
            Assert.That(Text("Profit target"), Does.Contain($"${level.ProfitTarget:N0}"));
            Assert.That(GameObject.Find("Start Auction"), Is.Null);
            preparation.RequestStart();
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));

            PointerClick(read);
            Assert.That(newspaper.IsOpen, Is.True);
            Assert.That(Text("Masthead"), Is.EqualTo("NEWSSSS"));
            Assert.That(Text("Subtitle"), Is.EqualTo("WORLD SPECIAL NEWS"));
            Assert.That(GameObject.Find("Reporter"), Is.Not.Null);
            VerifyNewsLayout(newspaper);
            var next = GameObject.Find("Continue News").GetComponent<Button>();
            Assert.That(newspaper.GetComponentsInChildren<Button>(), Is.EqualTo(new[] { next }));
            Assert.That(newspaper.GetComponentsInChildren<TMP_Text>().Select(label => label.name),
                Is.EquivalentTo(new[] { "Masthead", "Subtitle", "Reporter placeholder", "Headline", "Body", "Label" }));
            Assert.That(next.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("tap to continue"));
            auction.Tick(1000);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Idle));
            Assert.That(auction.Opponents, Is.Empty);
            Assert.That(auction.CurrentItem, Is.Null);

            foreach (NewspaperEntry entry in level.GetNewspaperEntries())
            {
                Assert.That(Text("Headline"), Is.EqualTo(entry.Headline));
                Assert.That(Text("Body"), Is.EqualTo(entry.BodyText));
                Assert.That(GameObject.Find("Start Auction"), Is.Null);
                PointerClick(next);
            }

            Assert.That(newspaper.IsOpen, Is.False);
            var start = GameObject.Find("Start Auction").GetComponent<Button>();
            read.onClick.Invoke();
            next.onClick.Invoke();
            Assert.That(newspaper.IsOpen, Is.False);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(start.gameObject));
            var starts = new List<AuctionItem>();
            auction.OnItemChanged += starts.Add;
            PointerClick(start);
            start.onClick.Invoke();
            Assert.That(starts, Is.EqualTo(new[] { level.Variants[0] }));
            Assert.That(auction.State, Is.EqualTo(AuctionState.ItemReveal));

            auction.Tick(AuctionManager.RevealDuration);
            var bid = GameObject.Find("AuctionPanel").GetComponentsInChildren<Button>()
                .FirstOrDefault(button => button.IsInteractable());
            Assert.That(bid, Is.Not.Null);
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)bid.transform;
            Vector2 pointer = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Assert.That(bid.targetGraphic.Raycast(pointer, null), Is.True);
            Assert.That(bid.GetComponentInParent<CanvasGroup>().blocksRaycasts, Is.True);
            Assert.That(newspaper.GetComponentInParent<GraphicRaycaster>(true).isActiveAndEnabled, Is.False);

            while (auction.State == AuctionState.Bidding && auction.CurrentItem == starts[0] &&
                   auction.RemainingTime > AuctionManager.AntiSnipingTime)
                auction.Tick(0.125f);
            Assert.That(auction.State, Is.EqualTo(AuctionState.Bidding));
            Assert.That(auction.CurrentItem, Is.SameAs(level.Variants[0]));
            Assert.That(auction.RemainingTime, Is.InRange(0.01f, AuctionManager.AntiSnipingTime));
            Assert.That(auction.BidHistory.Any(record => record.bidderId != BidderIds.Player), Is.True);
            read.onClick.Invoke();
            next.onClick.Invoke();
            Assert.That(newspaper.IsOpen, Is.False);
            Assert.That(GameObject.Find("Auction Newspaper"), Is.Null);
            Assert.That(GameObject.Find("Preparation Canvas"), Is.Null);
            auction.Tick(AuctionManager.MaximumBiddingDuration);
            Assert.That(auction.Results, Is.Not.Empty);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator NewsImagesUseCustomColorsStableSilhouettesAndMissingPlaceholders()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var preparation = Object.FindFirstObjectByType<PreparationPhase>();
            var level = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>(LevelPath));
            var database = Object.Instantiate(level.Database);
            var fallbackType = level.Variants[0].ItemType;
            var customType = Object.Instantiate(level.Variants[1].ItemType);
            var missingType = level.Variants[2].ItemType;
            var customVariant = Object.Instantiate(level.Variants[1]);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { Color.clear, Color.red, Color.blue, Color.green });
            texture.Apply();
            var customSprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one / 2);

            try
            {
                SetReference(customType, "newspaperSprite", customSprite);
                SetReference(customVariant, "itemType", customType);
                var fallbackVariants = level.Database.GetVariantsByType(fallbackType);
                var catalog = new SerializedObject(database);
                var items = catalog.FindProperty("items");
                items.arraySize = 3;
                items.GetArrayElementAtIndex(0).objectReferenceValue = fallbackVariants[0];
                items.GetArrayElementAtIndex(1).objectReferenceValue = fallbackVariants[1];
                items.GetArrayElementAtIndex(2).objectReferenceValue = customVariant;
                catalog.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(customVariant.Sprite, Is.Not.Null);
                Assert.That(database.GetVariantsByType(missingType), Is.Empty);

                SetReference(level, "itemDatabase", database);
                var data = new SerializedObject(level);
                var entries = data.FindProperty("newspaperEntries");
                entries.arraySize = 4;
                SetEntry(entries, 0, fallbackType, "Silhouette", "Body");
                SetEntry(entries, 1, customType, "Custom", string.Concat(Enumerable.Repeat("Long article text. ", 100)));
                SetEntry(entries, 2, missingType, "Missing image", "Body");
                SetEntry(entries, 3, fallbackType, "Silhouette again", "Body");
                data.ApplyModifiedPropertiesWithoutUndo();
                SetReference(preparation, "level", level);

                Random.State randomState = Random.state;
                LogAssert.Expect(LogType.Warning, $"No NEWSSSS image for item type '{missingType.TypeId}'.");
                PointerClick(GameObject.Find("Read NEWSSSS").GetComponent<Button>());
                Assert.That(Random.state, Is.EqualTo(randomState));
                var next = GameObject.Find("Continue News").GetComponent<Button>();
                var image = GameObject.Find("ItemType image").GetComponent<Image>();
                var fallbackSprite = image.sprite;
                Assert.That(fallbackVariants.Select(item => item.Sprite), Does.Contain(fallbackSprite));
                Assert.That(image.color, Is.EqualTo(Color.black));
                Assert.That(image.material, Is.SameAs(image.defaultMaterial));
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(fallbackSprite));
                Assert.That(importer.DoesSourceTextureHaveAlpha(), Is.True);

                PointerClick(next);
                Assert.That(image.sprite, Is.SameAs(customSprite));
                Assert.That(image.color, Is.EqualTo(Color.white));
                Assert.That(image.sprite.texture.GetPixel(0, 0).a, Is.Zero);
                Assert.That(image.sprite.texture.GetPixel(1, 0), Is.EqualTo(Color.red));
                var scroll = GameObject.Find("Article").GetComponent<ScrollRect>();
                Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
                Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
                Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(0.001f));
                scroll.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = Vector2.down * 10 });
                Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(1));

                PointerClick(next);
                Assert.That(image.enabled, Is.False);
                Assert.That(GameObject.Find("Image placeholder"), Is.Not.Null);
                PointerClick(next);
                Assert.That(image.sprite, Is.SameAs(fallbackSprite));
                Assert.That(image.color, Is.EqualTo(Color.black));
                PointerClick(next);
                Assert.That(GameObject.Find("Start Auction"), Is.Not.Null);
            }
            finally
            {
                foreach (Object instance in new Object[] { level, database, customType, customVariant, customSprite, texture })
                    Object.Destroy(instance);
            }

            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator EmptyEditionStillUnlocksStart()
        {
            yield return new EnterPlayMode();
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var preparation = Object.FindFirstObjectByType<PreparationPhase>();
            var level = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>(LevelPath));
            try
            {
                var data = new SerializedObject(level);
                data.FindProperty("newspaperEntries").arraySize = 0;
                data.ApplyModifiedPropertiesWithoutUndo();
                SetReference(preparation, "level", level);
                LogAssert.Expect(LogType.Warning, "The level has no valid NEWSSSS articles.");
                PointerClick(GameObject.Find("Read NEWSSSS").GetComponent<Button>());
                Assert.That(GameObject.Find("Continue News"), Is.Null);
                Assert.That(GameObject.Find("Start Auction"), Is.Not.Null);
                Assert.That(Object.FindFirstObjectByType<AuctionManager>().State, Is.EqualTo(AuctionState.Idle));
            }
            finally
            {
                Object.Destroy(level);
            }

            yield return new ExitPlayMode();
        }

        private static void SetEntry(SerializedProperty entries, int index, AuctionItemType type, string headline,
            string body)
        {
            var entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("itemType").objectReferenceValue = type;
            entry.FindPropertyRelative("headline").stringValue = headline;
            entry.FindPropertyRelative("bodyText").stringValue = body;
        }

        private static void SetReference(Object target, string field, Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string Text(string name) => GameObject.Find(name).GetComponent<TMP_Text>().text;

        private static void VerifyNewsLayout(NewspaperView newspaper)
        {
            var overlay = (RectTransform)newspaper.transform;
            var page = (RectTransform)overlay.Find("News page");
            var reporter = (RectTransform)page.Find("Reporter");
            var article = (RectTransform)page.Find("Article");
            var prompt = (RectTransform)page.Find("Continue News");
            Assert.That(reporter.offsetMin.y, Is.GreaterThan(article.offsetMax.y));
            Assert.That(article.offsetMin.y, Is.GreaterThan(prompt.offsetMax.y));

            overlay.anchorMin = overlay.anchorMax = Vector2.one / 2;
            foreach (Vector2 size in new[] { new Vector2(360, 740), new Vector2(1280, 720) })
            {
                overlay.sizeDelta = size;
                Canvas.ForceUpdateCanvases();
                Assert.That(page.localScale.x, Is.GreaterThan(0));
                Assert.That(page.localScale.y, Is.EqualTo(page.localScale.x));
                Assert.That(page.rect.width * page.localScale.x, Is.LessThan(size.x));
                Assert.That(page.rect.height * page.localScale.y, Is.LessThan(size.y));
            }

            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        }

        private static void PointerClick(Button button) =>
            ExecuteEvents.Execute(button.gameObject,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);
    }
}
