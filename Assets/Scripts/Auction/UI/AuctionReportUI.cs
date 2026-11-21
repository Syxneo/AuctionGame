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

        private void OpenReport()
        {
            if (Report != null)
                return;

            Report = new AuctionReport(auction.Economy.Purchases, level.ProfitTarget);
            auctionPanel.gameObject.SetActive(false);
            RemoveView();
            CreateView("SALE Report");
            Label(view, "SALE", 28, 18, 110, 32, 24, Accent);
            Label(view, "Auction Report", 28, 50, 510, 50, 36, Color.white);
            Label(view, Report.Succeeded ? "SUCCESS" : "GAME OVER", 640, 49, 228, 50, 30,
                Report.Succeeded ? Gain : Loss).alignment = TextAlignmentOptions.Right;

            string dialogue = Report.Purchases.Count == 0 ? emptyDialogue
                : Report.Succeeded ? successDialogue : failureDialogue;
            Label(view, dialogue, 28, 110, 840, 58, 22, Color.white).textWrappingMode = TextWrappingModes.Normal;

            if (Report.Purchases.Count > 0)
                DrawPurchases();
            else
                Label(view, "No purchases", 28, 206, 840, 68, 24, Accent);

            Label(view, $"Total profit  {Money(Report.TotalProfit)}", 28, 476, 310, 56, 26, ProfitColor(Report.TotalProfit));
            Label(view, $"Profit target  {Money(Report.TargetProfit)}", 352, 476, 290, 56, 23, Color.white);
            if (Report.Succeeded)
                DrawButton("Continue").onClick.AddListener(Continue);
            else
                DrawButton("Retry").onClick.AddListener(Retry);
        }

        private void DrawPurchases()
        {
            Label(view, "Item", 28, 172, 324, 34, 18, Accent);
            Label(view, "Paid", 364, 172, 110, 34, 18, Accent);
            Label(view, "True value", 484, 172, 120, 34, 18, Accent);
            Label(view, "Profit", 614, 172, 100, 34, 18, Accent);
            Label(view, "Result", 724, 172, 134, 34, 18, Accent);

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
                    PurchaseOutcome.GoodBuy => "GOOD BUY",
                    PurchaseOutcome.Overpaid => "OVERPAID",
                    _ => "BREAK EVEN"
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
            auctionPanel.gameObject.SetActive(true);
        }

        private void Continue()
        {
            RemoveView();
            CreateView("Level Completion");
            Label(view, "SALE", 28, 18, 110, 42, 24, Accent);
            Label(view, "Level complete", 28, 70, 840, 60, 40, Gain);
            Label(view, $"Final profit  {Money(Report.TotalProfit)}", 28, 144, 840, 56, 26, Color.white);
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

        private Button DrawButton(string title)
        {
            var rect = Box(title + " Button", view, 678, 476, 190, 56);
            var background = AddImage(rect, Accent, true);
            var button = rect.gameObject.AddComponent<Button>();
            ButtonClickAudio.Bind(button);
            button.targetGraphic = background;
            Label(rect, title, 0, 0, 190, 56, 23, Panel).alignment = TextAlignmentOptions.Center;
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
