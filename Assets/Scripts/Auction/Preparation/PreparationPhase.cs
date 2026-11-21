using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    public sealed class PreparationPhase : MonoBehaviour
    {
        private enum Stage { Auction, Brief, News, Ready }

        [SerializeField] private MvpAuctionLevel level;
        [SerializeField] private AuctionManager auction;
        [SerializeField] private CanvasGroup auctionControls;
        [SerializeField, TextArea(2, 5)] private string saleBrief =
            "Good evening, agent. You have {0}. I expect at least {1} profit. Choose carefully.";
        [SerializeField] private int sortingOrder = 100;
        [SerializeField] private NewspaperView.Appearance newsAppearance = new();

        private GameObject canvasRoot;
        private GameObject briefing;
        private Button readNews;
        private Button startAuction;
        private NewspaperView newspaper;
        private TMP_Text status;
        private Stage stage;
        private bool controlsCovered;
        private bool previousRaycasts;
        private bool previousInteraction;

        private void Start()
        {
            if (!level || !auction)
            {
                Debug.LogError("Assign the MVP level and AuctionManager on Preparation Phase.", this);
                return;
            }

            BuildUi();
            auction.OnStateChanged += AuctionStateChanged;
            AuctionStateChanged(auction.State);
        }

        private void OnDestroy()
        {
            if (auction)
                auction.OnStateChanged -= AuctionStateChanged;
            RestoreControls();
            if (canvasRoot)
                Destroy(canvasRoot);
        }

        private void BuildUi()
        {
            canvasRoot = new GameObject("Preparation Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(850, 560);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            Transform root = canvasRoot.transform;
            var cover = PreparationUi.Fill("Preparation", root);
            briefing = cover.gameObject;
            PreparationUi.Surface(cover, PreparationUi.Background);
            var center = new Vector2(0.5f, 0.5f);
            var card = PreparationUi.Rect("SALE briefing", cover, center, center,
                new Vector2(-330, -180), new Vector2(330, 180));
            PreparationUi.Surface(card, PreparationUi.Paper);

            string budget = Dollars(level.StartingBudget);
            string target = Dollars(level.ProfitTarget);
            PreparationUi.Label(CardRow("Title", card, -74, -22), "SALE briefing", 34,
                PreparationUi.Ink, FontStyles.Bold);
            PreparationUi.Label(CardRow("Brief", card, -172, -78), FormatBrief(budget, target),
                24, PreparationUi.Ink);
            PreparationUi.Label(CardRow("Starting budget", card, -202, -170),
                $"Starting budget  {budget}", 23, PreparationUi.Ink, FontStyles.Bold);
            PreparationUi.Label(CardRow("Profit target", card, -236, -204),
                $"Profit target  {target}", 23, PreparationUi.Ink, FontStyles.Bold);
            status = PreparationUi.Label(CardRow("Status", card, -289, -241), "", 19, PreparationUi.Ink);

            readNews = PreparationUi.Button(PreparationUi.Rect("Read NEWSSSS", card, Vector2.right,
                Vector2.right, new Vector2(-246, 20), new Vector2(-28, 66)), "Read NEWSSSS");
            startAuction = PreparationUi.Button(PreparationUi.Rect("Start Auction", card, Vector2.right,
                Vector2.right, new Vector2(-246, 20), new Vector2(-28, 66)), "Start Auction");
            readNews.onClick.AddListener(ReadNews);
            startAuction.onClick.AddListener(RequestStart);

            newspaper = PreparationUi.Fill("NEWSSSS presentation", root).gameObject.AddComponent<NewspaperView>();
            newspaper.Initialize(newsAppearance);
            canvasRoot.SetActive(false);
        }

        private void ReadNews()
        {
            if (stage != Stage.Brief || auction.State != AuctionState.Idle)
                return;

            stage = Stage.News;
            briefing.SetActive(false);
            newspaper.Present(level, ShowReady);
        }

        private void ShowReady()
        {
            if (stage != Stage.News || auction.State != AuctionState.Idle)
                return;

            stage = Stage.Ready;
            readNews.gameObject.SetActive(false);
            startAuction.gameObject.SetActive(true);
            briefing.SetActive(true);
            Select(startAuction.gameObject);
        }

        public void RequestStart()
        {
            if (stage != Stage.Ready || auction.State != AuctionState.Idle)
                return;

            if (auction.Economy.AvailableBudget != level.StartingBudget)
            {
                status.text = "The auction budget does not match the MVP level.";
                return;
            }

            IReadOnlyList<AuctionItem> run;
            try
            {
                run = level.CreateRun();
            }
            catch (InvalidOperationException exception)
            {
                status.text = exception.Message;
                return;
            }

            if (!auction.StartAuction(run, level.Database))
                status.text = "The auction could not start. Check the MVP run and catalog.";
        }

        private void AuctionStateChanged(AuctionState state)
        {
            if (state == AuctionState.Idle)
            {
                ShowBrief();
            }
            else if (stage != Stage.Auction)
            {
                stage = Stage.Auction;
                newspaper.Hide();
                canvasRoot.SetActive(false);
                RestoreControls();
            }
        }

        private void ShowBrief()
        {
            stage = Stage.Brief;
            newspaper.Hide();
            status.text = "";
            startAuction.gameObject.SetActive(false);
            readNews.gameObject.SetActive(true);
            briefing.SetActive(true);
            canvasRoot.SetActive(true);
            if (auctionControls && !controlsCovered)
            {
                previousRaycasts = auctionControls.blocksRaycasts;
                previousInteraction = auctionControls.interactable;
                auctionControls.blocksRaycasts = false;
                auctionControls.interactable = false;
                controlsCovered = true;
            }
            Select(readNews.gameObject);
        }

        private void RestoreControls()
        {
            if (!controlsCovered || !auctionControls)
                return;

            auctionControls.blocksRaycasts = previousRaycasts;
            auctionControls.interactable = previousInteraction;
            controlsCovered = false;
        }

        private static void Select(GameObject control)
        {
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(control);
        }

        private string FormatBrief(string budget, string target)
        {
            try
            {
                return string.Format(saleBrief, budget, target);
            }
            catch (FormatException)
            {
                Debug.LogWarning("SALE brief contains invalid format placeholders.", this);
                return saleBrief;
            }
        }

        private static RectTransform CardRow(string name, RectTransform card, int bottom, int top) =>
            PreparationUi.Rect(name, card, Vector2.up, Vector2.one,
                new Vector2(28, bottom), new Vector2(-28, top));

        private static string Dollars(int value) =>
            "$" + value.ToString("N0", CultureInfo.GetCultureInfo("en-US"));
    }
}
