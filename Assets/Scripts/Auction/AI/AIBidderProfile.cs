using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuctionGame.Auction
{
    public enum AIPersonality { Collector, Trickster, Analyst }

    [Serializable]
    internal sealed class AIBidderProfile
    {
        [SerializeField, Min(0)] private int startingBudget = 600;
        [SerializeField, Range(0, 1)] private float evaluationAccuracy;
        [SerializeField, Range(0, 1)] private float overestimateProbability = 0.50f;
        [SerializeField, Range(0, 1)] private float aggressiveness;
        [SerializeField] private string[] interests;
        [SerializeField, Range(0, 1)] private float interestAggressivenessBonus = 0.10f;
        [SerializeField, Range(0, 1)] private float interestProbabilityBonus = 0.10f;
        [SerializeField, Min(0)] private float interestPriceBonus = 0.15f;
        [SerializeField] private string[] bidComments = Array.Empty<string>();

        public int StartingBudget => Math.Max(0, startingBudget);
        public double EvaluationAccuracy => Mathf.Clamp01(evaluationAccuracy);
        public double OverestimateProbability => Mathf.Clamp01(overestimateProbability);
        public double Aggressiveness => Mathf.Clamp01(aggressiveness);
        public IReadOnlyList<string> Interests => Array.AsReadOnly(interests);
        public double InterestAggressivenessBonus => interestAggressivenessBonus;
        public double InterestProbabilityBonus => interestProbabilityBonus;
        public double InterestPriceBonus => interestPriceBonus;
        public IReadOnlyList<string> BidComments => bidComments;

        public AIBidderProfile(float accuracy, float aggression, params string[] categories)
        {
            evaluationAccuracy = accuracy;
            aggressiveness = aggression;
            interests = categories;
        }

        public static AIBidderProfile CreateTrickster() => new(0.35f, 0.70f, "Jewelry", "Art", "Luxury")
        {
            bidComments = new[] { "That’s my last bid. Probably.", "I wouldn’t go much higher if I were you." }
        };
    }
}
