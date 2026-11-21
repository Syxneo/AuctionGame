using System.Collections.Generic;
using UnityEngine;

namespace AuctionGame.Auction
{
    public enum ItemRole { Unspecified, Critical, Fluff }

    [CreateAssetMenu(menuName = "Auction/Item Type")]
    public sealed class AuctionItemType : ScriptableObject
    {
        [SerializeField] private string typeId;
        [SerializeField] private string displayName;
        [SerializeField] private List<ItemCategory> categories = new();
        [SerializeField] private ItemRole role;
        [SerializeField] private Sprite newspaperSprite;

        public string TypeId => typeId;
        public string DisplayName => displayName;
        public IReadOnlyList<ItemCategory> Categories => categories.AsReadOnly();
        public ItemRole Role => role;
        public Sprite NewspaperSprite => newspaperSprite;
    }
}
