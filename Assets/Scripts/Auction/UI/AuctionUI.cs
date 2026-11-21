using System.Text;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    public sealed class AuctionUI : MonoBehaviour
    {
        [SerializeField] private AuctionManager manager;
        [SerializeField] private Image itemImage;
        [SerializeField] private TMP_Text category;
        [SerializeField] private TMP_Text itemName;
        [SerializeField] private TMP_Text highestBid;
        [SerializeField] private TMP_Text highestBidder;
        [SerializeField] private TMP_Text opponentNames;
        [SerializeField] private TMP_Text timer;
        [SerializeField] private RectTransform timerProgress;
        [SerializeField] private TMP_Text budget;
        [SerializeField] private TMP_Text history;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Button tenButton;
        [SerializeField] private Button twentyFiveButton;
        [SerializeField] private Button fiftyButton;
        [SerializeField] private bool mobileHud;

        private static readonly Color Placeholder = new(0.15f, 0.24f, 0.30f);
        private TMP_Text tenCaption;
        private TMP_Text twentyFiveCaption;
        private TMP_Text fiftyCaption;

        private void Awake()
        {
            ButtonClickAudio.Bind(tenButton);
            ButtonClickAudio.Bind(twentyFiveButton);
            ButtonClickAudio.Bind(fiftyButton);
            if (!mobileHud)
                return;

            tenCaption = tenButton.GetComponentInChildren<TMP_Text>(true);
            twentyFiveCaption = twentyFiveButton.GetComponentInChildren<TMP_Text>(true);
            fiftyCaption = fiftyButton.GetComponentInChildren<TMP_Text>(true);
        }

        private void OnEnable()
        {
            if (!manager)
                return;

            manager.OnStateChanged += StateChanged;
            manager.OnBidPlaced += BidPlaced;
            manager.OnClockChanged += UpdateClock;
            tenButton.onClick.AddListener(BidTen);
            twentyFiveButton.onClick.AddListener(BidTwentyFive);
            fiftyButton.onClick.AddListener(BidFifty);
            Refresh();
        }

        private void OnDisable()
        {
            if (!manager)
                return;

            manager.OnStateChanged -= StateChanged;
            manager.OnBidPlaced -= BidPlaced;
            manager.OnClockChanged -= UpdateClock;
            tenButton.onClick.RemoveListener(BidTen);
            twentyFiveButton.onClick.RemoveListener(BidTwentyFive);
            fiftyButton.onClick.RemoveListener(BidFifty);
        }

        private void StateChanged(AuctionState state) => Refresh();
        private void BidPlaced(BidRecord bid) => Refresh();
        private void BidTen() => Bid(AuctionManager.MinimumOpeningBid);
        private void BidTwentyFive() => Bid(AuctionManager.MediumIncrement);
        private void BidFifty() => Bid(AuctionManager.HighIncrement);

        private void Bid(int increment)
        {
            if (manager.CurrentItem && ProposedAmount(increment, out int amount))
                manager.TryPlaceBid(BidderIds.Player, amount);
        }

        private bool ProposedAmount(int increment, out int amount)
        {
            long proposed = (long)manager.CurrentPrice + increment;
            amount = proposed <= int.MaxValue ? (int)proposed : 0;
            return amount > 0;
        }

        private void UpdateClock()
        {
            float remaining = manager.RemainingTime;
            if (timer)
                timer.SetText("{0:0}s", Mathf.CeilToInt(remaining));
            if (timerProgress)
                timerProgress.anchorMax = new Vector2(Mathf.Clamp01(remaining / AuctionManager.InitialBiddingTime), 0f);
        }

        private void Refresh()
        {
            AuctionItem item = manager.CurrentItem;
            Sprite sprite = item ? item.Sprite : null;
            if (itemImage)
            {
                itemImage.sprite = sprite;
                itemImage.color = sprite ? Color.white : Placeholder;
            }
            category.text = item ? string.Join(", ", item.Categories.Select(category => category.DisplayName())) : "";
            category.gameObject.SetActive(!sprite && category.text.Length > 0);
            if (itemName)
                itemName.text = item ? item.ItemName : "Dražba";
            string price = $"${manager.CurrentPrice}";
            if (highestBid)
                highestBid.text = !item ? "" : mobileHud ? price : $"Aktuálna ponuka  {price}";
            if (highestBidder)
                highestBidder.text = $"Vedúci dražiteľ  {manager.GetBidderDisplayName(manager.HighestBidderId)}";
            if (opponentNames)
                opponentNames.text = string.Join("   ·   ", manager.Opponents.Select(opponent => opponent.Personality.Name));
            if (budget)
                budget.text = $"Rozpočet  ${manager.Economy.AvailableBudget}";
            bool showBidButtons = item && manager.State != AuctionState.Opening;
            SetButton(tenButton, showBidButtons, AuctionManager.MinimumOpeningBid);
            SetButton(twentyFiveButton, showBidButtons, AuctionManager.MediumIncrement);
            SetButton(fiftyButton, showBidButtons, AuctionManager.HighIncrement);
            if (mobileHud)
            {
                string prefix = string.IsNullOrEmpty(manager.HighestBidderId) ? "$" : "+$";
                tenCaption.text = prefix + AuctionManager.MinimumOpeningBid;
                twentyFiveCaption.text = prefix + AuctionManager.MediumIncrement;
                fiftyCaption.text = prefix + AuctionManager.HighIncrement;
            }

            status.text = manager.State switch
            {
                AuctionState.ItemReveal => "Odhalenie predmetu",
                AuctionState.Bidding => "Dražba otvorená",
                AuctionState.Sold => $"Predaj: {manager.GetBidderDisplayName(manager.HighestBidderId)} za ${manager.CurrentPrice}",
                AuctionState.Unsold => "Nepredané",
                AuctionState.Finished => $"Dražba ukončená · {manager.Economy.Purchases.Count} {Plural(manager.Economy.Purchases.Count, "zakúpený", "zakúpené", "zakúpených")}",
                _ => "Čaká sa na začiatok"
            };

            if (!history)
            {
                UpdateClock();
                return;
            }

            var lines = new StringBuilder();
            var bids = manager.BidHistory;
            int historyLength = mobileHud ? 1 : 6;
            for (int index = bids.Count - 1; index >= Mathf.Max(0, bids.Count - historyLength); index--)
            {
                BidRecord bid = bids[index];
                lines.Append($"{manager.GetBidderDisplayName(bid.bidderId)}  ${bid.amount}");
                lines.AppendLine();
            }
            history.text = lines.Length == 0 ? "Zatiaľ žiadne ponuky" : lines.ToString().TrimEnd();
            UpdateClock();
        }

        private static string Plural(int count, string one, string few, string many) =>
            count == 1 ? one : count is >= 2 and <= 4 ? few : many;

        private void SetButton(Button button, bool visible, int increment)
        {
            button.gameObject.SetActive(visible);
            button.interactable = visible && ProposedAmount(increment, out int amount) &&
                                  manager.CanPlaceBid(BidderIds.Player, amount);
        }
    }
}
