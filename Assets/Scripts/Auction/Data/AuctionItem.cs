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

    public static class ItemCategoryNames
    {
        public static string DisplayName(this ItemCategory category) => category switch
        {
            ItemCategory.Historical => "Historické",
            ItemCategory.Rare => "Vzácne",
            ItemCategory.Art => "Umenie",
            ItemCategory.Technology => "Technológie",
            ItemCategory.Misc => "Rôzne",
            ItemCategory.Luxury => "Luxusné",
            ItemCategory.Mechanical => "Mechanické",
            ItemCategory.Military => "Vojenské",
            ItemCategory.Jewelry => "Šperky",
            ItemCategory.Gaming => "Herné",
            ItemCategory.Collectible => "Zberateľské",
            ItemCategory.Coins => "Mince",
            ItemCategory.Silly => "Bláznivé",
            ItemCategory.Archeological => "Archeologické",
            ItemCategory.PopCulture => "Popkultúra",
            ItemCategory.Fantasy => "Fantastické",
            _ => category.ToString()
        };
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
