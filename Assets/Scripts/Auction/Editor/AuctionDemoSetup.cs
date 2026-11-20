using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AuctionGame.Auction.Editor
{
    public static class AuctionDemoSetup
    {
        private const string ScenePath = "Assets/Scenes/AuctionDemo.unity";
        private const string Content = "Assets/AuctionDemo";
        private static readonly Color Background = new(0.055f, 0.08f, 0.11f);
        private static readonly Color Panel = new(0.10f, 0.15f, 0.20f);
        private static readonly Color Accent = new(0.93f, 0.74f, 0.40f);

        [MenuItem("Tools/Auction/Open Demo")]
        private static void OpenDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                EditorSceneManager.OpenScene(ScenePath);
            else
                CreateDemo();
        }

        public static void CreateDemo()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                return;

            if (!AssetDatabase.IsValidFolder(Content))
                AssetDatabase.CreateFolder("Assets", "AuctionDemo");

            TMP_FontAsset font = GetFont();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Auction Camera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.cullingMask = 0;
            var system = new GameObject("AuctionSystem", typeof(PlayerEconomy), typeof(AuctionManager),
                typeof(AuctionBootstrap), typeof(AuctionReportUI));
            var manager = system.GetComponent<AuctionManager>();
            var managerData = new SerializedObject(manager);
            managerData.FindProperty("itemDatabase").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ItemDatabase>("Assets/AuctionMvp/ItemDatabase.asset");
            var items = managerData.FindProperty("items");
            var samples = new (string id, string name, int price, int value, ItemCategory category)[]
            {
                ("royal-seal", "Royal Wax Seal", 90, 250, ItemCategory.Historical),
                ("moon-glass", "Moon Glass", 130, 215, ItemCategory.Rare),
                ("harbor-study", "Harbor Study", 75, 120, ItemCategory.Art),
                ("relay-prototype", "Relay Prototype", 180, 145, ItemCategory.Technology),
                ("tin-crown", "Tin Crown", 140, 20, ItemCategory.Misc),
                ("observatory-map", "Observatory Map", 110, 280, ItemCategory.Historical)
            };
            items.arraySize = samples.Length;
            for (int index = 0; index < samples.Length; index++)
            {
                var sample = samples[index];
                string path = $"{Content}/{sample.id}.asset";
                var item = AssetDatabase.LoadAssetAtPath<AuctionItem>(path);
                if (!item)
                {
                    item = ScriptableObject.CreateInstance<AuctionItem>();
                    var data = new SerializedObject(item);
                    data.FindProperty("itemId").stringValue = sample.id;
                    data.FindProperty("itemName").stringValue = sample.name;
                    data.FindProperty("startingPrice").intValue = sample.price;
                    data.FindProperty("trueValue").intValue = sample.value;
                    data.FindProperty("category").enumValueIndex = (int)sample.category;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.CreateAsset(item, path);
                }
                items.GetArrayElementAtIndex(index).objectReferenceValue = item;
            }
            managerData.ApplyModifiedPropertiesWithoutUndo();

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 600);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var backdrop = new GameObject("Background", typeof(Image)).GetComponent<Image>();
            backdrop.transform.SetParent(canvas.transform, false);
            backdrop.rectTransform.anchorMin = Vector2.zero;
            backdrop.rectTransform.anchorMax = Vector2.one;
            backdrop.rectTransform.sizeDelta = Vector2.zero;
            backdrop.color = Background;
            backdrop.raycastTarget = false;

            RectTransform panel = Box("AuctionPanel", canvas.transform, -480, 300, 960, 600);
            var ui = new SerializedObject(panel.gameObject.AddComponent<AuctionUI>());
            void Bind(string property, Object obj) => ui.FindProperty(property).objectReferenceValue = obj;

            Label(panel, font, "Auction", 40, 32, 460, 48, 34, Accent);
            Bind("manager", manager);
            Bind("budget", Label(panel, font, "Budget", 520, 32, 240, 48, 24, Color.white));
            Bind("timer", Label(panel, font, "Timer", 820, 32, 100, 48, 34, Accent));

            var image = Box("ItemImage", panel, 40, 96, 244, 178).gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            Bind("itemImage", image);
            var category = Label(panel, font, "", 40, 96, 244, 178, 22, Color.white);
            category.alignment = TextAlignmentOptions.Center;
            Bind("category", category);
            Bind("itemName", Label(panel, font, "", 310, 98, 610, 52, 36, Color.white));
            Bind("openingPrice", Label(panel, font, "", 310, 162, 300, 34, 22, Color.white));
            Bind("highestBid", Label(panel, font, "", 310, 200, 300, 34, 22, Accent));
            Bind("highestBidder", Label(panel, font, "", 310, 238, 300, 34, 22, Color.white));
            Bind("status", Label(panel, font, "", 40, 286, 880, 42, 26, Accent));

            Label(panel, font, "Opponents", 620, 162, 300, 34, 22, Accent);
            Bind("opponentNames", Label(panel, font, "", 620, 200, 300, 54, 18, Color.white));
            Label(panel, font, "Recent bids", 620, 345, 300, 34, 22, Accent);
            var history = Label(panel, font, "", 620, 383, 300, 176, 20, Color.white);
            history.alignment = TextAlignmentOptions.TopLeft;
            Bind("history", history);

            Bind("openingButton", BidButton(panel, font, "BID", 40, 360, 220, out var openingLabel));
            Bind("openingButtonLabel", openingLabel);
            Bind("tenButton", BidButton(panel, font, "+$10", 40, 360, 148, out _));
            Bind("twentyFiveButton", BidButton(panel, font, "+$25", 208, 360, 148, out _));
            Bind("fiftyButton", BidButton(panel, font, "+$50", 376, 360, 148, out _));
            ui.ApplyModifiedPropertiesWithoutUndo();

            var report = new SerializedObject(system.GetComponent<AuctionReportUI>());
            report.FindProperty("auction").objectReferenceValue = manager;
            report.FindProperty("auctionPanel").objectReferenceValue = panel;
            report.FindProperty("level").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<MvpAuctionLevel>("Assets/AuctionMvp/MvpAuctionLevel.asset");
            report.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new System.InvalidOperationException("Could not save the auction demo scene.");
        }

        private static TMP_FontAsset GetFont()
        {
            TMP_Settings settings = TMP_Settings.LoadDefaultSettings();
            if (!settings || !TMP_Settings.defaultFontAsset)
                throw new System.InvalidOperationException("Import TMP Essential Resources before creating the demo.");

            return TMP_Settings.defaultFontAsset;
        }

        private static RectTransform Box(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            bool root = parent is RectTransform && parent.GetComponent<Canvas>();
            rect.anchorMin = rect.anchorMax = root ? new Vector2(0.5f, 0.5f) : new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, root ? y : -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static TextMeshProUGUI Label(Transform parent, TMP_FontAsset font, string text,
            float x, float y, float width, float height, float size, Color color)
        {
            var label = Box(string.IsNullOrEmpty(text) ? "Label" : text, parent, x, y, width, height)
                .gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            return label;
        }

        private static Button BidButton(Transform parent, TMP_FontAsset font, string text, float x, float y,
            float width, out TMP_Text label)
        {
            var gameObject = Box(text + " Button", parent, x, y, width, 62).gameObject;
            var image = gameObject.AddComponent<Image>();
            image.color = Panel;
            var button = gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            label = Label(gameObject.transform, font, text, 0, 0, width, 62, 25, Color.white);
            label.alignment = TextAlignmentOptions.Center;
            return button;
        }
    }
}

