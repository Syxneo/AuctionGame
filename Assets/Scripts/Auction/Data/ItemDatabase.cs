using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AuctionGame.Auction
{
    [CreateAssetMenu(menuName = "Auction/Item Database")]
    public sealed class ItemDatabase : ScriptableObject
    {
        [SerializeField] private AuctionItem[] items = Array.Empty<AuctionItem>();

        public IReadOnlyList<AuctionItem> Items => Array.AsReadOnly(items);

        public AuctionItem GetItemById(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            return items.FirstOrDefault(item => item && string.Equals(item.ItemId, itemId, StringComparison.Ordinal));
        }

        public IReadOnlyList<AuctionItem> GetVariantsByType(AuctionItemType itemType) =>
            itemType ? Array.AsReadOnly(items.Where(item => item && item.ItemType == itemType).ToArray())
                : Array.Empty<AuctionItem>();

        public IReadOnlyList<AuctionItem> GetItemsSharingCategories(AuctionItem currentItem)
        {
            if (!currentItem)
                return Array.Empty<AuctionItem>();

            var categories = new HashSet<ItemCategory>(currentItem.Categories);
            return Array.AsReadOnly(items.Where(item => item &&
                !string.Equals(item.ItemId, currentItem.ItemId, StringComparison.Ordinal) &&
                item.Categories.Any(categories.Contains)).ToArray());
        }
    }
}
