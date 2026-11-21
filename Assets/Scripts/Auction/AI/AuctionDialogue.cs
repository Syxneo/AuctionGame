using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace AuctionGame.Auction
{
    internal sealed class AuctionDialogue
    {
        [Serializable]
        private sealed class Library
        {
            public Profile[] profiles;
        }

        [Serializable]
        private sealed class Profile
        {
            public string id;
            public Group[] groups;
        }

        [Serializable]
        private sealed class Group
        {
            public string key;
            public string[] lines;
        }

        private sealed class Bag
        {
            private readonly string[] lines;
            private readonly int[] order;
            private int next;
            private string last;

            public Bag(string[] lines)
            {
                this.lines = lines;
                order = new int[lines.Length];
                next = order.Length;
            }

            public string Take(Random random)
            {
                if (next == order.Length)
                {
                    for (int index = 0; index < order.Length; index++)
                        order[index] = index;
                    for (int index = order.Length - 1; index > 0; index--)
                    {
                        int swap = random.Next(index + 1);
                        (order[index], order[swap]) = (order[swap], order[index]);
                    }

                    for (int index = 1; lines[order[0]] == last && index < order.Length; index++)
                    {
                        if (lines[order[index]] != last)
                            (order[0], order[index]) = (order[index], order[0]);
                    }
                    next = 0;
                }

                return last = lines[order[next++]];
            }
        }

        private enum Hint { Comfortable, Cautious, NearLimit, PriceLimit, BudgetLimit, BothLimits }

        private readonly Library library;
        private readonly Random random;
        private readonly double cooldown;
        private readonly AuctionManager auction;
        private readonly Dictionary<(string bidder, string category), Bag> bags = new();
        private readonly Dictionary<string, string> profileIds = new();
        private readonly Dictionary<string, Hint> lastHints = new();
        private readonly Dictionary<string, double> lastReactions = new();
        private readonly HashSet<string> withdrawals = new();
        private int lot;
        private bool active;
        private long lastBid;

        public AuctionDialogue(AuctionManager auction, TextAsset source, int seed, float cooldown)
        {
            this.auction = auction;
            this.cooldown = Math.Max(0, cooldown);
            random = new Random(seed);
            if (source)
            {
                try
                {
                    library = JsonUtility.FromJson<Library>(source.text);
                }
                catch (ArgumentException)
                {
                    Debug.LogWarning("AI dialogue library contains invalid JSON.", auction);
                }
            }
        }

        public void OpenLot(int number, IReadOnlyList<AIBidder> bidders)
        {
            if (lot == number)
                return;

            lot = number;
            active = true;
            lastBid = 0;
            lastHints.Clear();
            lastReactions.Clear();
            withdrawals.Clear();

            foreach (AIBidder bidder in bidders)
            {
                profileIds[bidder.Id] = bidder.Personality.DialogueProfileId;
                string interest = bidder.EffectiveInterest switch
                {
                    < 25 => "very-low",
                    < 50 => "low",
                    < 75 => "high",
                    _ => "very-high"
                };
                Emit(bidder.Id, bidder.Personality.Name, "OPENING", "interest." + interest, 0);
            }
        }

        public void AcceptedBid(BidRecord bid, IReadOnlyList<AIBidder> bidders)
        {
            if (!active || bid.sequence <= lastBid)
                return;
            lastBid = bid.sequence;

            int amount = auction.GetNextBidAmount();
            if (amount <= 0)
                return;

            AIBidder speaker = null;
            Hint selected = default;
            foreach (AIBidder bidder in bidders)
            {
                if (bidder.HasChickenedOut || bidder.Id == auction.HighestBidderId)
                    continue;

                Hint hint = Classify(bidder, auction.CurrentPrice, amount);
                // Compare the first observed bid with the opening-price state.
                if (!lastHints.TryGetValue(bidder.Id, out Hint previous))
                    previous = Classify(bidder, auction.CurrentItem.StartingPrice, auction.CurrentItem.StartingPrice);
                lastHints[bidder.Id] = hint;
                if (previous == hint || (lastReactions.TryGetValue(bidder.Id, out double last) &&
                    bid.biddingElapsed - last < cooldown) || FindLines(bidder.Id, ReactionKey(hint)) == null)
                    continue;

                if (speaker == null || Priority(hint) > Priority(selected))
                {
                    speaker = bidder;
                    selected = hint;
                }
            }

            if (speaker == null)
                return;

            lastReactions[speaker.Id] = bid.biddingElapsed;
            Emit(speaker.Id, speaker.Personality.Name, ReactionKey(selected), ReactionKey(selected), bid.biddingElapsed);
        }

        public void ChickenOut(AIBidder bidder, double elapsed)
        {
            if (active && bidder.HasChickenedOut && withdrawals.Add(bidder.Id))
                Emit(bidder.Id, bidder.Personality.Name, "WD-01", "WD-01", elapsed);
        }

        public void EndLot(AuctionResult result, AIBidder winner, double elapsed)
        {
            if (!active)
                return;
            active = false;

            if (winner != null)
            {
                double ratio = winner.StartingBudget > 0 ? (double)winner.AvailableBudget / winner.StartingBudget : 0;
                string group = ratio >= 0.75 ? "WIN-01" : ratio >= 0.40 ? "WIN-02" : ratio >= 0.15 ? "WIN-03" : "WIN-04";
                Emit(winner.Id, winner.Personality.Name, group, group, elapsed);
            }
            else
            {
                string group = result.sold ? "SALE-01" : "SALE-02";
                Emit("sale", "Dražobník", group, group, elapsed);
            }
        }

        private static Hint Classify(AIBidder bidder, int price, int next)
        {
            bool overEstimate = next > bidder.EstimatedValue;
            bool overBudget = next > bidder.AvailableBudget;
            if (overEstimate && overBudget)
                return Hint.BothLimits;
            if (overEstimate)
                return Hint.PriceLimit;
            if (overBudget)
                return Hint.BudgetLimit;

            double ratio = bidder.EstimatedValue > 0 ? (double)price / bidder.EstimatedValue : double.PositiveInfinity;
            return ratio <= 0.50 ? Hint.Comfortable : ratio <= 0.80 ? Hint.Cautious : Hint.NearLimit;
        }

        private static int Priority(Hint hint) => hint switch
        {
            Hint.PriceLimit or Hint.BudgetLimit or Hint.BothLimits => 2,
            Hint.NearLimit => 1,
            _ => 0
        };
        private static string ReactionKey(Hint hint) => hint switch
        {
            Hint.Comfortable => "BR-01",
            Hint.Cautious => "BR-02",
            Hint.NearLimit => "BR-03",
            Hint.PriceLimit => "BR-04",
            Hint.BudgetLimit => "BR-05",
            _ => "BR-04/05"
        };

        private string[] FindLines(string bidder, string key)
        {
            if (library?.profiles == null)
                return null;

            string profileId = profileIds.TryGetValue(bidder, out string configuredId) ? configuredId : bidder;
            foreach (Profile profile in library.profiles)
            {
                if (profile == null || profile.id != profileId || profile.groups == null)
                    continue;
                foreach (Group group in profile.groups)
                {
                    if (group != null && group.key == key && group.lines is { Length: > 0 })
                        return group.lines;
                }
            }

            return null;
        }

        private string Take(string bidder, string category)
        {
            if (!bags.TryGetValue((bidder, category), out Bag bag))
            {
                bag = new Bag(FindLines(bidder, category));
                bags.Add((bidder, category), bag);
            }
            return bag.Take(random);
        }

        private void Emit(string bidder, string speaker, string category, string key, double elapsed)
        {
            if (FindLines(bidder, key) != null)
                Write(speaker, category, elapsed, Take(bidder, key));
        }

        private void Write(string speaker, string category, double elapsed, string text)
        {
            int seconds = (int)elapsed;
            Debug.Log($"[AI Dialogue] [Lot {lot:00}] [{seconds / 60:00}:{seconds % 60:00}] [{speaker}] [{category}] „{text}“", auction);
        }
    }
}
