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
            timer.SetText("{0:0}s", Mathf.CeilToInt(manager.RemainingTime));
        }

        private void Refresh()
        {
            AuctionItem item = manager.CurrentItem;
            itemImage.sprite = item ? item.Sprite : null;
            itemImage.color = itemImage.sprite ? Color.white : Placeholder;
            category.text = item ? string.Join(", ", item.Categories) : "";
            category.gameObject.SetActive(!itemImage.sprite && category.text.Length > 0);
            itemName.text = item ? item.ItemName : "Auction";
            string price = $"${manager.CurrentPrice}";
            highestBid.text = !item ? "" : mobileHud ? price : $"Current bid  {price}";
            highestBidder.text = $"Leading bidder  {manager.GetBidderDisplayName(manager.HighestBidderId)}";
            if (opponentNames)
                opponentNames.text = string.Join("   ·   ", manager.Opponents.Select(opponent => opponent.Personality.Name));
            budget.text = $"Budget  ${manager.Economy.AvailableBudget}";
            SetButton(tenButton, item, AuctionManager.MinimumOpeningBid);
            SetButton(twentyFiveButton, item, AuctionManager.MediumIncrement);
            SetButton(fiftyButton, item, AuctionManager.HighIncrement);
            if (mobileHud)
            {
                string prefix = string.IsNullOrEmpty(manager.HighestBidderId) ? "$" : "+$";
                tenCaption.text = prefix + AuctionManager.MinimumOpeningBid;
                twentyFiveCaption.text = prefix + AuctionManager.MediumIncrement;
                fiftyCaption.text = prefix + AuctionManager.HighIncrement;
            }

            status.text = manager.State switch
            {
                AuctionState.ItemReveal => "Item reveal",
                AuctionState.Bidding => "Bidding open",
                AuctionState.Sold => $"Sold to {manager.GetBidderDisplayName(manager.HighestBidderId)} for ${manager.CurrentPrice}",
                AuctionState.Unsold => "Unsold",
                AuctionState.Finished => $"Auction complete · {manager.Economy.Purchases.Count} purchased",
                _ => "Waiting to start"
            };

            var lines = new StringBuilder();
            var bids = manager.BidHistory;
            int historyLength = mobileHud ? 1 : 6;
            for (int index = bids.Count - 1; index >= Mathf.Max(0, bids.Count - historyLength); index--)
            {
                BidRecord bid = bids[index];
                lines.Append($"{manager.GetBidderDisplayName(bid.bidderId)}  ${bid.amount}");
                lines.AppendLine();
            }
            history.text = lines.Length == 0 ? "No bids yet" : lines.ToString().TrimEnd();
            UpdateClock();
        }

        private void SetButton(Button button, bool visible, int increment)
        {
            button.gameObject.SetActive(visible);
            button.interactable = visible && ProposedAmount(increment, out int amount) &&
                                  manager.CanPlaceBid(BidderIds.Player, amount);
        }
    }
}
