using System.Text;
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
        [SerializeField] private TMP_Text openingPrice;
        [SerializeField] private TMP_Text highestBid;
        [SerializeField] private TMP_Text highestBidder;
        [SerializeField] private TMP_Text timer;
        [SerializeField] private TMP_Text budget;
        [SerializeField] private TMP_Text history;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Button openingButton;
        [SerializeField] private TMP_Text openingButtonLabel;
        [SerializeField] private Button tenButton;
        [SerializeField] private Button twentyFiveButton;
        [SerializeField] private Button fiftyButton;

        private static readonly Color Placeholder = new(0.15f, 0.24f, 0.30f);

        private void OnEnable()
        {
            if (!manager)
                return;

            manager.OnStateChanged += StateChanged;
            manager.OnBidPlaced += BidPlaced;
            manager.OnClockChanged += UpdateClock;
            openingButton.onClick.AddListener(BidOpening);
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
            openingButton.onClick.RemoveListener(BidOpening);
            tenButton.onClick.RemoveListener(BidTen);
            twentyFiveButton.onClick.RemoveListener(BidTwentyFive);
            fiftyButton.onClick.RemoveListener(BidFifty);
        }

        private void StateChanged(AuctionState state) => Refresh();
        private void BidPlaced(BidRecord bid) => Refresh();
        private void BidOpening() => Bid(0);
        private void BidTen() => Bid(10);
        private void BidTwentyFive() => Bid(25);
        private void BidFifty() => Bid(50);

        private void Bid(int increment)
        {
            if (manager.CurrentItem && ProposedAmount(increment, out int amount))
                manager.TryPlaceBid(BidderIds.Player, amount);
        }

        private bool ProposedAmount(int increment, out int amount)
        {
            long proposed = increment == 0
                ? manager.CurrentItem.StartingPrice
                : (long)manager.CurrentPrice + increment;
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
            bool hasWinner = manager.HighestBidderId.Length > 0;
            itemImage.sprite = manager.CurrentItemVariant is { } variant ? variant.DisplaySprite : item ? item.Sprite : null;
            itemImage.color = itemImage.sprite ? Color.white : Placeholder;
            category.gameObject.SetActive(!itemImage.sprite);
            category.text = item ? item.Category.ToString() : "";
            itemName.text = item ? item.ItemName : "Auction";
            openingPrice.text = item ? $"Opening price  ${item.StartingPrice}" : "";
            highestBid.text = hasWinner ? $"Highest bid  ${manager.CurrentPrice}" : "Highest bid  —";
            highestBidder.text = $"Leading bidder  {BidderIds.DisplayName(manager.HighestBidderId)}";
            budget.text = $"Budget  ${manager.Economy.AvailableBudget}";
            openingButtonLabel.text = item ? $"BID ${item.StartingPrice}" : "";
            SetButton(openingButton, item && !hasWinner, 0);
            SetButton(tenButton, item && hasWinner, 10);
            SetButton(twentyFiveButton, item && hasWinner, 25);
            SetButton(fiftyButton, item && hasWinner, 50);

            status.text = manager.State switch
            {
                AuctionState.ItemReveal => "Item reveal",
                AuctionState.Bidding => "Bidding open",
                AuctionState.Sold => $"Sold to {BidderIds.DisplayName(manager.HighestBidderId)} for ${manager.CurrentPrice}",
                AuctionState.Unsold => "Unsold",
                AuctionState.Finished => $"Auction complete · {manager.Economy.Purchases.Count} purchased",
                _ => "Waiting to start"
            };

            var lines = new StringBuilder();
            var bids = manager.BidHistory;
            for (int index = bids.Count - 1; index >= Mathf.Max(0, bids.Count - 6); index--)
            {
                BidRecord bid = bids[index];
                lines.Append($"{BidderIds.DisplayName(bid.bidderId)}  ${bid.amount}");
                if (!string.IsNullOrEmpty(bid.comment))
                    lines.Append($" · {bid.comment}");
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
