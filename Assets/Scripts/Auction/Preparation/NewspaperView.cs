using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class NewspaperView : MonoBehaviour
    {
        [Serializable]
        internal sealed class Appearance
        {
            public Sprite reporterSprite;
            public Sprite televisionSprite;
            public Sprite paperSprite;
            public TMP_FontAsset mastheadFont;
            public TMP_FontAsset articleFont;
            public Color paperColor = new(0.98f, 0.97f, 0.94f);
            public Color textPanelColor = new(0.88f, 0.87f, 0.84f);
            public Color pictureColor = new(0.73f, 0.73f, 0.69f);
            public Color textColor = PreparationUi.Ink;
            public string continuePrompt = "tap to continue";
        }

        private static readonly Vector2 PageSize = new(400, 650);
        private RectTransform page;
        private Image illustration;
        private TMP_Text missingImage;
        private TMP_Text headline;
        private TMP_Text body;
        private Button continueButton;
        private ScrollRect articleScroll;
        private IReadOnlyList<NewspaperEntry> entries;
        private readonly Dictionary<AuctionItemType, (Sprite sprite, Color color)> images = new();
        private Action completed;
        private int index;

        public bool IsOpen => completed != null && gameObject.activeInHierarchy;

        internal void Initialize(Appearance appearance)
        {
            PreparationUi.Surface((RectTransform)transform, new Color(0.16f, 0.16f, 0.16f));
            var center = new Vector2(0.5f, 0.5f);
            page = PreparationUi.Rect("News page", transform, center, center, -PageSize / 2, PageSize / 2);
            var paper = PreparationUi.Surface(page, appearance.paperColor);
            paper.sprite = appearance.paperSprite;

            var masthead = PreparationUi.Label(Row("Masthead", page, 22, 16, 80), "NEWSSSS",
                59, appearance.textColor, FontStyles.Bold);
            if (appearance.mastheadFont)
                masthead.font = appearance.mastheadFont;
            masthead.textWrappingMode = TextWrappingModes.NoWrap;
            masthead.enableAutoSizing = true;
            masthead.fontSizeMin = 32;
            masthead.fontSizeMax = 59;
            var subtitle = PreparationUi.Label(Row("Subtitle", page, 22, 77, 103), "WORLD SPECIAL NEWS",
                20, appearance.textColor);
            if (appearance.mastheadFont)
                subtitle.font = appearance.mastheadFont;

            RectTransform picture;
            if (appearance.televisionSprite)
            {
                var television = PreparationUi.Rect("Television", page, Vector2.up, Vector2.up,
                    new Vector2(22, -378), new Vector2(240, -108));
                var televisionImage = PreparationUi.Surface(television, Color.white);
                televisionImage.sprite = appearance.televisionSprite;
                picture = PreparationUi.Rect("Television screen", television,
                    new Vector2(.14f, .26f), new Vector2(.67f, .60f), Vector2.zero, Vector2.zero);
            }
            else
            {
                var frame = Row("Picture frame", page, 22, 112, 282);
                PreparationUi.Surface(frame, Color.black);
                picture = PreparationUi.Rect("Picture", frame, Vector2.zero, Vector2.one,
                    new Vector2(6, 6), new Vector2(-6, -6));
                PreparationUi.Surface(picture, appearance.pictureColor);
            }
            illustration = PreparationUi.Surface(PreparationUi.Fill("ItemType image", picture), Color.white);
            illustration.raycastTarget = false;
            illustration.preserveAspect = true;
            missingImage = PreparationUi.Label(PreparationUi.Fill("Image placeholder", picture),
                "Image unavailable", 20, appearance.textColor);
            missingImage.alignment = TextAlignmentOptions.Center;

            PreparationUi.Surface(Row("Text panel", page, 0, 382, 650), appearance.textPanelColor);
            var reporter = PreparationUi.Rect("Reporter", page, Vector2.up, Vector2.up,
                appearance.televisionSprite ? new Vector2(240, -381) : new Vector2(22, -418),
                appearance.televisionSprite ? new Vector2(378, -204) : new Vector2(216, -292));
            var portrait = PreparationUi.Surface(reporter,
                appearance.reporterSprite ? Color.white : appearance.pictureColor);
            portrait.sprite = appearance.reporterSprite;
            portrait.preserveAspect = true;
            if (!appearance.reporterSprite)
                PreparationUi.Label(PreparationUi.Fill("Reporter placeholder", reporter), "Reporter", 20,
                    appearance.textColor).alignment = TextAlignmentOptions.Center;

            BuildArticle(Row("Article", page, 22, 430, 587), appearance);
            continueButton = PreparationUi.Button(Row("Continue News", page, 22, 593, 638),
                appearance.continuePrompt);
            continueButton.targetGraphic.color = appearance.paperColor;
            continueButton.GetComponentInChildren<TMP_Text>().color = appearance.textColor;
            continueButton.onClick.AddListener(Advance);
            Hide();
        }

        private void BuildArticle(RectTransform viewport, Appearance appearance)
        {
            PreparationUi.Surface(viewport, appearance.textPanelColor);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = PreparationUi.Rect("Article content", viewport, Vector2.up, Vector2.one,
                Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 5;
            layout.padding = new RectOffset(0, 4, 0, 6);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            articleScroll = viewport.gameObject.AddComponent<ScrollRect>();
            articleScroll.viewport = viewport;
            articleScroll.content = content;
            articleScroll.horizontal = false;
            articleScroll.movementType = ScrollRect.MovementType.Clamped;
            articleScroll.scrollSensitivity = 32;

            headline = PreparationUi.Label(PreparationUi.Fill("Headline", content), "", 24,
                appearance.textColor, FontStyles.Bold);
            body = PreparationUi.Label(PreparationUi.Fill("Body", content), "", 21, appearance.textColor);
            if (appearance.articleFont)
                headline.font = body.font = appearance.articleFont;
        }

        internal void Present(MvpAuctionLevel level, Action onCompleted)
        {
            entries = level.GetNewspaperEntries();
            if (entries.Count == 0)
            {
                Debug.LogWarning("The level has no valid NEWSSSS articles.", level);
                onCompleted();
                return;
            }

            index = 0;
            images.Clear();
            var random = new System.Random();
            foreach (NewspaperEntry entry in entries)
            {
                if (!images.ContainsKey(entry.ItemType))
                    images.Add(entry.ItemType, ResolveImage(entry.ItemType, level.Database, random));
            }

            completed = onCompleted;
            gameObject.SetActive(true);
            ShowArticle();
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
        }

        private (Sprite sprite, Color color) ResolveImage(AuctionItemType itemType, ItemDatabase database,
            System.Random random)
        {
            if (itemType.NewspaperSprite)
                return (itemType.NewspaperSprite, Color.white);

            if (database)
            {
                Sprite[] candidates = database.GetVariantsByType(itemType)
                    .Select(item => item.Sprite).Where(sprite => sprite).ToArray();
                if (candidates.Length > 0)
                    return (candidates[random.Next(candidates.Length)], Color.black);
            }

            Debug.LogWarning($"No NEWSSSS image for item type '{itemType.TypeId}'.", itemType);
            return (null, Color.white);
        }

        private void ShowArticle()
        {
            NewspaperEntry entry = entries[index];
            var image = images[entry.ItemType];
            illustration.sprite = image.sprite;
            illustration.color = image.color;
            illustration.enabled = image.sprite;
            missingImage.gameObject.SetActive(!image.sprite);
            headline.text = entry.Headline;
            body.text = entry.BodyText;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(articleScroll.content);
            articleScroll.StopMovement();
            articleScroll.verticalNormalizedPosition = 1;
        }

        private void Advance()
        {
            if (!IsOpen)
                return;

            index++;
            if (index < entries.Count)
            {
                ShowArticle();
                return;
            }

            Action onCompleted = completed;
            Hide();
            onCompleted();
        }

        internal void Hide()
        {
            completed = null;
            gameObject.SetActive(false);
        }

        private void OnEnable() => FitPage();
        private void OnRectTransformDimensionsChange() => FitPage();

        private void FitPage()
        {
            if (!page)
                return;

            Vector2 available = ((RectTransform)transform).rect.size - new Vector2(20, 20);
            float scale = Mathf.Max(0, Mathf.Min(available.x / PageSize.x, available.y / PageSize.y));
            page.localScale = new Vector3(scale, scale, 1);
        }

        private static RectTransform Row(string name, RectTransform parent, int margin, int top, int bottom) =>
            PreparationUi.Rect(name, parent, Vector2.up, Vector2.one,
                new Vector2(margin, -bottom), new Vector2(-margin, -top));
    }
}
