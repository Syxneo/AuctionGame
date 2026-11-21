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
        private BidderSpeechBubble articleBubble;
        private BidderSpeechBubble introBubble;
        private string presenterDialogue;
        private AudioSource jingleSource;
        private float entranceStarted;
        private const float EntranceDuration = .32f;
        private NewspaperView.Appearance appearance;
        private GameObject introPromptBacking;
        private IReadOnlyList<NewsCard> cards;
        private Action completed;
        private Func<bool> inputReady;
        private int index;
        private int lastAdvanceFrame;

        internal NewsCard CurrentCard => completed != null && index >= 0 ? cards[index] : null;

        internal void Initialize(NewspaperView.Appearance appearance, BidderSpeechBubble speechBubble,
            string openingDialogue, AudioClip openingJingle)
        {
            jingleSource = gameObject.AddComponent<AudioSource>();
            jingleSource.playOnAwake = false;
            jingleSource.spatialBlend = 0;
            jingleSource.clip = openingJingle ? openingJingle : Resources.Load<AudioClip>("Auction/NewsIntroJingle");

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

            reader = Box("Reporter", safe, .025f, 0, .975f, .54f);
            reader.pivot = new Vector2(.5f, 0);
            reader.offsetMin = reader.offsetMax = Vector2.zero;
            if (appearance.reporterSprite)
                Art(reader, appearance.reporterSprite, Color.white);
            else
                Label(reader, "[Moderátor]", 32, appearance.articleFont);

            var broadcastArea = Box("Broadcast Area", safe, .005f, .32f, .995f, .76f);
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

            presenterDialogue = openingDialogue;
            introBubble = BidderSpeechBubble.CreateDialogue(speechBubble,
                Box("Presenter Speech", safe, .06f, .44f, .94f, .65f), "Presenter Dialogue", 34);
            articleBubble = BidderSpeechBubble.CreateDialogue(speechBubble,
                Box("Article Speech", safe, .06f, .075f, .94f, .32f), "NewsBodyText", 36);
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
            broadcast.parent.gameObject.SetActive(false);
            articleBubble.Hide();
            introBubble.Hide();
            entranceStarted = Time.unscaledTime;
            introPromptBacking.SetActive(true);
            gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Update();
            if (jingleSource.clip)
                jingleSource.Play();
        }

        internal void Hide()
        {
            completed = null;
            inputReady = null;
            cards = null;
            articleBubble.Hide();
            introBubble.Hide();
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
            broadcast.parent.gameObject.SetActive(true);
            broadcast.gameObject.SetActive(true);
            introBubble.Hide();
            introPromptBacking.SetActive(false);
            articleBubble.SetDialogue(card.Body);
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
            if (completed == null || index >= 0)
                return;

            // The artwork rests on the physical screen edge while controls stay inside the safe area.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, Vector2.zero, null, out Vector2 edge);
            float bottomOffset = edge.y - safe.rect.yMin;
            float progress = Mathf.Clamp01((Time.unscaledTime - entranceStarted) / EntranceDuration);
            float entranceOffset = (reader.rect.height + 2) * (1 - Mathf.SmoothStep(0, 1, progress));
            reader.anchoredPosition = Vector2.up * (bottomOffset - entranceOffset);
            if (progress >= 1 && !introBubble.gameObject.activeSelf && !string.IsNullOrWhiteSpace(presenterDialogue))
            {
                introBubble.SetDialogue(presenterDialogue);
                introBubble.ShowBottomPointer(110);
            }
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
