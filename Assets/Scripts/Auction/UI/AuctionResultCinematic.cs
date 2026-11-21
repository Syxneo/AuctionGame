using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    public sealed class AuctionResultCinematic : MonoBehaviour
    {
        [SerializeField] private AuctionManager auction;
        [SerializeField] private SceneIntroOutro stage;
        [SerializeField] private Transform podium;
        [SerializeField] private CanvasGroup auctionHud;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Sprite soldStamp;
        [SerializeField] private SaleAuctioneer sale;
        [SerializeField] private BidderSpeechBubble speechBubble;
        [SerializeField] private PreparationPhase preparation;
        [Header("Winner Presentation")]
        [SerializeField] private bool enableWinnerTextReveal = true;
        [SerializeField, Min(0.01f)] private float winnerRevealDuration = 0.3f;
        [SerializeField, Range(1f, 1.12f)] private float winnerRevealScale = 1.08f;
        [SerializeField] private bool enableAiWinnerEntrance = true;
        [SerializeField, Min(0.01f)] private float aiEntranceDuration = 0.4f;
        [SerializeField, Range(0f, 32f)] private float aiEntranceOffset = 24f;
        [Header("SOLD Impact")]
        [SerializeField] private bool enableSoldStampImpact = true;
        [SerializeField, Min(0.01f)] private float stampDuration = 0.35f;
        [SerializeField, Min(1)] private float stampStartScale = 1.4f;
        [SerializeField, Range(1f, 1.4f)] private float soldRibbonWidth = 1.2f;
        [SerializeField, Min(1f)] private float stampFleckScale = 2.5f;
        [Header("Player Confetti")]
        [SerializeField] private bool enablePlayerConfetti = true;
        [SerializeField, Min(0.01f)] private float confettiDuration = 0.8f;
        [SerializeField, Range(1, 20)] private int confettiCount = 12;

        private static readonly Color Gold = new(0.91f, 0.75f, 0.49f);
        private static readonly Color Ivory = new(1f, 0.95f, 0.85f);
        private static readonly Color SoldRed = new(0.46f, 0.10f, 0.16f);
        private static readonly Color UnsoldBlue = new(0.19f, 0.25f, 0.32f);
        private static readonly Color VictoryGreen = new(0.64f, 0.84f, 0.49f);

        private Canvas overlay;
        private Button continueButton;
        private RectTransform itemBackdrop;
        private Image itemImage;
        private Image ribbon;
        private RectTransform banner;
        private Image stamp;
        private TMP_Text stampCaption;
        private Image[] stampBurst;
        private Image[] confetti;
        private bool soldEffectsPlayed;
        private double soldEffectsStarted = double.NegativeInfinity;
        private TMP_Text outcome;
        private TMP_Text winner;
        private Image winnerPortrait;
        private Color winnerBaseColor;
        private Vector3 winnerBaseScale;
        private Vector3 portraitBaseScale;
        private Vector2 portraitBasePosition;
        private Color portraitBaseColor;
        private double winnerEffectsStarted = double.NegativeInfinity;
        private TMP_Text price;
        private BidderSpeechBubble saleComment;
        private AuctionResult activeResult;
        private BidderDialoguePresenter bidderDialogue;
        private bool waitingForDialogue;
        private bool hudHidden;
        private float hudAlpha;
        private bool hudInteractable;
        private bool hudBlocksRaycasts;
        private bool resultConfirmed;
        private bool presentationCompleted;

        private void OnEnable()
        {
            bidderDialogue = FindFirstObjectByType<BidderDialoguePresenter>();
            if (auction && stage && podium)
                auction.OnAuctionEnded += PresentResult;
        }

        private void OnDisable()
        {
            if (auction)
                auction.OnAuctionEnded -= PresentResult;
            Cancel();
        }

        private void OnDestroy()
        {
            if (overlay)
                Destroy(overlay.gameObject);
        }

        private void LateUpdate()
        {
            if (activeResult != null && (!auction || !auction.isActiveAndEnabled))
                Cancel();
            else if (waitingForDialogue)
                TryStartResult();

            if (!double.IsNegativeInfinity(soldEffectsStarted))
                UpdateSoldEffects((float)(Time.unscaledTimeAsDouble - soldEffectsStarted));
            if (!double.IsNegativeInfinity(winnerEffectsStarted))
                UpdateWinnerEffects((float)(Time.unscaledTimeAsDouble - winnerEffectsStarted));
        }

        private void Cancel()
        {
            if (activeResult == null)
                return;
            if (stage)
                stage.CancelResult();
            if (activeResult != null)
                FinishResult();
        }

        private void PresentResult(AuctionResult result)
        {
            if (!isActiveAndEnabled || activeResult != null || !auction.CurrentItem ||
                !stage || !stage.isActiveAndEnabled || !podium || !auction.HoldResultForPresentation(result))
                return;

            activeResult = result;
            waitingForDialogue = true;
            resultConfirmed = false;
            presentationCompleted = false;
            soldEffectsPlayed = false;
            TryStartResult();
        }

        private void TryStartResult()
        {
            if (!stage || !stage.isActiveAndEnabled || !podium)
            {
                FinishResult();
                return;
            }
            if (auction.HasPendingWinnerDialogue || (bidderDialogue && bidderDialogue.HasActiveDialogue))
                return;

            waitingForDialogue = false;
            if (!stage.PlayResult(podium.position, ShowOverlay, FinishResult))
            {
                FinishResult();
                return;
            }

            if (!overlay)
                CreateOverlay();

            HideHud();
            if (sale)
            {
                sale.ClearBubble();
                if (string.IsNullOrWhiteSpace(sale.ResultLine))
                    saleComment.Hide();
                else
                    saleComment.SetDialogue(sale.ResultLine);
                if (activeResult.sold)
                    sale.StartGavelSlam();
            }
            AuctionResult result = activeResult;
            itemImage.sprite = auction.CurrentItem.Sprite;
            itemImage.enabled = itemImage.sprite != null;
            bool showStamp = result.sold && soldStamp;
            ribbon.color = result.sold ? SoldRed : UnsoldBlue;
            float ribbonOverhang = result.sold ? (soldRibbonWidth - 1f) * 0.5f : 0f;
            ribbon.rectTransform.anchorMin = new Vector2(-ribbonOverhang, 0.08f);
            ribbon.rectTransform.anchorMax = new Vector2(1f + ribbonOverhang, 0.92f);
            stamp.enabled = showStamp;
            stampCaption.enabled = showStamp;
            outcome.enabled = !showStamp;
            itemBackdrop.anchorMin = new Vector2(0.026f, result.sold ? 0.235f : 0.025f);
            itemImage.rectTransform.anchorMin = new Vector2(0.09f, result.sold ? 0.26f : 0.13f);
            banner.anchorMin = new Vector2(0.055f, result.sold ? 0.36f : 0.19f);
            banner.anchorMax = new Vector2(0.945f, result.sold ? 0.52f : 0.35f);
            outcome.text = result.sold ? "PREDANÉ" : "NEPREDANÉ";
            winner.text = !result.sold ? "" : result.winnerId == BidderIds.Player
                ? "VYHRAL SI"
                : $"VÍŤAZ: {auction.GetBidderDisplayName(result.winnerId)}";
            winner.color = result.sold && result.winnerId == BidderIds.Player ? VictoryGreen : Ivory;
            winnerBaseColor = winner.color;
            SetWinnerPortrait(result);
            winner.rectTransform.anchorMin = new Vector2(winnerPortrait.enabled ? 0.26f : 0.05f, 0.101f);
            price.text = result.sold ? $"${result.finalPrice}" : "";
            winner.gameObject.SetActive(result.sold);
            price.gameObject.SetActive(result.sold);
        }

        private void FinishResult()
        {
            waitingForDialogue = false;
            ShowOverlay(false);
            if (sale)
                sale.StopGavelSlam();
            RestoreHud();
            if (itemImage)
            {
                itemImage.sprite = null;
                winnerPortrait.enabled = false;
                winnerPortrait.sprite = null;
            }
            if (auction && activeResult != null)
                auction.CompleteResultPresentation(activeResult);
            activeResult = null;
            resultConfirmed = false;
            presentationCompleted = false;
        }

        private void HideHud()
        {
            if (!auctionHud)
                return;

            hudAlpha = auctionHud.alpha;
            hudInteractable = auctionHud.interactable;
            hudBlocksRaycasts = auctionHud.blocksRaycasts;
            hudHidden = true;
            auctionHud.alpha = 0f;
            auctionHud.interactable = false;
            auctionHud.blocksRaycasts = false;
        }

        private void RestoreHud()
        {
            if (!hudHidden)
                return;

            hudHidden = false;
            if (!auctionHud)
                return;

            auctionHud.alpha = hudAlpha;
            auctionHud.interactable = hudInteractable;
            auctionHud.blocksRaycasts = hudBlocksRaycasts;
        }

        private void ShowOverlay(bool visible)
        {
            if (overlay && overlay.enabled != visible)
            {
                overlay.enabled = visible;
                continueButton.interactable = visible;
                if (visible && activeResult != null && activeResult.sold && !soldEffectsPlayed)
                {
                    soldEffectsPlayed = true;
                    soldEffectsStarted = Time.unscaledTimeAsDouble;
                    UpdateSoldEffects(0f);
                    winnerEffectsStarted = Time.unscaledTimeAsDouble;
                    UpdateWinnerEffects(0f);
                }
                else if (!visible)
                {
                    ResetSoldEffects();
                    ResetWinnerEffects();
                }
                // The overlay hides exactly when the curtains start reopening after the
                // player confirmed the result. Complete the presentation right away so
                // the auction swaps in the next lot while the intro reveal is running,
                // instead of only after the whole cinematic has finished.
                if (!visible && resultConfirmed && activeResult != null && auction && !presentationCompleted)
                {
                    presentationCompleted = true;
                    auction.CompleteResultPresentation(activeResult);
                }
            }
        }

        private void ConfirmResult()
        {
            resultConfirmed = true;
            if (stage)
                stage.ConfirmResult();
        }

        private void CreateOverlay()
        {
            RectTransform root = Box("Auction Result Overlay", null, Vector2.zero, Vector2.one);
            SceneManager.MoveGameObjectToScene(root.gameObject, gameObject.scene);
            overlay = root.gameObject.AddComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = 50;
            overlay.enabled = false;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            var tap = Box("Continue Result", root, Vector2.zero, Vector2.one);
            var target = Fill(tap, Color.clear);
            target.raycastTarget = true;
            continueButton = tap.gameObject.AddComponent<Button>();
            continueButton.targetGraphic = target;
            continueButton.transition = Selectable.Transition.None;
            continueButton.interactable = false;
            continueButton.onClick.AddListener(ConfirmResult);
            root.gameObject.AddComponent<GraphicRaycaster>();

            RectTransform safeArea = Box("Result Safe Area", root, Vector2.zero, Vector2.one);
            safeArea.gameObject.AddComponent<AuctionHudSafeArea>();
            var continuePrompt = Label("Continue Prompt", safeArea, new Vector2(0.07f, 0.14f),
                new Vector2(0.93f, 0.245f), 58, Ivory);
            continuePrompt.text = "Klepni a pokračuj";
            BidButtonPress.Bind(continueButton, visual: continuePrompt.rectTransform);
            if (sale)
            {
                var commentary = Box("SALE Commentary Region", safeArea,
                    new Vector2(0.07f, 0.785f), new Vector2(0.93f, 0.96f));
                saleComment = BidderSpeechBubble.CreateDialogue(speechBubble, commentary, "SALE Commentary", 49);
            }
            RectTransform frame = Box("Result Frame", safeArea, new Vector2(0.11f, 0.27f), new Vector2(0.89f, 0.77f));
            Fill(frame, Gold);
            RectTransform card = Box("Result Card", frame, Vector2.zero, Vector2.one);
            card.offsetMin = new Vector2(3, 3);
            card.offsetMax = -card.offsetMin;
            Fill(card, new Color(0.09f, 0.067f, 0.10f));
            itemBackdrop = Box("Item Backdrop", card, new Vector2(0.026f, 0.235f), new Vector2(0.974f, 0.975f));
            Fill(itemBackdrop, new Color(0.17f, 0.12f, 0.17f));

            itemImage = Fill(Box("Result Item Sprite", card, new Vector2(0.09f, 0.26f), new Vector2(0.91f, 0.94f)),
                Color.white);
            itemImage.preserveAspect = true;

            banner = Box("Result Banner", card, new Vector2(0.055f, 0.32f), new Vector2(0.945f, 0.48f));
            banner.localRotation = Quaternion.Euler(0, 0, 7);
            Fill(Box("Ribbon Top Trim", banner, new Vector2(0, 0.92f), Vector2.one), Gold);
            Fill(Box("Ribbon Bottom Trim", banner, Vector2.zero, new Vector2(1, 0.08f)), Gold);
            // Keep the trim exposed while the red center snaps open behind the sticker.
            ribbon = Fill(Box("Result Ribbon", banner, new Vector2(0, 0.08f), new Vector2(1, 0.92f)), SoldRed);
            outcome = Label("Result Outcome", banner, new Vector2(0.08f, 0.1f), new Vector2(0.92f, 0.9f), 92, Ivory);
            outcome.fontStyle = FontStyles.Bold;
            outcome.characterSpacing = 10f;
            stamp = Fill(Box("Sold Stamp", banner, new Vector2(-.04f, -.21f),
                new Vector2(1.04f, 1.33f)), Color.white);
            stamp.sprite = soldStamp;
            stamp.preserveAspect = true;
            var stampFace = Box("Stamp Face", stamp.rectTransform, Vector2.zero, Vector2.one);
            var stampAspect = stampFace.gameObject.AddComponent<AspectRatioFitter>();
            stampAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            stampAspect.aspectRatio = soldStamp ? soldStamp.rect.width / soldStamp.rect.height : 1f;
            stampCaption = Label("Stamp Caption", stampFace, new Vector2(0.14f, 0.40f),
                new Vector2(0.81f, 0.68f), 54, Ivory);
            stampCaption.font = TMP_Settings.defaultFontAsset;
            stampCaption.fontSizeMin = 16;
            stampCaption.fontStyle = FontStyles.Bold;
            stampCaption.text = "PREDANÉ";

            winnerPortrait = Fill(Box("AI Winner Portrait", card, new Vector2(0.04f, 0.04f),
                new Vector2(0.25f, 0.232f)), Color.white);
            winnerPortrait.preserveAspect = true;
            winnerPortrait.enabled = false;
            portraitBaseScale = winnerPortrait.rectTransform.localScale;
            portraitBasePosition = winnerPortrait.rectTransform.anchoredPosition;
            portraitBaseColor = winnerPortrait.color;

            winner = Label("Result Winner", card, new Vector2(0.05f, 0.101f), new Vector2(0.95f, 0.245f), 96, Ivory);
            winner.fontStyle = FontStyles.Bold;
            winnerBaseScale = winner.rectTransform.localScale;
            winnerBaseColor = winner.color;
            price = Label("Result Price", card, new Vector2(0.05f, 0.012f), new Vector2(0.95f, 0.101f), 86, Gold);
            price.fontStyle = FontStyles.Bold;
        }

        private void SetWinnerPortrait(AuctionResult result)
        {
            Sprite portrait = null;
            if (preparation && result.sold && result.winnerId != BidderIds.Player)
            {
                foreach (AIBidder bidder in auction.Opponents)
                {
                    if (bidder.Id != result.winnerId)
                        continue;

                    portrait = preparation.GetBidderPortrait(bidder.Personality);
                    break;
                }
            }

            winnerPortrait.sprite = portrait;
            winnerPortrait.enabled = portrait != null;
            float left = portrait ? 0.29f : 0.05f;
            winner.rectTransform.anchorMin = new Vector2(left, 0.101f);
            price.rectTransform.anchorMin = new Vector2(left, 0.012f);
        }

        private void UpdateWinnerEffects(float elapsed)
        {
            float textProgress = enableWinnerTextReveal ? Mathf.Clamp01(elapsed / winnerRevealDuration) : 1f;
            float portraitProgress = enableAiWinnerEntrance && winnerPortrait.enabled
                ? Mathf.Clamp01(elapsed / aiEntranceDuration) : 1f;
            if (textProgress >= 1f && portraitProgress >= 1f)
            {
                ResetWinnerEffects();
                return;
            }

            float textScale = textProgress < 0.65f
                ? Mathf.SmoothStep(0.85f, winnerRevealScale, textProgress / 0.65f)
                : Mathf.SmoothStep(winnerRevealScale, 1f, (textProgress - 0.65f) / 0.35f);
            winner.rectTransform.localScale = winnerBaseScale * textScale;
            winner.alpha = winnerBaseColor.a * Mathf.SmoothStep(0f, 1f, textProgress);

            float rise = 1f - (1f - portraitProgress) * (1f - portraitProgress);
            winnerPortrait.rectTransform.anchoredPosition = portraitBasePosition - Vector2.up * (aiEntranceOffset * (1f - rise));
            winnerPortrait.rectTransform.localScale = portraitBaseScale * Mathf.Lerp(0.9f, 1f, rise);
            Color portraitColor = portraitBaseColor;
            portraitColor.a *= rise;
            winnerPortrait.color = portraitColor;
        }

        private void ResetWinnerEffects()
        {
            winnerEffectsStarted = double.NegativeInfinity;
            winner.rectTransform.localScale = winnerBaseScale;
            winner.color = winnerBaseColor;
            winnerPortrait.rectTransform.anchoredPosition = portraitBasePosition;
            winnerPortrait.rectTransform.localScale = portraitBaseScale;
            winnerPortrait.color = portraitBaseColor;
        }

        private void UpdateSoldEffects(float elapsed)
        {
            float stampProgress = Mathf.Clamp01(elapsed / stampDuration);
            float confettiProgress = Mathf.Clamp01(elapsed / confettiDuration);
            bool impact = enableSoldStampImpact && stamp.enabled;
            bool celebrate = enablePlayerConfetti && activeResult.winnerId == BidderIds.Player;
            if (impact && stampProgress < 1f)
            {
                float scale = stampProgress < 0.42f
                    ? Mathf.SmoothStep(stampStartScale, 0.95f, stampProgress / 0.42f)
                    : Mathf.SmoothStep(0.95f, 1f, (stampProgress - 0.42f) / 0.58f);
                var rect = stamp.rectTransform;
                rect.localScale = Vector3.one * scale;
                float shake = Mathf.Sin(stampProgress * 17f) * (1f - stampProgress);
                rect.localRotation = Quaternion.Euler(0f, 0f, 5f * (1f - stampProgress) + 2f * shake);
                rect.anchoredPosition = new Vector2(5f * shake, 0f);
                ribbon.rectTransform.localScale = new Vector3(Mathf.SmoothStep(0.6f, 1f, stampProgress * 3f), 1f, 1f);
                if (stampProgress >= 0.25f)
                {
                    stampBurst ??= CreateParticles("Stamp Flecks", 6, stampFleckScale);
                    AnimateParticles(stampBurst, (stampProgress - 0.25f) / 0.75f, false);
                }
            }
            else
                ResetStamp();

            if (celebrate && confettiProgress < 1f)
            {
                confetti ??= CreateParticles("Victory Confetti", confettiCount);
                AnimateParticles(confetti, confettiProgress, true);
            }
            else
                HideParticles(confetti);

            if ((!impact || stampProgress >= 1f) && (!celebrate || confettiProgress >= 1f))
                ResetSoldEffects();
        }

        private Image[] CreateParticles(string label, int count, float sizeMultiplier = 1f)
        {
            var particles = new Image[count];
            for (int i = 0; i < count; i++)
            {
                var rect = Box(label, banner, Vector2.one * 0.5f, Vector2.one * 0.5f);
                rect.sizeDelta = new Vector2(4f + i % 3 * 2f, 8f + i % 4 * 2f) * sizeMultiplier;
                particles[i] = Fill(rect, Gold);
            }
            return particles;
        }

        private void AnimateParticles(Image[] particles, float progress, bool victory)
        {
            float spread = 1f - (1f - progress) * (1f - progress);
            for (int i = 0; i < particles.Length; i++)
            {
                Image particle = particles[i];
                var rect = particle.rectTransform;
                float side = i % 2 == 0 ? -1f : 1f;
                float variation = (i * 7 % 11) / 10f;
                float width = banner.rect.width;
                float origin = victory ? 0.38f : 0.47f;
                rect.anchoredPosition = new Vector2(side * (width * origin + spread * (22f + variation * 55f)),
                    (variation - 0.35f) * 65f + spread * (victory ? 85f : 35f) -
                    progress * progress * (victory ? 140f : 20f));
                rect.localRotation = Quaternion.Euler(0f, 0f, i * 31f + side * progress * 140f);
                Color color = victory && i % 3 == 2 ? VictoryGreen : i % 3 == 1 ? Ivory : Gold;
                color.a = Mathf.Clamp01(progress * 12f) * (1f - Mathf.SmoothStep(0f, 1f, progress));
                particle.color = color;
                particle.enabled = true;
            }
        }

        private void ResetStamp()
        {
            stamp.rectTransform.localScale = Vector3.one;
            stamp.rectTransform.localRotation = Quaternion.identity;
            stamp.rectTransform.anchoredPosition = Vector2.zero;
            ribbon.rectTransform.localScale = Vector3.one;
            HideParticles(stampBurst);
        }

        private void ResetSoldEffects()
        {
            soldEffectsStarted = double.NegativeInfinity;
            ResetStamp();
            HideParticles(confetti);
        }

        private static void HideParticles(Image[] particles)
        {
            if (particles == null)
                return;
            foreach (Image particle in particles)
                particle.enabled = false;
        }

        private TMP_Text Label(string name, RectTransform parent, Vector2 min, Vector2 max, float size, Color color)
        {
            var label = Box(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font ? font : TMP_Settings.defaultFontAsset;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = size * 0.5f;
            label.fontSizeMax = size;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.richText = false;
            label.raycastTarget = false;
            return label;
        }

        private static Image Fill(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform Box(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
