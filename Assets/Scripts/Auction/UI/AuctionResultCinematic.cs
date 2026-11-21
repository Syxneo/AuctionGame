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

        private static readonly Color Gold = new(0.91f, 0.75f, 0.49f);
        private static readonly Color Ivory = new(1f, 0.95f, 0.85f);
        private static readonly Color SoldRed = new(0.46f, 0.10f, 0.16f);
        private static readonly Color UnsoldBlue = new(0.19f, 0.25f, 0.32f);

        private Canvas overlay;
        private Button continueButton;
        private RectTransform itemBackdrop;
        private Image itemImage;
        private Image ribbon;
        private TMP_Text outcome;
        private TMP_Text winner;
        private TMP_Text price;
        private TMP_Text saleComment;
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
                saleComment.text = string.IsNullOrEmpty(sale.ResultLine) ? "" : "SALE • " + sale.ResultLine;
            }
            AuctionResult result = activeResult;
            itemImage.sprite = auction.CurrentItem.Sprite;
            itemImage.enabled = itemImage.sprite != null;
            bool showStamp = result.sold && soldStamp;
            ribbon.sprite = showStamp ? soldStamp : null;
            ribbon.preserveAspect = showStamp;
            ribbon.color = showStamp ? Color.white : result.sold ? SoldRed : UnsoldBlue;
            outcome.color = showStamp ? Color.clear : Ivory;
            itemBackdrop.anchorMin = new Vector2(0.026f, result.sold ? 0.235f : 0.025f);
            itemImage.rectTransform.anchorMin = new Vector2(0.09f, result.sold ? 0.26f : 0.13f);
            ribbon.rectTransform.anchorMin = new Vector2(0.055f, result.sold ? 0.32f : 0.19f);
            ribbon.rectTransform.anchorMax = new Vector2(0.945f, result.sold ? 0.48f : 0.35f);
            outcome.text = result.sold ? "PREDANÉ" : "NEPREDANÉ";
            winner.text = result.sold ? auction.GetBidderDisplayName(result.winnerId) : "";
            price.text = result.sold ? $"${result.finalPrice}" : "";
            winner.gameObject.SetActive(result.sold);
            price.gameObject.SetActive(result.sold);
        }

        private void FinishResult()
        {
            waitingForDialogue = false;
            ShowOverlay(false);
            RestoreHud();
            if (itemImage)
                itemImage.sprite = null;
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
            Label("Continue Prompt", safeArea, new Vector2(0.12f, 0.16f), new Vector2(0.88f, 0.24f),
                44, Ivory).text = "Klepni pre pokračovanie";
            if (sale)
            {
                saleComment = Label("SALE Commentary", safeArea, new Vector2(0.12f, 0.78f),
                    new Vector2(0.88f, 0.88f), 35, Ivory);
                saleComment.textWrappingMode = TextWrappingModes.Normal;
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

            RectTransform banner = Box("Result Ribbon", card, new Vector2(0.055f, 0.32f), new Vector2(0.945f, 0.48f));
            banner.localRotation = Quaternion.Euler(0, 0, 7);
            ribbon = Fill(banner, SoldRed);
            Fill(Box("Ribbon Top Trim", banner, new Vector2(0, 0.92f), Vector2.one), Gold);
            Fill(Box("Ribbon Bottom Trim", banner, Vector2.zero, new Vector2(1, 0.08f)), Gold);
            outcome = Label("Result Outcome", banner, new Vector2(0.08f, 0.1f), new Vector2(0.92f, 0.9f), 92, Ivory);
            outcome.fontStyle = FontStyles.Bold;
            outcome.characterSpacing = 10f;

            winner = Label("Result Winner", card, new Vector2(0.05f, 0.127f), new Vector2(0.95f, 0.225f), 49, Ivory);
            price = Label("Result Price", card, new Vector2(0.05f, 0.025f), new Vector2(0.95f, 0.132f), 71, Gold);
            price.fontStyle = FontStyles.Bold;
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
