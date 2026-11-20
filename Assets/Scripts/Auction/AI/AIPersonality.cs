using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuctionGame.Auction
{
    [CreateAssetMenu(menuName = "Auction/AI Personality")]
    public sealed class AIPersonality : ScriptableObject
    {
        [Serializable]
        private struct CategoryProfile
        {
            public ItemCategory category;
            [Range(0, 100)] public int interest;
            [Range(0, 100)] public int accuracy;
        }

        [SerializeField] private string bidderName;
        [SerializeField, Min(0)] private int startingBudget = 600;
        [SerializeField, Range(0, 100)] private int overestimateBias = 50;
        [SerializeField, Range(0, 100)] private int intimidationResistance = 50;
        [SerializeField] private CategoryProfile[] categoryProfiles = Array.Empty<CategoryProfile>();
        [SerializeField] private string[] bidComments = Array.Empty<string>();

        public string Name => string.IsNullOrWhiteSpace(bidderName) ? name : bidderName;
        public int StartingBudget => Math.Max(0, startingBudget);
        public int OverestimateBias => Math.Clamp(overestimateBias, 0, 100);
        public int IntimidationResistance => Math.Clamp(intimidationResistance, 0, 100);
        public IReadOnlyList<string> BidComments => bidComments;

        internal (int interest, int accuracy) GetEffectiveProfile(IReadOnlyList<ItemCategory> categories)
        {
            // Missing categories contribute zero to each independent maximum.
            int interest = 0;
            int accuracy = 0;
            foreach (ItemCategory category in categories)
            {
                foreach (CategoryProfile profile in categoryProfiles)
                {
                    if (profile.category == category)
                    {
                        interest = Math.Max(interest, profile.interest);
                        accuracy = Math.Max(accuracy, profile.accuracy);
                    }
                }
            }

            return (Math.Clamp(interest, 0, 100), Math.Clamp(accuracy, 0, 100));
        }
    }
}
