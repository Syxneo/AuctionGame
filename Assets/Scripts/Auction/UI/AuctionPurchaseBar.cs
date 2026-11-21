using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AuctionPurchaseBar : MonoBehaviour
    {
        private const int Capacity = 6;

        [SerializeField] private AuctionManager auction;

        private readonly Image[] icons = new Image[Capacity];

        private void Awake()
        {
            for (int index = 0; index < icons.Length; index++)
            {
                var icon = new GameObject($"Purchase {index + 1}", typeof(Image)).GetComponent<Image>();
                icon.rectTransform.SetParent(transform, false);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                icons[index] = icon;
            }
        }

        private void OnEnable()
        {
            if (!auction)
                return;

            auction.OnStateChanged += StateChanged;
            Refresh();
        }

        private void OnDisable()
        {
            if (auction)
                auction.OnStateChanged -= StateChanged;
        }

        private void StateChanged(AuctionState state)
        {
            if (state is AuctionState.Sold or AuctionState.Idle)
                Refresh();
        }

        private void Refresh()
        {
            var purchases = auction.Economy.Purchases;
            int visibleSlots = Mathf.Min(purchases.Count, Capacity);
            float firstSlot = (Capacity - visibleSlots) * 0.5f;
            for (int index = 0; index < icons.Length; index++)
            {
                AuctionItem item = index < visibleSlots ? purchases[index].Variant : null;
                Image icon = icons[index];
                icon.sprite = item ? item.Sprite : null;
                icon.enabled = icon.sprite != null;
                if (!icon.enabled)
                    continue;

                RectTransform rect = icon.rectTransform;
                rect.anchorMin = new Vector2((firstSlot + index) / Capacity, 0f);
                rect.anchorMax = new Vector2((firstSlot + index + 1f) / Capacity, 1f);
                rect.offsetMin = new Vector2(6f, 0f);
                rect.offsetMax = new Vector2(-6f, -24f);
            }
        }
    }
}
