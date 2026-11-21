using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    internal sealed class BreakingNewsView : MonoBehaviour
    {
        private RectTransform safe;
        private RectTransform broadcast;
        private RectTransform reader;
        private Image illustration;
        private TMP_Text missingImage;
        private TMP_Text body;
        private NewspaperView.Appearance appearance;
        private GameObject lowerPaper;
        private GameObject introPromptBacking;
        private IReadOnlyList<NewsCard> cards;
        private Action completed;
        private Func<bool> inputReady;
        private int index;
        private int lastAdvanceFrame;

        internal NewsCard CurrentCard => completed != null && index >= 0 ? cards[index] : null;

        internal void Initialize(NewspaperView.Appearance appearance)
        {
            var background = PreparationUi.Fill("Background", transform);
            if (appearance.paperSprite || !appearance.paperTexture)
                Art(background, appearance.paperSprite, new Color(.83f, .73f, .47f), false);
            else
            {
                var paper = background.gameObject.AddComponent<RawImage>();
                paper.texture = appearance.paperTexture;
                paper.uvRect = appearance.paperTextureUv;
                paper.raycastTarget = false;
            }
            lowerPaper = Art(Box("Lower parchment", background, 0, 0, 1, .31f), null,
                new Color(.34f, .19f, .09f, .56f), false).gameObject;
            safe = PreparationUi.Fill("News Safe Area", transform);

            var tap = PreparationUi.Button(PreparationUi.Fill("Continue News", transform), "");
            tap.transition = Selectable.Transition.None;
            tap.navigation = new Navigation { mode = Navigation.Mode.None };
            tap.targetGraphic.color = Color.clear;
            tap.targetGraphic.canvasRenderer.cullTransparentMesh = false;
            tap.onClick.AddListener(Advance);
            tap.transform.SetAsFirstSibling();
            background.SetAsFirstSibling();

            var header = Label(Box("BreakingNewsHeader", safe, .08f, .76f, .95f, .93f),
                "BREAKING\nNEWS", 92, appearance.mastheadFont, FontStyles.Bold);
            header.color = Color.white;
            header.alignment = TextAlignmentOptions.TopLeft;
            header.textWrappingMode = TextWrappingModes.NoWrap;
            header.lineSpacing = -12;

            reader = Box("Reporter", safe, .025f, 0, .975f, .58f);
            if (appearance.reporterSprite)
                Art(reader, appearance.reporterSprite, Color.white);
            else
                Label(reader, "[Moderátor]", 32, appearance.articleFont);

            var broadcastArea = Box("Broadcast Area", safe, .005f, .24f, .995f, .82f);
            broadcast = PreparationUi.Fill("BroadcastContent", broadcastArea);
            var television = PreparationUi.Fill("RetroTVFrame", broadcast);
            var fitter = broadcast.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = appearance.televisionSprite
                ? appearance.televisionSprite.rect.width / appearance.televisionSprite.rect.height : .83f;
            Art(television, appearance.televisionSprite, new Color(.18f, .09f, .05f));
            if (!appearance.televisionSprite)
                Label(Box("Frame placeholder", television, .05f, .04f, .95f, .16f),
                    "[Televízor]", 24, appearance.articleFont);

            var screen = Box("TVScreenContent", broadcast, .14f, .25f, .67f, .59f);
            illustration = Art(screen, null, Color.clear);
            missingImage = Label(PreparationUi.Fill("Illustration placeholder", screen),
                "", 24, appearance.articleFont);
            missingImage.color = appearance.televisionSprite ? PreparationUi.Ink : Color.white;
            this.appearance = appearance;

            body = Label(Box("NewsBodyText", safe, .07f, .075f, .93f, .25f),
                "", 36, appearance.articleFont);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.color = new Color(1f, .80f, .75f);
            introPromptBacking = Art(Box("Intro prompt backing", safe, .32f, .021f, .68f, .059f), null,
                new Color(.24f, .15f, .07f, .87f)).gameObject;
            Label(Box("TapToContinue", safe, .08f, .015f, .92f, .065f),
                appearance.continuePrompt, 25, appearance.articleFont);
            Hide();
        }

        internal void Present(IReadOnlyList<NewsCard> edition, Func<bool> canAdvance, Action onCompleted)
        {
            cards = edition;
            completed = onCompleted;
            inputReady = canAdvance;
            index = -1;
            lastAdvanceFrame = Time.frameCount;
            reader.gameObject.SetActive(true);
            broadcast.gameObject.SetActive(false);
            body.gameObject.SetActive(false);
            lowerPaper.SetActive(false);
            introPromptBacking.SetActive(true);
            gameObject.SetActive(true);
            Update();
        }

        internal void Hide()
        {
            completed = null;
            inputReady = null;
            cards = null;
            body.text = "";
            illustration.sprite = null;
            gameObject.SetActive(false);
        }

        private void Advance()
        {
            if (!isActiveAndEnabled || completed == null || !inputReady() || lastAdvanceFrame == Time.frameCount)
                return;

            lastAdvanceFrame = Time.frameCount;
            if (++index >= cards.Count)
            {
                Action onCompleted = completed;
                Hide();
                onCompleted();
                return;
            }

            NewsCard card = cards[index];
            reader.gameObject.SetActive(false);
            broadcast.gameObject.SetActive(true);
            body.gameObject.SetActive(true);
            lowerPaper.SetActive(true);
            introPromptBacking.SetActive(false);
            body.text = card.Body;
            Sprite placeholder = card.Type switch
            {
                NewsType.Item => appearance.itemPlaceholder,
                NewsType.Bidder => appearance.bidderPlaceholder,
                _ => appearance.worldPlaceholder
            };
            illustration.sprite = card.Image ? card.Image : placeholder;
            illustration.enabled = illustration.sprite;
            illustration.color = Color.white;
            missingImage.gameObject.SetActive(!illustration.sprite);
            missingImage.text = card.Type switch
            {
                NewsType.Item => "[Predmet]",
                NewsType.Bidder => "[Portrét]",
                _ => "[Ilustrácia]"
            };
        }

        private void Update()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;
            Rect area = Screen.safeArea;
            safe.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safe.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        }

        private static RectTransform Box(string name, Transform parent, float left, float bottom,
            float right, float top) => PreparationUi.Rect(name, parent, new Vector2(left, bottom),
                new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Image Art(RectTransform rect, Sprite sprite, Color fallback, bool preserveAspect = true)
        {
            var image = PreparationUi.Surface(rect, sprite ? Color.white : fallback);
            image.sprite = sprite;
            image.preserveAspect = preserveAspect;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Label(RectTransform rect, string text, int size, TMP_FontAsset font,
            FontStyles style = FontStyles.Normal)
        {
            var label = PreparationUi.Label(rect, text, size, new Color(1, .97f, .90f), style);
            if (font)
                label.font = font;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18;
            label.fontSizeMax = size;
            return label;
        }
    }
}
