using System.Collections.Generic;
using UnityEngine;

namespace AuctionGame.Auction
{
    [CreateAssetMenu(menuName = "Auction/Item Type")]
    public sealed class AuctionItemType : ScriptableObject
    {
        [SerializeField] private string typeId;
        [SerializeField] private string displayName;
        [SerializeField] private List<ItemCategory> categories = new();

        public string TypeId => typeId;
        public string DisplayName => displayName;
        public IReadOnlyList<ItemCategory> Categories => categories.AsReadOnly();
    }
}
