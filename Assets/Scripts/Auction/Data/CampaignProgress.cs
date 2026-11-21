using System;
using System.Linq;
using UnityEngine;

namespace AuctionGame.Auction
{
    internal sealed class CampaignProgress
    {
        internal const string SaveKey = "AuctionGame.Campaign.v1";

        [Serializable]
        private sealed class Save
        {
            public int currentDay = 1;
            public int highestUnlockedDay = 1;
            public bool completed;
            public int seed;
            public string[] lots;
        }

        private readonly bool persistent;
        private readonly MvpAuctionLevel level;
        private Save save;

        internal int CurrentDay => save.currentDay;
        internal int HighestUnlockedDay => save.highestUnlockedDay;
        internal bool Completed => save.completed;
        internal int DayCount => level.CampaignDays.Count;
        internal CampaignDay Day => level.CampaignDays[CurrentDay - 1];

        internal CampaignProgress(MvpAuctionLevel level, bool persistent)
        {
            this.level = level;
            this.persistent = persistent;
            if (DayCount != 5 || level.CampaignDays.Any(day => day == null))
                throw new InvalidOperationException("Assign all five campaign days to the MVP level.");

            try
            {
                save = persistent && PlayerPrefs.HasKey(SaveKey)
                    ? JsonUtility.FromJson<Save>(PlayerPrefs.GetString(SaveKey)) : new Save();
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException("The saved campaign progress could not be read.", exception);
            }
            if (save == null || CurrentDay < 1 || CurrentDay > HighestUnlockedDay ||
                HighestUnlockedDay > DayCount || (Completed && CurrentDay != DayCount))
                throw new InvalidOperationException("The saved campaign progress is invalid.");
        }

        internal DailyRunPlan Open(int seed)
        {
            if (HighestUnlockedDay > CurrentDay)
                return Generate(HighestUnlockedDay, seed, out _);
            if (save.lots is not { Length: > 0 })
                return Generate(CurrentDay, seed, out _);

            Day.Validate();
            level.ValidateDailyCatalog();
            return DailyItemGenerator.Restore(level.PossibleItems, Day, save.seed, save.lots, level.DailyArticles());
        }

        internal DailyRunPlan Generate(int day, int seed, out int attempts)
        {
            if (day < 1 || day > DayCount)
                throw new InvalidOperationException($"Campaign day {day} is out of range.");
            CampaignDay config = level.CampaignDays[day - 1];
            config.Validate();
            level.ValidateDailyCatalog();
            int limit = level.CampaignGenerationAttempts;
            if (limit < 1)
                throw new InvalidOperationException("Campaign generation requires at least one attempt.");

            var catalog = level.PossibleItems;
            var articles = level.DailyArticles();
            for (int attempt = 0; attempt < limit; attempt++)
            {
                int candidateSeed = unchecked(seed + attempt * (int)0x9E3779B9u);
                var plan = DailyItemGenerator.Generate(catalog, config.DailyValueLimit, candidateSeed,
                    articles, config.MinLots, config.MaxLots);
                if (config.Accepts(plan, catalog))
                {
                    attempts = attempt + 1;
                    return plan;
                }
            }

            throw new InvalidOperationException(
                $"Campaign Day {day}: no valid daily plan after {limit} attempts (initial seed {seed}).");
        }

        internal void Accept(int day, DailyRunPlan plan)
        {
            if ((day != CurrentDay && day != CurrentDay + 1) || day > HighestUnlockedDay || Completed)
                throw new InvalidOperationException($"Cannot open campaign day {day}.");
            save.currentDay = day;
            save.seed = plan.Seed;
            save.lots = plan.Lots.Select(item => item.ItemId).ToArray();
            Persist();
        }

        internal void Restart(DailyRunPlan openingPlan)
        {
            if (!Completed)
                throw new InvalidOperationException("Complete the campaign before restarting.");
            save = new Save();
            Accept(1, openingPlan);
        }

        internal void RecordSuccess()
        {
            if (CurrentDay == DayCount)
                save.completed = true;
            else
                save.highestUnlockedDay = Math.Max(HighestUnlockedDay, CurrentDay + 1);
            Persist();
        }

        private void Persist()
        {
            if (!persistent)
                return;
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(save));
            PlayerPrefs.Save();
        }
    }
}
