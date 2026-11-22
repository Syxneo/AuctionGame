using System.Collections;
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
        [Header("Report Reveal")]
        [SerializeField] private bool enableProfitCountUp = true;
        [SerializeField, Min(0.01f)] private float profitCountUpDuration = 0.8f;
        [SerializeField] private bool enableReportStatusStamp = true;
        [SerializeField, Min(0.01f)] private float reportStampDuration = 0.35f;
        [SerializeField, Range(1f, 1.5f)] private float reportStampScale = 1.35f;

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
        private Coroutine reveal;
        private ScrollRect revealScroll;
        private CanvasGroup[] revealRows;
        private float profitBaseAlpha;
        private TMP_Text profitLabel;
        private TMP_Text statusLabel;
        private string finalProfitText;
        private Vector3 statusBaseScale;
        private Quaternion statusBaseRotation;
        private float statusBaseAlpha;

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
            CancelReveal();
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
                }, preparation ? preparation.SaleEnding(snapshot.Succeeded) : null);
                return;
            }

            Report = snapshot;
            CreateView("SALE Report");
            ButtonClickAudio.Find(gameObject.scene)?.ReportOpened(Report.Succeeded);
            Label(view, "ZADÁVATEĽ: SALE", 28, 18, 290, 32, 24, Accent);
            Label(view, $"DEŇ {ReportDay}", 722, 18, 146, 32, 24, Accent).alignment = TextAlignmentOptions.Right;
            Label(view, "DENNÉ VYÚČTOVANIE", 28, 50, 510, 50, 36, Color.white);
            Label(view, Report.Succeeded ? "ÚLOHA SPLNENÁ" : "CIEĽ NESPLNENÝ", 540, 49, 328, 50, 30,
                Report.Succeeded ? Gain : Loss).alignment = TextAlignmentOptions.Right;

            string dialogue = Report.Purchases.Count == 0 ? emptyDialogue
                : Report.Succeeded ? successDialogue : failureDialogue;
            Label(view, dialogue, 28, 110, 840, 58, 22, Color.white).textWrappingMode = TextWrappingModes.Normal;

            if (Report.Purchases.Count > 0)
                DrawPurchases();
            else
                Label(view, "Žiadne nákupy", 28, 206, 840, 68, 24, Accent);

            Label(view, $"CELKOVÝ ZISK  {Money(Report.TotalProfit)}", 28, 476, 310, 56, 26, ProfitColor(Report.TotalProfit));
            Label(view, $"CIEĽOVÝ ZISK  {Money(Report.TargetProfit)}", 352, 476, 290, 56, 23, Color.white);
            if (Report.Succeeded)
                DrawButton("Continue Button", "POKRAČOVAŤ").onClick.AddListener(Continue);
            else
                DrawButton("Retry Button", "SKÚSIŤ ZNOVA").onClick.AddListener(Retry);
        }

        private int ReportDay => preparation ? preparation.CurrentDay : 1;

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
                "DENNÉ VYÚČTOVANIE", 32, PreparationUi.Ink).fontStyle = FontStyles.Bold;
            PortraitColumns(Anchor("Report Columns", paper, .02f, .85f, .94f, .92f),
                "NAKÚPENÉ\nPREDMETY", "NÁKUPNÁ\nCENA", "SKUTOČNÁ\nHODNOTA", "ZISK / STRATA", PreparationUi.Ink, 21);
            var viewport = Anchor("Purchases", paper, .02f, .19f, .94f, .85f);
            ScrollRect purchaseScroll = null;
            CanvasGroup[] purchaseRows = null;
            if (Report.Purchases.Count == 0)
                PortraitLabel(PreparationUi.Fill("Empty State", viewport), "Žiadne nákupy", 31, PreparationUi.Ink);
            else
                purchaseScroll = DrawPortraitItems(viewport, paper, out purchaseRows);

            PortraitLabel(Anchor("Report Day", paper, .04f, .085f, .38f, .18f),
                $"DEŇ {ReportDay}", 27, PreparationUi.Ink);
            PortraitTotal(paper, "CIEĽOVÝ ZISK", Money(Report.TargetProfit), .36f, .64f, PreparationUi.Ink);
            var profit = PortraitTotal(paper, "CELKOVÝ ZISK", SignedMoney(Report.TotalProfit), .66f, .97f,
                PortraitProfitColor(Report.TotalProfit));
            var status = PortraitLabel(Anchor("Result", paper, .05f, .01f, .95f, .085f),
                Report.Succeeded ? "ÚLOHA SPLNENÁ" : "CIEĽ NESPLNENÝ", 34,
                Report.Succeeded ? PortraitProfitColor(1) : PortraitProfitColor(-1));
            status.fontStyle = FontStyles.Bold;
            if (preparation && !string.IsNullOrWhiteSpace(preparation.ReportNote))
                PortraitLabel(Anchor("Internal Report Note", view, .09f, .169f, .91f, .208f),
                    preparation.ReportNote, 20, PreparationUi.Ink);

            var logoRect = Anchor("Auction Frenzy Logo", view, .29f, .83f, .71f, .96f);
            if (endPresentation.ReportLogo)
            {
                var logoImage = AddImage(logoRect, Color.white);
                logoImage.sprite = endPresentation.ReportLogo;
                logoImage.preserveAspect = true;
            }
            else
                PortraitLabel(logoRect, "AUCTION FRENZY", 46, PreparationUi.Ink);
            PortraitLabel(Anchor("Report Sender", view, .08f, .78f, .92f, .83f),
                "ZADÁVATEĽ: SALE", 28, PreparationUi.Ink);

            var actionRect = Anchor(Report.Succeeded ? "Continue Button" : "Retry Button", view, .075f, .035f, .925f, .165f);
            var action = PreparationUi.Button(actionRect, Report.Succeeded ? "POKRAČOVAŤ" : "SKÚSIŤ ZNOVA");
            var background = (Image)action.targetGraphic;
            background.sprite = reportButton ? reportButton : endPresentation.ReportButton;
            background.color = background.sprite ? Color.white : Accent;
            background.preserveAspect = !reportButton && background.sprite;
            var text = action.GetComponentInChildren<TMP_Text>();
            text.enableAutoSizing = true;
            text.fontSizeMin = 42;
            text.fontSizeMax = 82;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontStyle = FontStyles.Bold;
            text.color = PreparationUi.Ink;
            if (Report.Succeeded)
                action.onClick.AddListener(Continue);
            else
                action.onClick.AddListener(Retry);
            StartReveal(profit, status, purchaseScroll, purchaseRows);
        }

        private void StartReveal(TMP_Text profit, TMP_Text status, ScrollRect scroll, CanvasGroup[] rows)
        {
            if (!isActiveAndEnabled || (rows == null && !enableProfitCountUp && !enableReportStatusStamp))
                return;

            CancelReveal();
            revealScroll = scroll;
            revealRows = rows;
            profitLabel = profit;
            profitBaseAlpha = profit.alpha;
            statusLabel = status;
            finalProfitText = profit.text;
            statusBaseScale = status.rectTransform.localScale;
            statusBaseRotation = status.rectTransform.localRotation;
            statusBaseAlpha = status.alpha;
            statusLabel.alpha = 0f;
            profitLabel.alpha = 0f;
            if (revealRows != null)
                foreach (var row in revealRows)
                    row.alpha = 0f;
            reveal = StartCoroutine(RevealReport(Report.TotalProfit));
        }

        private IEnumerator RevealReport(long totalProfit)
        {
            while (endPresentation && endPresentation.IsFading)
                yield return null;
            if (revealRows != null)
                yield return RevealPurchases();

            profitLabel.alpha = profitBaseAlpha;
            if (enableProfitCountUp)
            {
                double started = Time.unscaledTimeAsDouble;
                float duration = Mathf.Max(0.01f, profitCountUpDuration);
                long displayed = totalProfit;
                for (float elapsed = 0f; elapsed < duration; elapsed = (float)(Time.unscaledTimeAsDouble - started))
                {
                    if (!profitLabel || !statusLabel)
                        yield break;
                    float progress = 1f - elapsed / duration;
                    long amount = (long)System.Math.Round(totalProfit * (1m - (decimal)(progress * progress)));
                    if (amount != displayed || elapsed == 0f)
                    {
                        profitLabel.text = SignedMoney(amount);
                        displayed = amount;
                    }
                    yield return null;
                }
            }

            if (profitLabel)
                profitLabel.text = finalProfitText;
            if (enableReportStatusStamp)
            {
                double started = Time.unscaledTimeAsDouble;
                float duration = Mathf.Max(0.01f, reportStampDuration);
                for (float elapsed = 0f; elapsed < duration; elapsed = (float)(Time.unscaledTimeAsDouble - started))
                {
                    if (!statusLabel)
                        yield break;
                    float progress = elapsed / duration;
                    float scale = progress < 0.46f
                        ? Mathf.SmoothStep(reportStampScale, 0.94f, progress / 0.46f)
                        : Mathf.SmoothStep(0.94f, 1f, (progress - 0.46f) / 0.54f);
                    statusLabel.rectTransform.localScale = statusBaseScale * scale;
                    statusLabel.rectTransform.localRotation =
                        statusBaseRotation * Quaternion.Euler(0f, 0f, Mathf.SmoothStep(-6f, 0f, progress));
                    statusLabel.alpha = statusBaseAlpha * Mathf.SmoothStep(0f, 1f, progress / 0.46f);
                    yield return null;
                }
            }

            reveal = null;
            RestoreReveal();
        }

        private IEnumerator RevealPurchases()
        {
            const float fadeDuration = .20f;
            float stagger = Mathf.Min(.16f, 2f / revealRows.Length);
            float duration = (revealRows.Length - 1) * stagger + fadeDuration;
            double started = Time.unscaledTimeAsDouble;
            for (float elapsed = 0; elapsed < duration; elapsed = (float)(Time.unscaledTimeAsDouble - started))
            {
                for (int i = 0; i < revealRows.Length; i++)
                {
                    float progress = Mathf.SmoothStep(0f, 1f, (elapsed - i * stagger) / fadeDuration);
                    revealRows[i].alpha = progress;
                    revealRows[i].transform.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, progress);
                }

                float viewHeight = revealScroll.viewport.rect.height;
                float travel = revealScroll.content.rect.height - viewHeight;
                if (travel > 0f)
                {
                    float rowsShown = Mathf.Clamp(1f + elapsed / stagger, 1, revealRows.Length);
                    float revealedHeight = rowsShown * revealScroll.content.rect.height / revealRows.Length;
                    revealScroll.StopMovement();
                    revealScroll.verticalNormalizedPosition = 1f - Mathf.Clamp01((revealedHeight - viewHeight) / travel);
                }
                yield return null;
            }
            RestoreRows();
        }

        private void RestoreRows()
        {
            if (revealRows == null)
                return;
            foreach (var row in revealRows)
            {
                if (!row)
                    continue;
                row.alpha = 1f;
                row.transform.localScale = Vector3.one;
            }
            revealRows = null;
            revealScroll = null;
        }

        private void CancelReveal()
        {
            if (reveal != null)
                StopCoroutine(reveal);
            reveal = null;
            RestoreReveal();
        }

        private void RestoreReveal()
        {
            RestoreRows();
            if (profitLabel)
            {
                profitLabel.text = finalProfitText;
                profitLabel.alpha = profitBaseAlpha;
            }
            if (statusLabel)
            {
                statusLabel.rectTransform.localScale = statusBaseScale;
                statusLabel.rectTransform.localRotation = statusBaseRotation;
                statusLabel.alpha = statusBaseAlpha;
            }
            profitLabel = null;
            statusLabel = null;
            finalProfitText = null;
        }

        private ScrollRect DrawPortraitItems(RectTransform viewport, RectTransform paper, out CanvasGroup[] rows)
        {
            const float height = 112;
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

            rows = new CanvasGroup[Report.Purchases.Count];
            for (int index = 0; index < Report.Purchases.Count; index++)
            {
                ReportPurchase purchase = Report.Purchases[index];
                var row = PreparationUi.Rect($"Purchase {index + 1}", content, Vector2.up, Vector2.one,
                    new Vector2(0, -(index + 1) * height + 4), new Vector2(0, -index * height - 4));
                rows[index] = row.gameObject.AddComponent<CanvasGroup>();
                string itemLabel = purchase.Sprite ? "" : "—";
                PortraitColumns(row, itemLabel, Money(purchase.PurchasePrice), Money(purchase.TrueValue),
                    SignedMoney(purchase.Profit), PortraitProfitColor(purchase.Profit), 38);
                if (purchase.Sprite)
                {
                    var image = AddImage(Anchor("Item sprite", row, 0, 0, .22f, 1), Color.white);
                    image.sprite = purchase.Sprite;
                    image.preserveAspect = true;
                }
            }

            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1;
            return scroll;
        }

        private static void PortraitColumns(RectTransform row, string item, string bid, string worth, string profit,
            Color profitColor, int size)
        {
            var itemText = PortraitLabel(Anchor("Item", row, 0, 0, .22f, 1), item, size, PreparationUi.Ink);
            if (!string.IsNullOrEmpty(item))
                itemText.alignment = TextAlignmentOptions.MidlineLeft;
            PortraitLabel(Anchor("Your Bid", row, .23f, 0, .49f, 1), bid, size, PreparationUi.Ink).alignment =
                TextAlignmentOptions.MidlineRight;
            PortraitLabel(Anchor("Worth", row, .50f, 0, .72f, 1), worth, size, PreparationUi.Ink).alignment =
                TextAlignmentOptions.MidlineRight;
            PortraitLabel(Anchor("Profit", row, .73f, 0, 1, 1), profit, size, profitColor).alignment =
                TextAlignmentOptions.MidlineRight;
        }

        private static TMP_Text PortraitTotal(RectTransform paper, string caption, string value, float left, float right,
            Color color)
        {
            PortraitLabel(Anchor(caption, paper, left, .145f, right, .18f), caption, 24, PreparationUi.Ink)
                .alignment = TextAlignmentOptions.MidlineRight;
            var label = PortraitLabel(Anchor(caption + " Value", paper, left, .085f, right, .145f), value, 34, color);
            label.alignment = TextAlignmentOptions.MidlineRight;
            return label;
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
            const float viewportHeight = 246;
            const float rowHeight = 78;
            const float rowSpacing = 4;
            Label(view, "NAKÚPENÉ PREDMETY", 28, 172, 324, 34, 18, Accent);
            Label(view, "NÁKUPNÁ CENA", 364, 172, 110, 34, 16, Accent);
            Label(view, "SKUTOČNÁ HODNOTA", 484, 172, 120, 34, 14, Accent);
            Label(view, "ZISK / STRATA", 614, 172, 100, 34, 16, Accent);
            Label(view, "Výsledok", 724, 172, 134, 34, 18, Accent);

            var viewport = Box("Purchases", view, 28, 210, 840, viewportHeight);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            AddImage(viewport, Panel, true);
            float contentHeight = Report.Purchases.Count * (rowHeight + rowSpacing) - rowSpacing;
            scroll.content = Box("Items", viewport, 0, 0, 840, contentHeight);
            for (int index = 0; index < Report.Purchases.Count; index++)
            {
                ReportPurchase purchase = Report.Purchases[index];
                var row = Box($"Purchase {index + 1}", scroll.content, 0, index * (rowHeight + rowSpacing), 840, rowHeight);
                AddImage(row, Row);
                if (purchase.Sprite)
                {
                    var icon = AddImage(Box("Item sprite", row, 8, 5, 68, 68), Color.white);
                    icon.sprite = purchase.Sprite;
                    icon.preserveAspect = true;
                }

                int nameLeft = purchase.Sprite ? 88 : 12;
                Label(row, purchase.ItemName, nameLeft, 0, 324 - nameLeft, rowHeight, 21, Color.white);
                Label(row, Money(purchase.PurchasePrice), 336, 0, 110, rowHeight, 20, Color.white);
                Label(row, Money(purchase.TrueValue), 456, 0, 120, rowHeight, 20, Color.white);
                Color profitColor = ProfitColor(purchase.Profit);
                Label(row, Money(purchase.Profit), 586, 0, 100, rowHeight, 20, profitColor);
                string outcome = purchase.Outcome switch
                {
                    PurchaseOutcome.GoodBuy => "DOBRÝ NÁKUP",
                    PurchaseOutcome.Overpaid => "PREPLATENÉ",
                    _ => "BEZ ZISKU"
                };
                Label(row, outcome, 696, 0, 134, rowHeight, 18, profitColor);
            }

            if (contentHeight > viewportHeight)
            {
                var track = Box("Scroll track", view, 876, 210, 8, viewportHeight);
                AddImage(track, Row, true);
                var scrollbar = track.gameObject.AddComponent<Scrollbar>();
                var handle = Box("Handle", track, 0, 0, 8, viewportHeight);
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
                if (preparation.IsFinalCampaignDay)
                {
                    if (auction.State != AuctionState.Finished || !endReportParent ||
                        endPresentation.CurrentStage != GameIntroFlow.Stage.EndReport || endPresentation.IsFading)
                        return;

                    continuing = true;
                    RemoveView();
                    endReportParent = null;
                    preparation.ShowFinale(reportParchment);
                    return;
                }

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

                    Report = null;
                    auctionPanel.gameObject.SetActive(true);
                    RemoveView();
                    endReportParent = null;
                }
                finally
                {
                    continuing = false;
                }
                return;
            }

            RemoveView();
            if (endReportParent)
            {
                view = PreparationUi.Fill("Level Completion", endReportParent);
                PortraitLabel(Anchor("Completion", view, .08f, .45f, .92f, .72f),
                    $"Úroveň dokončená\nKonečný zisk {Money(Report.TotalProfit)}", 44, PortraitProfitColor(1));
                return;
            }
            CreateView("Level Completion");
            Label(view, "SALE", 28, 18, 110, 42, 24, Accent);
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
            CancelReveal();
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
            BidButtonPress.Bind(button);
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
