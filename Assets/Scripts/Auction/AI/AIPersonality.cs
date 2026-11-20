using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuctionGame.Auction
{
    public enum AIBidIncrementOrder { SmallFirst, LargeFirst, RandomRotation }

    [CreateAssetMenu(menuName = "Auction/AI Personality")]
    public sealed class AIPersonality : ScriptableObject
    {
        [SerializeField] private string bidderName;
        [SerializeField, Min(0)] private int startingBudget = 600;
        [SerializeField, Range(0, 1)] private float evaluationAccuracy;
        [SerializeField, Range(0, 1)] private float overestimateProbability = 0.50f;
        [SerializeField, Range(0, 1)] private float aggressiveness;
        [SerializeField] private string[] interests = Array.Empty<string>();
        [SerializeField, Range(0, 1)] private float interestAggressivenessBonus = 0.10f;
        [SerializeField, Range(0, 1)] private float interestProbabilityBonus = 0.10f;
        [SerializeField, Min(0)] private float interestPriceBonus = 0.15f;
        [SerializeField] private string[] bidComments = Array.Empty<string>();
        [SerializeField, Min(0.01f)] private double minimumDecisionInterval = 1.5;
        [SerializeField, Min(0.01f)] private double maximumDecisionInterval = 3.0;
        [SerializeField] private AIBidIncrementOrder incrementOrder;

        public string Name => string.IsNullOrWhiteSpace(bidderName) ? name : bidderName;
        public int StartingBudget => Math.Max(0, startingBudget);
        public double EvaluationAccuracy => Mathf.Clamp01(evaluationAccuracy);
        public double OverestimateProbability => Mathf.Clamp01(overestimateProbability);
        public double Aggressiveness => Mathf.Clamp01(aggressiveness);
        public IReadOnlyList<string> Interests => Array.AsReadOnly(interests);
        public double InterestAggressivenessBonus => interestAggressivenessBonus;
        public double InterestProbabilityBonus => interestProbabilityBonus;
        public double InterestPriceBonus => interestPriceBonus;
        public IReadOnlyList<string> BidComments => bidComments;

        public double MinimumDecisionInterval => Math.Max(0.01, minimumDecisionInterval);
        public double MaximumDecisionInterval => Math.Max(MinimumDecisionInterval, maximumDecisionInterval);
        public AIBidIncrementOrder IncrementOrder => incrementOrder;
    }
}
