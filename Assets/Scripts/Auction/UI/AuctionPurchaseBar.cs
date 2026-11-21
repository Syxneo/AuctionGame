using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame.Auction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AuctionPurchaseBar : MonoBehaviour
    {
        private const int DefaultLayoutColumns = 6;

        [SerializeField] private AuctionManager auction;

        private readonly List<Image> icons = new();

        private void EnsureIcons(int count)
        {
            while (icons.Count < count)
            {
                var icon = new GameObject($"Purchase {icons.Count + 1}", typeof(Image)).GetComponent<Image>();
                icon.rectTransform.SetParent(transform, false);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                icons.Add(icon);
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
            int columns = Mathf.Max(DefaultLayoutColumns, purchases.Count);
            EnsureIcons(columns);
            float firstSlot = (columns - purchases.Count) * 0.5f;
            for (int index = 0; index < icons.Count; index++)
            {
                AuctionItem item = index < purchases.Count ? purchases[index].Variant : null;
                Image icon = icons[index];
                icon.sprite = item ? item.Sprite : null;
                icon.enabled = icon.sprite != null;
                if (!icon.enabled)
                    continue;

                RectTransform rect = icon.rectTransform;
                rect.anchorMin = new Vector2((firstSlot + index) / columns, 0f);
                rect.anchorMax = new Vector2((firstSlot + index + 1f) / columns, 1f);
                float inset = 6f * DefaultLayoutColumns / columns;
                rect.offsetMin = new Vector2(inset, 0f);
                rect.offsetMax = new Vector2(-inset, 0f);
            }
        }
    }
}
