using UnityEngine;

namespace AuctionGame.Auction
{
    public enum ItemCategory { Historical, Rare, Art, Technology, Misc }

    [CreateAssetMenu(menuName = "Auction/Item")]
    public sealed class AuctionItem : ScriptableObject
    {
        [SerializeField] private string itemId;
        [SerializeField] private string itemName;
        [SerializeField, Min(1)] private int startingPrice = 1;
        [SerializeField, Min(0)] private int trueValue;
        [SerializeField] private ItemCategory category;
        [SerializeField] private Sprite sprite;

        public string ItemId => itemId;
        public string ItemName => itemName;
        public int StartingPrice => startingPrice;
        public int TrueValue => trueValue;
        public ItemCategory Category => category;
        public Sprite Sprite => sprite;
    }
}
