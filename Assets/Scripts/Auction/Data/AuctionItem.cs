using UnityEngine;

namespace AuctionGame.Auction
{
    public enum ItemCategory { Historical, Rare, Art, Technology, Misc }
    public enum ItemRole { Critical, Fluff }
    public enum ItemAuthenticity { Genuine, Fake }

    [CreateAssetMenu(menuName = "Auction/Item")]
    public sealed class AuctionItem : ScriptableObject
    {
        [SerializeField] private string itemId;
        [SerializeField] private string itemName;
        [SerializeField, Min(1)] private int startingPrice = 1;
        [SerializeField, Min(0)] private int trueValue;
        [SerializeField] private ItemCategory category;
        [SerializeField] private Sprite sprite;
        [SerializeField] private ItemRole itemRole;
        [SerializeField, Min(-1)] private int genuineTrueValue = -1;
        [SerializeField, Min(-1)] private int fakeTrueValue = -1;
        [SerializeField] private Sprite genuineSprite;
        [SerializeField] private Sprite fakeSprite;
        [SerializeField] private string newspaperClueKey;

        public string ItemId => itemId;
        public string ItemName => itemName;
        public int StartingPrice => startingPrice;
        public int TrueValue => trueValue;
        public ItemCategory Category => category;
        public Sprite Sprite => sprite;
        public ItemRole Role => itemRole;
        public int GenuineTrueValue => genuineTrueValue;
        public int FakeTrueValue => fakeTrueValue;
        public Sprite GenuineSprite => genuineSprite;
        public Sprite FakeSprite => fakeSprite;
        public string NewspaperClueKey => newspaperClueKey;
        public bool HasVariantPrices => StartingPrice > 0 && GenuineTrueValue >= 0 && FakeTrueValue >= 0;
    }
}
