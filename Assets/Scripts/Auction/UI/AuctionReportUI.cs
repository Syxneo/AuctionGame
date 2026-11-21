using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    public sealed class AuctionReportUI : MonoBehaviour
    {
        [SerializeField] private AuctionManager auction;
        [SerializeField] private RectTransform auctionPanel;
        [SerializeField] private MvpAuctionLevel level;
        [SerializeField] private PreparationPhase preparation;
        [SerializeField] private GameIntroFlow endPresentation;
        [SerializeField] private Sprite reportParchment;
        [SerializeField] private Sprite reportOutline;
        [SerializeField] private Sprite reportButton;
        [SerializeField, TextArea] private string successDialogue =
            "Výborne. Dosiahol si požadovaný zisk. Zdá sa, že máš na obchod dobrý nos.";
        [SerializeField, TextArea] private string failureDialogue =
            "Nesplnil si požadovaný zisk. V tomto biznise sa za chyby platí.";
        [SerializeField, TextArea] private string emptyDialogue =
            "Nekúpil si nič. Bez rizika však nepríde ani zisk.";

        private static readonly Color Panel = new(0.10f, 0.15f, 0.20f);
        private static readonly Color Row = new(0.15f, 0.21f, 0.27f);
        private static readonly Color Accent = new(0.93f, 0.74f, 0.40f);
        private static readonly Color Gain = new(0.54f, 0.90f, 0.69f);
        private static readonly Color Loss = new(1f, 0.59f, 0.59f);
        private RectTransform view;
        private RectTransform endReportParent;
        private bool awaitingCall;
        private bool continuing;
        private TMP_Text continueError;

        public AuctionReport Report { get; private set; }

        private void OnEnable()
        {
            if (!auction || !auctionPanel || !level)
                return;

            auction.OnAuctionPhaseFinished += OpenReport;
            if (auction.State == AuctionState.Finished)
                OpenReport();
        }

        private void OnDisable()
        {
            if (auction)
                auction.OnAuctionPhaseFinished -= OpenReport;
        }

        private void OnDestroy() => RemoveView();

        internal void ClearCompletedCampaign()
        {
            RemoveView();
            Report = null;
            endReportParent = null;
            continuing = false;
            auctionPanel.gameObject.SetActive(true);
        }

        private void OpenReport()
        {
            if (Report != null || awaitingCall)
                return;

            var snapshot = new AuctionReport(auction.Economy.Purchases, preparation ? preparation.ProfitTarget : level.ProfitTarget);
            if (snapshot.Succeeded && preparation)
                preparation.RecordSuccess();
            auctionPanel.gameObject.SetActive(false);
            RemoveView();
            if (endPresentation && endPresentation.isActiveAndEnabled)
            {
                awaitingCall = true;
                endPresentation.ShowEndCall(snapshot.Succeeded, parent =>
                {
                    awaitingCall = false;
                    Report = snapshot;
                    endReportParent = parent;
                    CreatePortraitReport();
                });
                return;
            }

            Report = snapshot;
            CreateView("SALE Report");
            ButtonClickAudio.Find(gameObject.scene)?.ReportOpened(Report.Succeeded);
            Label(view, "PREDAJ", 28, 18, 110, 32, 24, Accent);
            Label(view, "Správa z dražby", 28, 50, 510, 50, 36, Color.white);
            Label(view, Report.Succeeded ? "ÚSPECH" : "KONIEC HRY", 640, 49, 228, 50, 30,
                Report.Succeeded ? Gain : Loss).alignment = TextAlignmentOptions.Right;

            string dialogue = Report.Purchases.Count == 0 ? emptyDialogue
                : Report.Succeeded ? successDialogue : failureDialogue;
            Label(view, dialogue, 28, 110, 840, 58, 22, Color.white).textWrappingMode = TextWrappingModes.Normal;

            if (Report.Purchases.Count > 0)
                DrawPurchases();
            else
                Label(view, "Žiadne nákupy", 28, 206, 840, 68, 24, Accent);

            Label(view, $"Celkový zisk  {Money(Report.TotalProfit)}", 28, 476, 310, 56, 26, ProfitColor(Report.TotalProfit));
            Label(view, $"Cieľový zisk  {Money(Report.TargetProfit)}", 352, 476, 290, 56, 23, Color.white);
            if (Report.Succeeded)
                DrawButton("Continue Button", "Pokračovať").onClick.AddListener(Continue);
            else
                DrawButton("Retry Button", "Zopakovať").onClick.AddListener(Retry);
        }

        private void CreatePortraitReport()
        {
            view = PreparationUi.Fill("SALE Report", endReportParent);
            // Let the parchment extend past the safe area while keeping the controls inside it.
            var backdrop = AddImage(PreparationUi.Fill("Report Parchment", endReportParent.parent), Color.white);
            backdrop.transform.SetAsFirstSibling();
            backdrop.sprite = reportParchment;
            backdrop.enabled = reportParchment;
            var frame = Anchor("Report Frame", view, .04f, .21f, .96f, .78f);
            AddImage(frame, reportOutline ? new Color(.95f, .95f, .95f) : PreparationUi.Ink);
            var paper = PreparationUi.Rect("Report Paper", frame, Vector2.zero, Vector2.one,
                new Vector2(12, 12), new Vector2(-12, -12));
            AddImage(paper, new Color(.95f, .95f, .95f));
            if (reportOutline)
            {
                var outline = AddImage(PreparationUi.Fill("Ink Outline", frame), Color.white);
                outline.sprite = reportOutline;
                outline.type = Image.Type.Sliced;
            }

            PortraitLabel(Anchor("Report Title", paper, .05f, .92f, .95f, 1),
                "SALE REPORT", 32, PreparationUi.Ink).fontStyle = FontStyles.Bold;
            PortraitColumns(Anchor("Report Columns", paper, .02f, .85f, .94f, .92f),
                "Item", "Your bid", "Worth", "Profit", PreparationUi.Ink, 23);
            var viewport = Anchor("Purchases", paper, .02f, .19f, .94f, .85f);
            if (Report.Purchases.Count == 0)
                PortraitLabel(PreparationUi.Fill("Empty State", viewport), "No items purchased.", 31, PreparationUi.Ink);
            else
                DrawPortraitItems(viewport, paper);

            PortraitTotal(paper, "Profit target", Money(Report.TargetProfit), .45f, .69f, PreparationUi.Ink);
            PortraitTotal(paper, "Your profit", SignedMoney(Report.TotalProfit), .71f, .97f,
                PortraitProfitColor(Report.TotalProfit));
            PortraitLabel(Anchor("Result", paper, .05f, .01f, .95f, .085f),
                Report.Succeeded ? "SUCCESS" : "GAME OVER", 34,
                Report.Succeeded ? PortraitProfitColor(1) : PortraitProfitColor(-1)).fontStyle = FontStyles.Bold;

            var logoRect = Anchor("Auction Frenzy Logo", view, .29f, .79f, .71f, .94f);
            if (endPresentation.ReportLogo)
            {
                var logoImage = AddImage(logoRect, Color.white);
                logoImage.sprite = endPresentation.ReportLogo;
                logoImage.preserveAspect = true;
            }
            else
                PortraitLabel(logoRect, "AUCTION FRENZY", 46, PreparationUi.Ink);

            var actionRect = Anchor(Report.Succeeded ? "Continue Button" : "Retry Button", view, .075f, .035f, .925f, .165f);
            var action = PreparationUi.Button(actionRect, Report.Succeeded ? "CONTINUE" : "TRY AGAIN");
            var background = (Image)action.targetGraphic;
            background.sprite = reportButton ? reportButton : endPresentation.ReportButton;
            background.color = background.sprite ? Color.white : Accent;
            background.preserveAspect = !reportButton && background.sprite;
            var text = action.GetComponentInChildren<TMP_Text>();
            text.fontSize = 82;
            text.fontStyle = FontStyles.Bold;
            text.color = PreparationUi.Ink;
            if (Report.Succeeded)
                action.onClick.AddListener(Continue);
            else
                action.onClick.AddListener(Retry);
        }

        private void DrawPortraitItems(RectTransform viewport, RectTransform paper)
        {
            const float height = 96;
            viewport.gameObject.AddComponent<RectMask2D>();
            AddImage(viewport, Color.clear, true);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            var content = PreparationUi.Rect("Items", viewport, Vector2.up, Vector2.one,
                Vector2.zero, Vector2.zero);
            content.pivot = Vector2.up;
            content.sizeDelta = new Vector2(0, Report.Purchases.Count * height);
            scroll.content = content;

            var track = Anchor("Purchase Scrollbar", paper, .955f, .19f, .98f, .85f);
            AddImage(track, new Color(.83f, .79f, .72f), true);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            var slidingArea = PreparationUi.Rect("Sliding Area", track, Vector2.zero, Vector2.one,
                new Vector2(3, 3), new Vector2(-3, -3));
            scrollbar.handleRect = PreparationUi.Fill("Handle", slidingArea);
            scrollbar.targetGraphic = AddImage(scrollbar.handleRect, new Color(.46f, .28f, .18f), true);
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            for (int index = 0; index < Report.Purchases.Count; index++)
            {
                ReportPurchase purchase = Report.Purchases[index];
                var row = PreparationUi.Rect($"Purchase {index + 1}", content, Vector2.up, Vector2.one,
                    new Vector2(0, -(index + 1) * height + 4), new Vector2(0, -index * height - 4));
                AddImage(Anchor("Item Tile", row, 0, .06f, .19f, .94f), Color.white);
                string itemLabel = purchase.Sprite ? "" : "—";
                PortraitColumns(row, itemLabel, Money(purchase.PurchasePrice), Money(purchase.TrueValue),
                    SignedMoney(purchase.Profit), PortraitProfitColor(purchase.Profit), 38);
                if (purchase.Sprite)
                {
                    var image = AddImage(Anchor("Item sprite", row, .01f, .10f, .18f, .90f), Color.white);
                    image.sprite = purchase.Sprite;
                    image.preserveAspect = true;
                }
            }

            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
        }

        private static void PortraitColumns(RectTransform row, string item, string bid, string worth, string profit,
            Color profitColor, int size)
        {
            var itemText = PortraitLabel(Anchor("Item", row, 0, 0, .19f, 1), item, size, PreparationUi.Ink);
            if (item == "Item")
                itemText.alignment = TextAlignmentOptions.MidlineLeft;
            PortraitLabel(Anchor("Your Bid", row, .23f, 0, .49f, 1), bid, size, PreparationUi.Ink).alignment =
                TextAlignmentOptions.MidlineRight;
            PortraitLabel(Anchor("Worth", row, .50f, 0, .72f, 1), worth, size, PreparationUi.Ink).alignment =
                TextAlignmentOptions.MidlineRight;
            PortraitLabel(Anchor("Profit", row, .73f, 0, 1, 1), profit, size, profitColor).alignment =
                TextAlignmentOptions.MidlineRight;
        }

        private static void PortraitTotal(RectTransform paper, string caption, string value, float left, float right,
            Color color)
        {
            PortraitLabel(Anchor(caption, paper, left, .145f, right, .18f), caption, 24, PreparationUi.Ink)
                .alignment = TextAlignmentOptions.MidlineRight;
            PortraitLabel(Anchor(caption + " Value", paper, left, .085f, right, .145f), value, 34, color)
                .alignment = TextAlignmentOptions.MidlineRight;
        }

        private static TMP_Text PortraitLabel(RectTransform rect, string value, int size, Color color)
        {
            var text = PreparationUi.Label(rect, value, size, color);
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 17;
            text.fontSizeMax = size;
            return text;
        }

        private static RectTransform Anchor(string name, Transform parent, float left, float bottom, float right, float top) =>
            PreparationUi.Rect(name, parent, new Vector2(left, bottom), new Vector2(right, top),
                Vector2.zero, Vector2.zero);

        private static Color PortraitProfitColor(long value) => value > 0 ? new Color(.01f, .43f, .21f) :
            value < 0 ? new Color(.75f, .04f, .08f) : PreparationUi.Ink;

        private static string SignedMoney(long value) => value > 0 ? $"+{Money(value)}" : Money(value);

        private void DrawPurchases()
        {
            Label(view, "Predmet", 28, 172, 324, 34, 18, Accent);
            Label(view, "Zaplatené", 364, 172, 110, 34, 18, Accent);
            Label(view, "Skutočná hodnota", 484, 172, 120, 34, 18, Accent);
            Label(view, "Zisk", 614, 172, 100, 34, 18, Accent);
            Label(view, "Výsledok", 724, 172, 134, 34, 18, Accent);

            var viewport = Box("Purchases", view, 28, 210, 840, 246);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            AddImage(viewport, Panel, true);
            scroll.content = Box("Items", viewport, 0, 0, 840, Report.Purchases.Count * 64f);
            for (int index = 0; index < Report.Purchases.Count; index++)
            {
                ReportPurchase purchase = Report.Purchases[index];
                var row = Box($"Purchase {index + 1}", scroll.content, 0, 64 * index, 840, 60);
                AddImage(row, Row);
                if (purchase.Sprite)
                {
                    var icon = AddImage(Box("Item sprite", row, 8, 6, 52, 48), Color.white);
                    icon.sprite = purchase.Sprite;
                    icon.preserveAspect = true;
                }

                int nameLeft = purchase.Sprite ? 72 : 12;
                Label(row, purchase.ItemName, nameLeft, 0, 324 - nameLeft, 60, 21, Color.white);
                Label(row, Money(purchase.PurchasePrice), 336, 0, 110, 60, 20, Color.white);
                Label(row, Money(purchase.TrueValue), 456, 0, 120, 60, 20, Color.white);
                Color profitColor = ProfitColor(purchase.Profit);
                Label(row, Money(purchase.Profit), 586, 0, 100, 60, 20, profitColor);
                string outcome = purchase.Outcome switch
                {
                    PurchaseOutcome.GoodBuy => "DOBRÝ NÁKUP",
                    PurchaseOutcome.Overpaid => "PREPLATENÉ",
                    _ => "BEZ ZISKU"
                };
                Label(row, outcome, 696, 0, 134, 60, 18, profitColor);
            }

            if (Report.Purchases.Count > 3)
            {
                var track = Box("Scroll track", view, 876, 210, 8, 246);
                AddImage(track, Row, true);
                var scrollbar = track.gameObject.AddComponent<Scrollbar>();
                var handle = Box("Handle", track, 0, 0, 8, 246);
                handle.anchorMin = Vector2.zero;
                handle.anchorMax = Vector2.one;
                handle.offsetMin = handle.offsetMax = Vector2.zero;
                scrollbar.targetGraphic = AddImage(handle, Accent, true);
                scrollbar.handleRect = handle;
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                scroll.verticalScrollbar = scrollbar;
            }
        }

        private void Retry()
        {
            if (Report == null || Report.Succeeded)
                return;

            AuctionReport previous = Report;
            Report = null;
            bool reset = preparation && preparation.isActiveAndEnabled
                ? auction.ResetLevel()
                : auction.RestartAuction();
            if (!reset)
            {
                Report = previous;
                return;
            }

            if (Report != null)
                return;

            RemoveView();
            endReportParent = null;
            auctionPanel.gameObject.SetActive(true);
        }

        private void Continue()
        {
            if (Report == null || !Report.Succeeded || continuing)
                return;

            if (preparation && preparation.CampaignDayCount > 0)
            {
                continuing = true;
                try
                {
                    if (!preparation.TryContinueCampaign(out string error))
                    {
                        if (error != null && endReportParent)
                        {
                            if (!continueError)
                                continueError = PortraitLabel(Anchor("Campaign Error", view, .08f, .003f, .92f, .065f),
                                    "", 18, Loss);
                            continueError.text = error;
                        }
                        return;
                    }

                    if (!preparation.CampaignCompleted)
                    {
                        Report = null;
                        auctionPanel.gameObject.SetActive(true);
                    }
                    RemoveView();
                    endReportParent = null;
                }
                finally
                {
                    // Keep the final report snapshot, even after leaving its presentation.
                    continuing = preparation.CampaignCompleted;
                }
                return;
            }

            RemoveView();
            if (endReportParent)
            {
                view = PreparationUi.Fill("Level Completion", endReportParent);
                PortraitLabel(Anchor("Completion", view, .08f, .45f, .92f, .72f),
                    $"Level complete\nFinal profit {Money(Report.TotalProfit)}", 44, PortraitProfitColor(1));
                return;
            }
            CreateView("Level Completion");
            Label(view, "PREDAJ", 28, 18, 110, 42, 24, Accent);
            Label(view, "Úroveň dokončená", 28, 70, 840, 60, 40, Gain);
            Label(view, $"Konečný zisk  {Money(Report.TotalProfit)}", 28, 144, 840, 56, 26, Color.white);
        }

        private void CreateView(string title)
        {
            view = Box(title, auctionPanel.parent, -450, -280, 900, 560);
            view.anchorMin = view.anchorMax = new Vector2(0.5f, 0.5f);
            AddImage(view, Panel, true);
        }

        private void RemoveView()
        {
            if (!view)
                return;

            view.gameObject.SetActive(false);
            Destroy(view.gameObject);
            view = null;
        }

        private Button DrawButton(string name, string caption)
        {
            var rect = Box(name, view, 678, 476, 190, 56);
            var background = AddImage(rect, Accent, true);
            var button = rect.gameObject.AddComponent<Button>();
            ButtonClickAudio.Bind(button);
            button.targetGraphic = background;
            Label(rect, caption, 0, 0, 190, 56, 23, Panel).alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static Color ProfitColor(long profit) => profit > 0 ? Gain : profit < 0 ? Loss : Color.white;
        private static string Money(long value) => value < 0 ? $"-${-value}" : $"${value}";

        private static RectTransform Box(string name, Transform parent, float left, float top, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static TMP_Text Label(Transform parent, string value, float x, float y, float width, float height,
            float size, Color color)
        {
            var label = Box(value, parent, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = value;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            label.richText = false;
            return label;
        }

        private static Image AddImage(RectTransform rect, Color color, bool raycastTarget = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }
    }
}
