using System;
using UnityEngine;
using Random = System.Random;

namespace AuctionGame.Auction
{
    internal static class AIEvaluation
    {
        public static int Estimate(int trueValue, int effectiveAccuracy, int overestimateBias, Random random)
        {
            float accuracy = effectiveAccuracy / 100f;
            float maxError = 1f - accuracy;
            double magnitude = Math.Pow(random.NextDouble(), Mathf.Max(0.1f, accuracy));
            double error = maxError * magnitude;
            bool overestimate = random.NextDouble() < overestimateBias / 100f;
            double estimatedValue = trueValue * (overestimate ? 1 + error : 1 - error);
            return (int)Math.Clamp(Math.Round(estimatedValue, MidpointRounding.AwayFromZero), 0, int.MaxValue);
        }
    }
}
