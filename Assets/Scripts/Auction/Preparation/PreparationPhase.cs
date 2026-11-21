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
        private enum Stage { Auction, Intro, Brief, News, Ready, Opening }

        [SerializeField] private GameIntroFlow intro;
        [SerializeField] private SceneIntroOutro openingStage;
        [SerializeField] private MvpAuctionLevel level;
        [SerializeField] private bool useCampaign;
        [SerializeField] private bool useValueCappedGeneration;
        [SerializeField] private bool useFixedDailySeed;
        [SerializeField] private int fixedDailySeed;
        [SerializeField] private AuctionManager auction;
        [SerializeField] private CanvasGroup auctionControls;
        [SerializeField, TextArea(2, 5)] private string saleBrief =
            "Dobrý večer, agent. Máte {0}. Očakávam aspoň {1} zisku. Vyberajte opatrne.";
        [SerializeField] private int sortingOrder = 100;
        [SerializeField] private NewspaperView.Appearance newsAppearance = new();
        [SerializeField] private AudioClip newsIntroJingle;
        [SerializeField, TextArea(2, 5)] private string newsPresenterDialogue =
            "Vitajte pri dnešných správach! Trh je stabilný, ceny sú absurdné a zdravý rozum opäť klesol na historické minimum!";

        [SerializeField] private BroadcastEdition broadcastEdition = new();

        private CampaignProgress campaign;
        public int CurrentDay => campaign?.CurrentDay ?? 1;
        public int HighestUnlockedDay => campaign?.HighestUnlockedDay ?? 1;
        public int CampaignDayCount => campaign?.DayCount ?? 0;
        public bool CampaignCompleted => campaign?.Completed ?? false;
        public int StartingBudget => campaign?.Day.StartingBudget ?? level.StartingBudget;
        public int ProfitTarget => campaign?.Day.ProfitTarget ?? level.ProfitTarget;
        internal IReadOnlyList<string> SaleDialogue => campaign?.Day.SaleDialogue;
        public event Action OnCampaignCompletePresented;

        private BreakingNewsView broadcast;
        private IReadOnlyList<NewsCard> selectedNews;
        private IReadOnlyList<AuctionItem> fixedRun;
        internal IReadOnlyList<NewsCard> SelectedNews => selectedNews;

        private GameObject canvasRoot;
        private GameObject briefing;
        private Button readNews;
        private Button startAuction;
        private Button openingPrompt;
        private NewspaperView newspaper;
        private TMP_Text status;
        private Stage stage;
        private bool controlsCovered;
        private bool previousRaycasts;
        private bool previousInteraction;
        private float? coveredHudAlpha;
        public DailyRunPlan DailyPlan { get; private set; }

        private void Start()
        {
            if (!level || !auction)
            {
                Debug.LogError("Assign the MVP level and AuctionManager on Preparation Phase.", this);
                return;
            }

            BuildUi();
            try
            {
                if (useCampaign)
                {
                    if (!intro)
                        throw new InvalidOperationException("The campaign requires the game intro presentation.");
                    campaign = new CampaignProgress(level, !useFixedDailySeed);
                    if (CampaignCompleted)
                    {
                        ShowCampaignComplete();
                        return;
                    }

                    var plan = campaign.Open(NewDailySeed(HighestUnlockedDay));
                    ApplyCampaignDay(HighestUnlockedDay, plan, broadcastEdition.Select(plan, auction.NewsBidderPool));
                }
                else if (useValueCappedGeneration)
                {
                    int seed = useFixedDailySeed ? fixedDailySeed : new System.Random().Next();
                    DailyPlan = level.CreateDailyRun(seed);
                }

                if (intro && campaign == null)
                {
                    if (DailyPlan == null)
                        fixedRun = level.CreateRun();
                    selectedNews = DailyPlan != null
                        ? broadcastEdition.Select(DailyPlan, auction.NewsBidderPool)
                        : broadcastEdition.Select(fixedRun, level.GetNewspaperEntries(), auction.NewsBidderPool, fixedDailySeed);
                }
            }
            catch (InvalidOperationException exception)
            {
                ShowBrief();
                readNews.gameObject.SetActive(false);
                status.gameObject.SetActive(true);
                status.text = exception.Message;
                Debug.LogError(exception.Message, this);
                return;
            }
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

        internal void RecordSuccess() => campaign?.RecordSuccess();

        internal bool TryContinueCampaign(out string error)
        {
            error = null;
            if (campaign == null || auction.State != AuctionState.Finished)
                return false;

            if (CampaignCompleted)
            {
                ShowCampaignComplete();
                return true;
            }

            try
            {
                int nextDay = CurrentDay + 1;
                if (nextDay > HighestUnlockedDay)
                    return false;
                var plan = campaign.Generate(nextDay, NewDailySeed(nextDay), out _);
                var news = broadcastEdition.Select(plan, auction.NewsBidderPool);
                return auction.ResetLevel(() => ApplyCampaignDay(nextDay, plan, news));
            }
            catch (InvalidOperationException exception)
            {
                error = exception.Message;
                Debug.LogError(error, this);
                return false;
            }
        }

        private int NewDailySeed(int day) =>
            useFixedDailySeed ? unchecked(fixedDailySeed + day - 1) : new System.Random().Next();

        private void ApplyCampaignDay(int day, DailyRunPlan plan, IReadOnlyList<NewsCard> news)
        {
            campaign.Accept(day, plan);
            DailyPlan = plan;
            selectedNews = news;
            auction.CampaignDay = campaign.Day;
            auction.Economy.CampaignStartingBudget = StartingBudget;
        }

        private void ShowCampaignComplete()
        {
            CoverControls();
            intro.ShowCampaignComplete(sortingOrder + 1);
            OnCampaignCompletePresented?.Invoke();
        }

        private void BuildUi()
        {
            canvasRoot = new GameObject(intro ? "BreakingNewsCanvas" : "Preparation Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = intro ? new Vector2(720, 1280) : new Vector2(850, 560);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            Transform root = canvasRoot.transform;
            var cover = PreparationUi.Fill("Preparation", root);
            briefing = cover.gameObject;
            PreparationUi.Surface(cover, PreparationUi.Background);
            var center = new Vector2(0.5f, 0.5f);
            var card = PreparationUi.Rect(intro ? "Auction Ready" : "SALE briefing", cover, center, center,
                new Vector2(-330, -180), new Vector2(330, 180));
            PreparationUi.Surface(card, PreparationUi.Paper);

            string budget = Dollars(level.StartingBudget);
            string target = Dollars(level.ProfitTarget);
            PreparationUi.Label(CardRow("Title", card, -74, -22), "Brífing PREDAJ", 34,
                PreparationUi.Ink, FontStyles.Bold);
            PreparationUi.Label(CardRow("Brief", card, -172, -78), FormatBrief(budget, target),
                24, PreparationUi.Ink);
            PreparationUi.Label(CardRow("Starting budget", card, -202, -170),
                $"Počiatočný rozpočet  {budget}", 23, PreparationUi.Ink, FontStyles.Bold);
            PreparationUi.Label(CardRow("Profit target", card, -236, -204),
                $"Cieľový zisk  {target}", 23, PreparationUi.Ink, FontStyles.Bold);
            if (intro)
                foreach (Transform row in card)
                    row.gameObject.SetActive(false);
            status = PreparationUi.Label(CardRow("Status", card, -289, -241), "", 19, PreparationUi.Ink);

            readNews = PreparationUi.Button(PreparationUi.Rect("Read NEWSSSS", card, Vector2.right,
                Vector2.right, new Vector2(-246, 20), new Vector2(-28, 66)), "Čítaj NOVINKY");
            startAuction = PreparationUi.Button(PreparationUi.Rect("Start Auction", card, Vector2.right,
                Vector2.right, new Vector2(-246, 20), new Vector2(-28, 66)), "Spustiť dražbu");
            readNews.onClick.AddListener(ReadNews);
            startAuction.onClick.AddListener(RequestStart);

            if (intro)
            {
                var ready = (RectTransform)startAuction.transform;
                ready.anchorMin = new Vector2(.20f, .40f);
                ready.anchorMax = new Vector2(.80f, .60f);
                ready.offsetMin = ready.offsetMax = Vector2.zero;
                startAuction.GetComponentInChildren<TMP_Text>().fontSize = 32;
                broadcast = PreparationUi.Fill("Breaking News", root).gameObject.AddComponent<BreakingNewsView>();
                broadcast.Initialize(newsAppearance, intro.SpeechBubble, newsPresenterDialogue);
            }
            else
            {
                newspaper = PreparationUi.Fill("NEWSSSS presentation", root).gameObject.AddComponent<NewspaperView>();
                newspaper.Initialize(newsAppearance);
            }
            if (openingStage)
            {
                openingPrompt = PreparationUi.Button(PreparationUi.Fill("Start Auction", root), "");
                var hitArea = (RectTransform)openingPrompt.transform;
                openingPrompt.targetGraphic.color = Color.clear;
                openingPrompt.targetGraphic.canvasRenderer.cullTransparentMesh = false;
                openingPrompt.transition = Selectable.Transition.None;
                openingPrompt.onClick.AddListener(RequestStart);

                var paper = PreparationUi.Rect("Start Auction Parchment", hitArea,
                    new Vector2(.5f, .24f), new Vector2(.5f, .24f),
                    new Vector2(-210, -54), new Vector2(210, 54));
                Sprite sprite = intro ? intro.ReportButton : null;
                var image = PreparationUi.Surface(paper, sprite ? Color.white : PreparationUi.Paper);
                image.sprite = sprite;
                image.raycastTarget = false;
                PreparationUi.Label(PreparationUi.Fill("Caption", paper), "START AUCTION", 38,
                    PreparationUi.Ink, FontStyles.Bold).alignment = TextAlignmentOptions.Center;
                var hint = PreparationUi.Rect("Tap hint", paper, Vector2.zero, Vector2.right,
                    new Vector2(0, -44), new Vector2(0, -4));
                var hintLabel = PreparationUi.Label(hint, "klepni pre pokračovanie", 25, Color.white);
                hintLabel.alignment = TextAlignmentOptions.Center;
                hintLabel.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -2);
                openingPrompt.gameObject.SetActive(false);
            }
            canvasRoot.SetActive(false);
        }

        internal bool EnterNewsFromIntro()
        {
            if (stage != Stage.Intro || auction.State != AuctionState.Idle)
                return false;

            canvasRoot.SetActive(true);
            PresentNews();
            return true;
        }

        private void ReadNews()
        {
            if (stage != Stage.Brief || auction.State != AuctionState.Idle)
                return;

            PresentNews();
        }

        private void PresentNews()
        {
            stage = Stage.News;
            briefing.SetActive(false);
            if (broadcast)
            {
                broadcast.Present(selectedNews, () => intro.CurrentStage == GameIntroFlow.Stage.Completed, ShowReady);
                intro.PlayNewsJingle(newsIntroJingle);
            }
            else
                newspaper.Present(DailyPlan?.NewspaperEntries ?? level.GetNewspaperEntries(), level.Database,
                    DailyPlan?.Seed, ShowReady);
        }

        private void ShowReady()
        {
            if (stage != Stage.News || auction.State != AuctionState.Idle)
                return;

            if (openingStage && auction.Economy.AvailableBudget == StartingBudget && openingStage.PrepareOpening())
            {
                if (auction.PrepareOpening(DailyPlan?.Lots ?? fixedRun, level.Database, DailyPlan))
                {
                    if (auctionControls)
                    {
                        coveredHudAlpha = auctionControls.alpha;
                        auctionControls.alpha = 0f;
                    }
                    stage = Stage.Ready;
                    openingPrompt.gameObject.SetActive(true);
                    Select(openingPrompt.gameObject);
                    return;
                }
                openingStage.SkipToEnd();
            }

            stage = Stage.Ready;
            readNews.gameObject.SetActive(false);
            startAuction.gameObject.SetActive(true);
            briefing.SetActive(true);
            Select(startAuction.gameObject);
        }

        public void RequestStart()
        {
            if (stage != Stage.Ready)
                return;

            if (auction.State == AuctionState.Opening)
            {
                stage = Stage.Opening;
                openingPrompt.gameObject.SetActive(false);
                canvasRoot.SetActive(false);
                Select(null);
                if (!openingStage || !openingStage.PlayOpening(auction.CompleteOpening))
                    auction.CompleteOpening();
                return;
            }

            if (auction.State != AuctionState.Idle)
                return;

            if (auction.Economy.AvailableBudget != StartingBudget)
            {
                status.text = "Rozpočet dražby nezodpovedá dnešnému rozpočtu.";
                return;
            }

            if (useCampaign || useValueCappedGeneration)
            {
                if (!auction.StartDailyAuction(DailyPlan, level.Database))
                    status.text = "The auction could not start. Check the daily plan and catalog.";
                return;
            }

            IReadOnlyList<AuctionItem> run;
            try
            {
                run = fixedRun ?? level.CreateRun();
            }
            catch (InvalidOperationException exception)
            {
                status.gameObject.SetActive(true);
                status.text = exception.Message;
                return;
            }

            if (!auction.StartAuction(run, level.Database))
                status.text = "Dražbu sa nepodarilo spustiť. Skontrolujte beh MVP a katalóg.";
        }

        private void AuctionStateChanged(AuctionState state)
        {
            if (state == AuctionState.Opening)
                return;

            if (state == AuctionState.Idle)
            {
                if (intro)
                {
                    stage = Stage.Intro;
                    HideNews();
                    status.text = "";
                    briefing.SetActive(false);
                    CoverControls();
                    canvasRoot.SetActive(false);
                    Select(null);
                    intro.ShowStart(this, sortingOrder + 1);
                }
                else
                    ShowBrief();
            }
            else if (stage != Stage.Auction)
            {
                stage = Stage.Auction;
                HideNews();
                canvasRoot.SetActive(false);
                RestoreControls();
            }
        }

        private void HideNews()
        {
            if (broadcast)
                broadcast.Hide();
            if (newspaper)
                newspaper.Hide();
        }

        private void ShowBrief()
        {
            stage = Stage.Brief;
            HideNews();
            status.text = "";
            startAuction.gameObject.SetActive(false);
            readNews.gameObject.SetActive(true);
            briefing.SetActive(true);
            canvasRoot.SetActive(true);
            CoverControls();
            Select(readNews.gameObject);
        }

        private void CoverControls()
        {
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
            if (coveredHudAlpha.HasValue)
            {
                auctionControls.alpha = coveredHudAlpha.Value;
                coveredHudAlpha = null;
            }
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
