using UnityEngine;

namespace AuctionGame.Auction
{
    public sealed class AuctionItemInstance
    {
        internal AuctionItemInstance(AuctionItem definition, ItemAuthenticity authenticity)
        {
            Definition = definition;
            Authenticity = authenticity;
            bool genuine = authenticity == ItemAuthenticity.Genuine;
            DisplaySprite = genuine ? definition.GenuineSprite : definition.FakeSprite;
            ActualTrueValue = genuine ? definition.GenuineTrueValue : definition.FakeTrueValue;
        }

        public AuctionItem Definition { get; }
        public ItemAuthenticity Authenticity { get; }
        public string ItemId => Definition.ItemId;
        public string ItemName => Definition.ItemName;
        public int StartingPrice => Definition.StartingPrice;
        public Sprite DisplaySprite { get; }
        public int ActualTrueValue { get; }

        // Both variants share the same basis for the existing personality estimates.
        public int AiReferenceValue =>
            (int)(((long)Definition.GenuineTrueValue + Definition.FakeTrueValue) / 2);
    }
}
