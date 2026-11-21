using UnityEngine;

namespace AuctionGame.Auction
{
    public enum ItemCategory
    {
        Historical = 0, Rare = 1, Art = 2, Technology = 3, Misc = 4,
        Luxury = 5, Mechanical = 6, Military = 7, Jewelry = 8,
        Gaming = 9, Collectible = 10, Coins = 11,
        Silly = 12, Archeological = 13, PopCulture = 14, Fantasy = 15
    }

    [CreateAssetMenu(menuName = "Auction/Item")]
    public sealed class AuctionItem : ScriptableObject
    {
        [SerializeField] private string itemId;
        [SerializeField] private string itemName;
        [SerializeField, Min(-1)] private int trueValue;
        [SerializeField] private Sprite sprite;
        [SerializeField] private AuctionItemType itemType;

        public string ItemId => itemId;
        public string ItemName => itemName;
        public int TrueValue => trueValue;
        public Sprite Sprite => sprite;
        public AuctionItemType ItemType => itemType;
        public System.Collections.Generic.IReadOnlyList<ItemCategory> Categories =>
            itemType ? itemType.Categories : System.Array.Empty<ItemCategory>();
    }
}
