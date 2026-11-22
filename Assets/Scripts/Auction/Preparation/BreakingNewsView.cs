using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    internal sealed class BreakingNewsView : MonoBehaviour
    {
        [Serializable]
        internal sealed class Effects
        {
            public bool enableHeadlineSlam = true;
            [Min(.01f)] public float slamDuration = .35f;
            [Range(1, 1.4f)] public float slamScale = 1.25f;
            public bool enableScanlines = true;
            [Range(0, .12f)] public float scanlineOpacity = .045f;
            [Range(0, 1.2f)] public float staticDuration = .75f;
            public bool enableCardTransition = true;
            [Min(.01f)] public float transitionDuration = .30f;
            [Range(0, 60)] public float transitionOffset = 25;
        }

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
        private Effects effects;
        private CanvasGroup headline;
        private float slamStarted;
        private bool slamming;
        private CanvasGroup imageContent;
        private RectTransform articleRegion;
        private RawImage staticOverlay;
        private Texture2D scanlineTexture;
        private Texture2D staticTexture;
        private bool transitioning;
        private bool swappedCard;
        private float transitionStarted;
        private float staticStarted = float.NegativeInfinity;

        internal NewsCard CurrentCard => completed != null && index >= 0 ? cards[index] : null;

        internal void Initialize(NewspaperView.Appearance appearance, BidderSpeechBubble speechBubble,
            AudioClip openingJingle, Effects settings)
        {
            effects = settings;
            jingleSource = gameObject.AddComponent<AudioSource>();
            jingleSource.playOnAwake = false;
            jingleSource.spatialBlend = 0;
            jingleSource.clip = openingJingle;

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
                "MIMORIADNE\nSPRÁVY", 92, appearance.mastheadFont, FontStyles.Bold);
            header.color = Color.white;
            header.alignment = TextAlignmentOptions.TopLeft;
            header.textWrappingMode = TextWrappingModes.NoWrap;
            header.lineSpacing = -12;
            headline = header.gameObject.AddComponent<CanvasGroup>();
            headline.blocksRaycasts = false;

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

            var screen = Box("TV Screen", broadcast, .14f, .25f, .67f, .59f);
            screen.gameObject.AddComponent<RectMask2D>();
            var imageRegion = PreparationUi.Fill("Article Picture", screen);
            imageContent = imageRegion.gameObject.AddComponent<CanvasGroup>();
            imageContent.blocksRaycasts = false;
            illustration = Art(PreparationUi.Fill("TVScreenContent", imageRegion), null, Color.clear);
            missingImage = Label(PreparationUi.Fill("Illustration placeholder", imageRegion),
                "", 24, appearance.articleFont);
            missingImage.color = appearance.televisionSprite ? PreparationUi.Ink : Color.white;
            BuildTvEffects(screen);
            this.appearance = appearance;

            introBubble = BidderSpeechBubble.CreateDialogue(speechBubble,
                Box("Presenter Speech", safe, .06f, .44f, .94f, .65f), "Presenter Dialogue", 34);
            articleRegion = Box("Article Speech", safe, .06f, .075f, .94f, .32f);
            articleBubble = BidderSpeechBubble.CreateDialogue(speechBubble, articleRegion, "NewsBodyText", 36);
            introPromptBacking = Art(Box("Intro prompt backing", safe, .32f, .021f, .68f, .059f), null,
                new Color(.24f, .15f, .07f, .87f)).gameObject;
            var continuePrompt = Label(Box("TapToContinue", safe, .08f, .015f, .92f, .065f),
                appearance.continuePrompt, 25, appearance.articleFont);
            BidButtonPress.Bind(tap, visual: continuePrompt.rectTransform);
            Hide();
        }

        internal void Present(IReadOnlyList<NewsCard> edition, string openingDialogue,
            Func<bool> canAdvance, Action onCompleted)
        {
            ResetEffects();
            presenterDialogue = openingDialogue;
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
            slamming = effects.enableHeadlineSlam;
            slamStarted = float.PositiveInfinity;
            if (slamming)
                SetHeadline(0, effects.slamScale, 14);
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
            if (!isActiveAndEnabled || completed == null || transitioning || !inputReady() ||
                lastAdvanceFrame == Time.frameCount)
                return;

            lastAdvanceFrame = Time.frameCount;
            if (index + 1 >= cards.Count)
            {
                Action onCompleted = completed;
                Hide();
                onCompleted();
                return;
            }

            if (index >= 0 && effects.enableCardTransition)
            {
                transitioning = true;
                swappedCard = false;
                transitionStarted = Time.unscaledTime;
                return;
            }

            bool switching = index >= 0;
            ShowCard(++index);
            if (switching)
                StartStatic();
        }

        private void ShowCard(int cardIndex)
        {
            NewsCard card = cards[cardIndex];
            reader.gameObject.SetActive(false);
            broadcast.parent.gameObject.SetActive(true);
            broadcast.gameObject.SetActive(true);
            introBubble.Hide();
            introPromptBacking.SetActive(false);
            articleBubble.SetDialogue(card.DisplayText);
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

        private void UpdateCardTransition(float now)
        {
            if (!transitioning)
                return;

            float progress = Mathf.Clamp01((now - transitionStarted) / effects.transitionDuration);
            if (progress < .5f)
            {
                float outgoing = Mathf.SmoothStep(0, 1, progress * 2);
                SetCardMotion(1 - outgoing, -effects.transitionOffset * outgoing);
                return;
            }

            if (!swappedCard)
            {
                ShowCard(++index);
                swappedCard = true;
                StartStatic();
            }

            float incoming = Mathf.SmoothStep(0, 1, (progress - .5f) * 2);
            SetCardMotion(incoming, effects.transitionOffset * (1 - incoming));
            transitioning = progress < 1;
            if (!transitioning)
                lastAdvanceFrame = Time.frameCount;
        }

        private void SetCardMotion(float alpha, float offset)
        {
            imageContent.alpha = articleBubble.Group.alpha = alpha;
            ((RectTransform)imageContent.transform).anchoredPosition =
                articleRegion.anchoredPosition = Vector2.right * offset;
        }

        private void UpdateHeadline(float now)
        {
            if (!slamming)
                return;
            if (float.IsPositiveInfinity(slamStarted))
            {
                // Wait for the existing parchment fade to uncover the headline.
                if (!inputReady())
                    return;
                slamStarted = now;
            }
            float progress = Mathf.Clamp01((now - slamStarted) / effects.slamDuration);
            float impact = Mathf.SmoothStep(0, 1, progress / .45f);
            float scale = progress < .45f ? Mathf.Lerp(effects.slamScale, .97f, impact) :
                Mathf.SmoothStep(.97f, 1, (progress - .45f) / .55f);
            SetHeadline(Mathf.Clamp01(progress * 5), scale, 14 * (1 - impact));
            slamming = progress < 1;
        }

        private void SetHeadline(float alpha, float scale, float offset)
        {
            headline.alpha = alpha;
            headline.transform.localScale = Vector3.one * scale;
            ((RectTransform)headline.transform).anchoredPosition = Vector2.up * offset;
        }

        private void BuildTvEffects(RectTransform screen)
        {
            if (effects.enableScanlines && effects.scanlineOpacity > 0)
            {
                scanlineTexture = BuildTexture("TV Scanline Pattern", 1, 4,
                    new[] { new Color32(38, 26, 15, 255), new Color32(), new Color32(), new Color32() });
                AddOverlay("TV Scanlines", screen, scanlineTexture,
                    new Rect(0, 0, 1, 48), new Color(1, 1, 1, effects.scanlineOpacity));
            }

            if (effects.staticDuration <= 0)
                return;
            const int size = 32;
            var pixels = new Color32[size * size];
            var random = new System.Random(8421);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte tone = (byte)random.Next(65, 245);
                pixels[i] = new Color32(tone, (byte)(tone * .93f), (byte)(tone * .79f), 255);
            }
            staticTexture = BuildTexture("TV Static Pattern", size, size, pixels);
            staticOverlay = AddOverlay("TV Static", screen, staticTexture, new Rect(0, 0, 3, 2), Color.clear);
            staticOverlay.enabled = false;
        }

        private static Texture2D BuildTexture(string name, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static RawImage AddOverlay(string name, RectTransform screen, Texture texture, Rect uv, Color color)
        {
            var overlay = PreparationUi.Fill(name, screen).gameObject.AddComponent<RawImage>();
            overlay.texture = texture;
            overlay.uvRect = uv;
            overlay.color = color;
            overlay.raycastTarget = false;
            return overlay;
        }

        private void StartStatic()
        {
            if (!staticOverlay)
                return;
            staticStarted = Time.unscaledTime;
            staticOverlay.enabled = true;
        }

        private void UpdateStatic(float now)
        {
            if (!staticOverlay || !staticOverlay.enabled)
                return;
            float elapsed = now - staticStarted;
            float progress = Mathf.Clamp01(elapsed / effects.staticDuration);
            float fade = Mathf.InverseLerp(.60f, 1f, progress);
            staticOverlay.color = new Color(1, 1, 1, .48f * (1 - fade));
            staticOverlay.uvRect = new Rect(0, Mathf.Floor(elapsed * 15f) * .17f, 3, 2);
            staticOverlay.enabled = progress < 1;
        }

        private void ResetEffects()
        {
            slamming = transitioning = false;
            SetHeadline(1, 1, 0);
            SetCardMotion(1, 0);
            if (staticOverlay)
                staticOverlay.enabled = false;
        }

        private void OnDisable()
        {
            if (!headline)
                return;
            ResetEffects();
            jingleSource.Stop();
        }

        private void OnDestroy()
        {
            if (scanlineTexture)
                Destroy(scanlineTexture);
            if (staticTexture)
                Destroy(staticTexture);
        }

        private void Update()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;
            Rect area = Screen.safeArea;
            safe.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safe.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            if (completed == null)
                return;
            float now = Time.unscaledTime;
            UpdateHeadline(now);
            UpdateCardTransition(now);
            UpdateStatic(now);
            if (index >= 0)
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
