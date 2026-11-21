using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace AuctionGame.Auction.Tests
{
    internal sealed class EconomyPlaytestReport
    {
        public const string LotFile = "economy-v1-lots.csv";
        public const string RunFile = "economy-v1-runs.csv";
        public const string SummaryFile = "economy-v1-summary.json";
        public const string ReportFile = "economy-v1-report.md";
        public const string LotHeader = "seed,scenario,lotIndex,itemId,itemRole,minimumOpeningBid,actualTrueValue,winner,winningPrice,sold,bidCount,biddingDurationSec,playerBudgetBefore,playerBudgetAfter,collectorBudgetBefore,collectorBudgetAfter,tricksterBudgetBefore,tricksterBudgetAfter,analystBudgetBefore,analystBudgetAfter,playerBought,itemProfit";
        public const string RunHeader = "seed,scenario,completedLots,playerPurchases,playerSpend,playerProfit,profitTarget,success,playerBudgetEnd,collectorBudgetEnd,tricksterBudgetEnd,analystBudgetEnd,runStatus,notes";
        public const int ScreeningTarget = 29;
        private const string PriceNote = "Winning prices are observed under each policy. Player bids change the auction, so SkipAll prices cannot establish what the player could have paid.";
        private const string Timing = "Call AuctionManager.Tick every 1/64 second of game time. After each tick, including the one opening bidding, check the current lot and submit at most one player bid. AI decisions and expiration are processed by the engine before this input sample.";
        private readonly EconomyPlaytest fixture;
        private readonly EconomyRun[] attempts;
        private readonly EconomyRun[] completed;
        private readonly Dictionary<EconomyPolicy, EconomyRun[]> byPolicy;
        private readonly string reproducibility;
        private readonly string gitRevision;

        public EconomyPlaytestReport(EconomyPlaytest fixture, EconomyRun[] attempts, string reproducibility, string gitRevision)
        {
            this.fixture = fixture;
            this.attempts = attempts;
            this.reproducibility = reproducibility;
            this.gitRevision = gitRevision;
            completed = attempts.Where(run => run.Completed).ToArray();
            byPolicy = EconomyPlaytest.Policies.ToDictionary(policy => policy,
                policy => completed.Where(run => run.Policy == policy).ToArray());
        }

        private int Target => fixture.ProfitTarget;
        private bool Succeeds(EconomyRun run) => run.Profit >= Target;
        private EconomyRun[] InformedRuns => completed.Where(run => run.Policy != EconomyPolicy.SkipAll).ToArray();
        private int[] ReachedSeeds => InformedRuns.Where(Succeeds).Select(run => run.Seed).Distinct().OrderBy(seed => seed).ToArray();

        internal static (int Spend, int Profit, int Count)[] OpeningSubsets(IReadOnlyList<AuctionItem> items)
        {
            var subsets = new List<(int, int, int)>();
            for (int mask = 0; mask < (1 << items.Count); mask++)
            {
                var selected = items.Where((item, index) => (mask & (1 << index)) != 0).ToArray();
                subsets.Add((selected.Length * AuctionManager.MinimumOpeningBid,
                    selected.Sum(item => item.TrueValue - AuctionManager.MinimumOpeningBid), selected.Length));
            }
            return subsets.ToArray();
        }

        public void Write(string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, LotFile), new[] { LotHeader }.Concat(completed.SelectMany(LotRows)));
            File.WriteAllLines(Path.Combine(directory, RunFile), new[] { RunHeader }.Concat(completed.Select(RunRow)));
            File.WriteAllText(Path.Combine(directory, SummaryFile), JsonConvert.SerializeObject(Summary(), Formatting.Indented));
            File.WriteAllText(Path.Combine(directory, ReportFile), Markdown());
        }

        private static IEnumerable<string> LotRows(EconomyRun run) =>
            run.Lots.Select((lot, index) => Csv(run.Seed, run.Policy, index + 1, lot.Item.ItemId, lot.Item.ItemType.Role,
                AuctionManager.MinimumOpeningBid, lot.Item.TrueValue, lot.Result.sold ? lot.Result.winnerId : null,
                lot.Result.sold ? (int?)lot.Result.finalPrice : null, lot.Result.sold, lot.Bids.Length,
                lot.Duration.ToString("0.000000", CultureInfo.InvariantCulture), lot.Before[0], lot.After[0],
                lot.Before[1], lot.After[1], lot.Before[2], lot.After[2], lot.Before[3], lot.After[3],
                lot.PlayerBought, lot.PlayerProfit));

        private string RunRow(EconomyRun run) =>
            Csv(run.Seed, run.Policy, run.Lots.Length, run.Purchases, run.Spend, run.Profit, Target, Succeeds(run),
                run.EndBalances[0], run.EndBalances[1], run.EndBalances[2], run.EndBalances[3], "Completed", "");

        internal static string Csv(params object[] fields) => string.Join(",", fields.Select(field =>
        {
            string value = field is bool boolean ? (boolean ? "true" : "false") : Convert.ToString(field, CultureInfo.InvariantCulture);
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
        }));

        private object Summary()
        {
            var subsets = OpeningSubsets(fixture.Items);
            return new
            {
                schemaVersion = "1.0",
                unityVersion = Application.unityVersion,
                gitRevision,
                generatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                fixtures = new { scene = EconomyPlaytest.ScenePath, level = EconomyPlaytest.LevelPath, catalog = EconomyPlaytest.DatabasePath },
                seeds = EconomyPlaytest.Seeds,
                rng = "Each run sets AuctionManager.randomSeed. The engine derives per-bidder evaluation, interest, comment, intimidation and reaction System.Random streams from it. Unity global RNG is unused.",
                playerBudget = fixture.Level.StartingBudget,
                profitTarget = Target,
                levelProfitTarget = fixture.Level.ProfitTarget,
                aiAuctionSettings = new { timing = fixture.TimingSettings, intimidation = fixture.IntimidationSettings },
                aiEvaluation = "The active AI applies category accuracy and overestimate bias to the current item's true value. The full catalog is passed to the auction manager and the evaluator has no catalog input.",
                ai = fixture.Personalities.Select((p, index) => new
                {
                    bidderId = EconomyPlaytest.BidderOrder[index + 1], asset = AssetDatabase.GetAssetPath(p),
                    name = p.Name, startingBudget = p.StartingBudget, overestimateBias = p.OverestimateBias,
                    intimidationResistance = p.IntimidationResistance, categoryProfiles = CategoryProfiles(p),
                    bidComments = p.BidComments
                }),
                catalogItemIds = fixture.Level.Database.Items.Select(item => item.ItemId),
                scenarioRules = new
                {
                    SkipAll = "No player bids",
                    InformedConservative = $"Skip lots whose true value is at most the minimum opening bid. Bid up to min(wallet, value minus reserve). Critical reserve {EconomyPlaytest.CriticalReserve}, Fluff reserve {EconomyPlaytest.FluffReserve}.",
                    InformedAggressive = "Skip lots whose true value is at most the minimum opening bid. Bid up to min(wallet, value minus one).",
                    increment = "Use the minimum $10 bid when uncontested and raise by $10 afterward. Skip while leading and continue after reaching the profit target.",
                    timing = Timing, stepSeconds = EconomyPlaytest.Step, revealSeconds = AuctionManager.RevealDuration,
                    initialBiddingSeconds = AuctionManager.InitialBiddingTime, extensionSeconds = AuctionManager.AntiSnipingTime,
                    biddingCapSeconds = AuctionManager.MaximumBiddingDuration
                },
                priceInterpretation = PriceNote,
                missingValues = "Unsold lot price and winner are blank in CSV. Empty statistical samples have null mean, median, min and max in JSON. Failed simulations are listed separately.",
                reproducibility,
                attemptedRuns = attempts.Length,
                completedRuns = completed.Length,
                completedLots = completed.Sum(run => run.Lots.Length),
                integrationErrors = attempts.Where(run => !run.Completed).Select(run =>
                    new { seed = run.Seed, scenario = run.Policy.ToString(), error = run.Error }),
                minimumOpeningBid = AuctionManager.MinimumOpeningBid,
                openingPrice = 0,
                minimumBidCosts = new
                {
                    totalCost = fixture.Items.Count * AuctionManager.MinimumOpeningBid, totalValue = fixture.Items.Sum(item => item.TrueValue),
                    buyAllProfit = subsets.Single(set => set.Count == fixture.Items.Count).Profit,
                    optimisticBudgetBound = subsets.Where(set => set.Spend <= fixture.Level.StartingBudget).Max(set => set.Profit),
                    twoPurchaseMax = subsets.Where(set => set.Count == 2).Max(set => set.Profit)
                },
                screeningTarget = ScreeningTarget,
                screeningThresholdMet = ReachedSeeds.Length >= ScreeningTarget,
                reachedSeeds = ReachedSeeds,
                unprovenSeeds = EconomyPlaytest.Seeds.Except(ReachedSeeds),
                runs = completed.Select(run => new
                {
                    seed = run.Seed, scenario = run.Policy.ToString(), completedLots = run.Lots.Length,
                    playerPurchases = run.Purchases, playerSpend = run.Spend, playerProfit = run.Profit, success = Succeeds(run),
                    endBalances = WalletMap(run.EndBalances), runStatus = "Completed"
                }),
                scenarios = byPolicy.Select(pair => new
                {
                    scenario = pair.Key.ToString(), completedRuns = pair.Value.Length,
                    successes = pair.Value.Count(Succeeds), successRate = pair.Value.Length == 0 ? (double?)null :
                        (double)pair.Value.Count(Succeeds) / pair.Value.Length,
                    runsByPurchaseCount = Enumerable.Range(0, fixture.Items.Count + 1)
                        .ToDictionary(count => count, count => pair.Value.Count(run => run.Purchases == count)),
                    successesByPurchaseCount = Enumerable.Range(0, fixture.Items.Count + 1)
                        .ToDictionary(count => count, count => pair.Value.Count(run => Succeeds(run) && run.Purchases == count)),
                    playerSpend = new Statistics(pair.Value.Select(run => (double)run.Spend)),
                    playerProfit = new Statistics(pair.Value.Select(run => (double)run.Profit)),
                    endBalances = EconomyPlaytest.BidderOrder.Select((bidder, i) => new
                    {
                        bidder, budget = new Statistics(pair.Value.Select(run => (double)run.EndBalances[i])),
                        spend = new Statistics(pair.Value.Select(run => (double)(run.Lots[0].Before[i] - run.EndBalances[i]))),
                        zeroWalletRuns = pair.Value.Count(run => run.EndBalances[i] == 0),
                        unableToAffordFinalOpening = pair.Value.Count(run => run.Lots.Last().Before[i] < AuctionManager.MinimumOpeningBid)
                    }),
                    walletBlockedPlayerLots = pair.Value.SelectMany(run => run.Lots).Count(lot => lot.WalletBlockedPlayer),
                    lotStatistics = fixture.Items.Select((item, i) => LotStatistics(pair.Value.Select(run => run.Lots[i]).ToArray(), item))
                })
            };
        }

        private static Dictionary<string, int> WalletMap(int[] balances) =>
            EconomyPlaytest.BidderOrder.Select((bidder, i) => (bidder, balance: balances[i]))
                .ToDictionary(entry => entry.bidder, entry => entry.balance);

        private static object[] CategoryProfiles(AIPersonality personality)
        {
            var profiles = new SerializedObject(personality).FindProperty("categoryProfiles");
            return Enumerable.Range(0, profiles.arraySize).Select(i =>
            {
                var profile = profiles.GetArrayElementAtIndex(i);
                return new
                {
                    category = ((ItemCategory)profile.FindPropertyRelative("category").intValue).ToString(),
                    interest = profile.FindPropertyRelative("interest").intValue,
                    accuracy = profile.FindPropertyRelative("accuracy").intValue
                };
            }).ToArray();
        }

        private static object LotStatistics(EconomyLot[] rows, AuctionItem item)
        {
            var sold = rows.Where(row => row.Result.sold).ToArray();
            return new
            {
                itemId = item.ItemId, sold = sold.Length, unsold = rows.Length - sold.Length,
                winners = EconomyPlaytest.BidderOrder.ToDictionary(id => id, id => sold.Count(lot => lot.Result.winnerId == id)),
                winningPrice = new Statistics(sold.Select(lot => (double)lot.Result.finalPrice)),
                bidCount = new Statistics(rows.Select(lot => (double)lot.Bids.Length)),
                durationSeconds = new Statistics(rows.Select(lot => lot.Duration)),
                aiWinsAboveTrueValue = sold.Count(lot => !lot.PlayerBought && lot.Result.finalPrice > item.TrueValue),
                aiByBidder = EconomyPlaytest.BidderOrder.Skip(1).Select((bidder, i) => new
                {
                    bidder,
                    estimates = new Statistics(rows.Select(lot => (double)lot.Estimates[i])),
                    effectiveInterest = new Statistics(rows.Select(lot => (double)lot.EffectiveInterests[i])),
                    effectiveAccuracy = new Statistics(rows.Select(lot => (double)lot.EffectiveAccuracies[i])),
                    usedLastBid = rows.Count(lot => lot.AiUsedLastBid[i]),
                    intimidatedOut = rows.Count(lot => lot.AiChickenedOut[i])
                })
            };
        }

        private string Markdown()
        {
            var md = new StringBuilder("# Automated Economy v1.0 — Zero Opening Price\n\n");
            void Line(string value = "") => md.AppendLine(value);
            int[] reached = ReachedSeeds;
            int[] unproven = EconomyPlaytest.Seeds.Except(reached).ToArray();
            Line($"Completed **{completed.Length}/{attempts.Length} runs** and **{completed.Sum(run => run.Lots.Length)} lot results**. " +
                $"Integration errors **{attempts.Length - completed.Length}**. Reproducibility **{reproducibility}**.");
            Line($"The informed policies demonstrated the ${Target} target in **{reached.Length}/{EconomyPlaytest.Seeds.Length} seeds**. " +
                $"Screening threshold **{ScreeningTarget}/{EconomyPlaytest.Seeds.Length}** was **{(reached.Length >= ScreeningTarget ? "met" : "missed")}**.");
            if (Target != fixture.Level.ProfitTarget)
                Line($"This batch applies ${Target} to the SALE report calculation. The serialized MVP target is ${fixture.Level.ProfitTarget}.");
            Line(unproven.Length == 0 ? "At least one policy reached the target in every seed."
                : $"Neither tested policy demonstrated a route to the target for seeds {string.Join(", ", unproven)}. Other bidding policies could still succeed.");
            if (attempts.Any(run => !run.Completed))
            {
                Line("The JSON describes integration errors for " +
                    string.Join(", ", attempts.Where(run => !run.Completed).Select(run => $"{run.Seed}/{run.Policy}")) + ".");
            }
            Line();
            Line("## Minimum opening bid bound");
            Line();
            var subsets = OpeningSubsets(fixture.Items);
            var favorable = fixture.Items.Where(item => item.TrueValue > AuctionManager.MinimumOpeningBid).ToArray();
            var triples = OpeningSubsets(favorable).Where(set => set.Count == 3).Select(set => set.Profit).ToArray();
            Line($"All six cost ${(fixture.Items.Count * AuctionManager.MinimumOpeningBid)} and are worth ${fixture.Items.Sum(item => item.TrueValue)} " +
                $"for ${subsets.Single(set => set.Count == fixture.Items.Count).Profit} profit. " +
                $"Exhaustively checking the {subsets.Length} opening-price subsets with the ${fixture.Level.StartingBudget} wallet gives an optimistic bound of " +
                $"${subsets.Where(set => set.Spend <= fixture.Level.StartingBudget).Max(set => set.Profit)} (the {favorable.Length} positive lots, costing ${(favorable.Length * AuctionManager.MinimumOpeningBid)}). " +
                $"Best single and pair yield ${subsets.Where(set => set.Count == 1).Max(set => set.Profit)} and ${subsets.Where(set => set.Count == 2).Max(set => set.Profit)}. " +
                $"The three-positive-lot combinations yield ${triples.Min()}–${triples.Max()} at opening.");
            Line();
            Line("## Results by policy");
            Line();
            Line("| Policy | Completed | Reached target | Profit min/median/max | Median spend | Target with 1 / 2 / 3 / 4+ purchases |");
            Line("| --- | ---: | ---: | ---: | ---: | ---: |");
            foreach (var pair in byPolicy)
            {
                var runs = pair.Value;
                var wins = runs.Where(Succeeds).ToArray();
                var profits = new Statistics(runs.Select(run => (double)run.Profit));
                var spend = new Statistics(runs.Select(run => (double)run.Spend));
                var rate = runs.Length == 0 ? "—" : ((double)wins.Length / runs.Length).ToString("0.0%", CultureInfo.InvariantCulture);
                Line($"| {pair.Key} | {runs.Length} | {wins.Length}/{runs.Length} ({rate}) | {Number(profits.min)}/{Number(profits.median)}/{Number(profits.max)} | {Number(spend.median)} | {wins.Count(run => run.Purchases == 1)} / {wins.Count(run => run.Purchases == 2)} / {wins.Count(run => run.Purchases == 3)} / {wins.Count(run => run.Purchases >= 4)} |");
            }
            Line();
            var qualifyingSubsets = subsets.Where(set => set.Spend <= fixture.Level.StartingBudget && set.Profit >= Target).ToArray();
            if (qualifyingSubsets.Length > 0)
                Line($"The fewest purchases needed to reach the target at opening prices is {qualifyingSubsets.Min(set => set.Count)}.");
            else
                Line("No affordable opening-price subset reaches the target.");
            Line();
            Line(PriceNote);
            Line("P / C / T / A stand for Player, Collector, Trickster and Analyst.");
            Line();
            Line("| Lot | Policy | Sold / unsold | Winner counts P / C / T / A | Winning price min / median / max | Mean |");
            Line("| --- | --- | ---: | ---: | ---: | ---: |");
            for (int i = 0; i < fixture.Items.Count; i++)
            {
                foreach (var pair in byPolicy)
                {
                    var rows = pair.Value.Select(run => run.Lots[i]).ToArray();
                    var sold = rows.Where(lot => lot.Result.sold).ToArray();
                    var prices = new Statistics(sold.Select(lot => (double)lot.Result.finalPrice));
                    var winners = EconomyPlaytest.BidderOrder.Select(id => sold.Count(lot => lot.Result.winnerId == id));
                    Line($"| {fixture.Items[i].ItemId} | {pair.Key} | {sold.Length} / {rows.Length - sold.Length} | {string.Join(" / ", winners)} | {Number(prices.min)} / {Number(prices.median)} / {Number(prices.max)} | {Number(prices.mean)} |");
                }
            }
            Line();
            Line("## Wallet pressure, traps and AI decisions");
            Line();
            Line("| Policy | Median closing wallet P / C / T / A | AI wallets below $10 before lot 6 C / T / A | Player wallet blocks (lots) | AI trap buys | Player trap buys |");
            Line("| --- | ---: | ---: | ---: | ---: | ---: |");
            foreach (var pair in byPolicy)
            {
                var rows = pair.Value.SelectMany(run => run.Lots).ToArray();
                var balances = Enumerable.Range(0, 4).Select(i =>
                    Number(new Statistics(pair.Value.Select(run => (double)run.EndBalances[i])).median));
                var spentDown = Enumerable.Range(1, 3).Select(i =>
                    pair.Value.Count(run => run.Lots.Last().Before[i] < AuctionManager.MinimumOpeningBid));
                var traps = rows.Where(lot => lot.Result.sold && lot.Item.TrueValue < AuctionManager.MinimumOpeningBid).ToArray();
                Line($"| {pair.Key} | {string.Join(" / ", balances)} | {string.Join(" / ", spentDown)} | {rows.Count(lot => lot.WalletBlockedPlayer)} | {traps.Count(lot => !lot.PlayerBought)} | {traps.Count(lot => lot.PlayerBought)} |");
            }
            var allLots = completed.SelectMany(run => run.Lots).ToArray();
            int aiOverpayments = allLots.Count(lot => lot.Result.sold && !lot.PlayerBought && lot.Result.finalPrice > lot.Item.TrueValue);
            int lastBids = allLots.Sum(lot => lot.AiUsedLastBid.Count(used => used));
            int intimidated = allLots.Sum(lot => lot.AiChickenedOut.Count(outcome => outcome));
            Line();
            int favorableOverpayments = allLots.Count(lot => lot.Result.sold && !lot.PlayerBought &&
                lot.Item.TrueValue > AuctionManager.MinimumOpeningBid && lot.Result.finalPrice > lot.Item.TrueValue);
            Line($"AI paid above true value on {aiOverpayments} settled lots, including {favorableOverpayments} lots profitable at opening. " +
                $"Across {allLots.Sum(lot => lot.Estimates.Length)} bidder-lot observations, AI used its final above-estimate bid {lastBids} times and withdrew after intimidation {intimidated} times. " +
                "The current evaluator uses category accuracy and true value. The 26-variant catalog remains configured on the auction, outside the estimation path.");
            Line();
            Line("## Seed outcomes");
            Line();
            Line("| Seed | SkipAll profit | Conservative profit (purchases) | Aggressive profit (purchases) |");
            Line("| ---: | ---: | ---: | ---: |");
            foreach (int seed in EconomyPlaytest.Seeds)
            {
                var cells = EconomyPlaytest.Policies.Select(policy =>
                {
                    var run = byPolicy[policy].SingleOrDefault(candidate => candidate.Seed == seed);
                    return run == null ? "integration error" : $"{run.Profit} ({run.Purchases}){(Succeeds(run) ? " ✓" : "")}";
                });
                Line($"| {seed} | {string.Join(" | ", cells)} |");
            }
            Line();
            Line("## Representative runs");
            Line();
            var informed = InformedRuns;
            var best = informed.OrderByDescending(run => run.Profit).FirstOrDefault();
            var shift = informed.GroupBy(run => run.Seed)
                .Where(group => group.Count() == 2 && group.Key != best?.Seed)
                .OrderByDescending(group => group.Max(run => run.Profit) - group.Min(run => run.Profit))
                .FirstOrDefault();
            var unprovenRun = informed.Where(run => unproven.Contains(run.Seed) && run.Seed != best?.Seed && run.Seed != shift?.Key)
                .GroupBy(run => run.Seed).OrderBy(group => group.Max(run => run.Profit))
                .Select(group => group.OrderByDescending(run => run.Profit).First()).FirstOrDefault();
            if (best != null && !Succeeds(best))
            {
                Line($"The closest result fell short by ${Target - best.Profit}.");
                Line();
            }
            AddCase("Highest observed profit", best);
            if (shift != null)
            {
                string comparison = string.Join(" and ", shift.Select(run => $"{run.Policy} ${run.Profit}"));
                AddCase($"Largest policy difference ({comparison})", shift.OrderByDescending(run => run.Profit).First());
            }
            AddCase("Seed with no observed success", unprovenRun);
            void AddCase(string label, EconomyRun run)
            {
                if (run == null)
                    return;
                string Settlement(EconomyLot lot) => $"{lot.Item.ItemId} {(lot.Result.sold ? $"{lot.Result.winnerId} ${lot.Result.finalPrice}" : "unsold")}";
                Line($"- **{label} — {run.Seed}, {run.Policy}.** Profit ${run.Profit}, spend ${run.Spend}, " +
                    $"{run.Purchases} purchases. Lot settlements were {string.Join(", ", run.Lots.Select(Settlement))}.");
            }
            Line();
            Line("## Reproducing this batch");
            Line();
            Line($"Unity {Application.unityVersion}, Git HEAD `{gitRevision ?? "unavailable"}`, seeds 1000–1029. Run `EconomyPlaytestTests.BatchProducesConsistentReports` in Edit Mode. " +
                "The fixture uses fresh objects, MVP assets, and scene personality references. Its seed is set on the temporary manager.");
            Line($"Set `{EconomyPlaytestTests.TargetVariable}={Target}` in the Unity process environment to reproduce this target.");
            Line();
            Line(Timing + " Both informed policies know true values, always take the smallest legal bid within their cap, and continue until the auction ends. " +
                "The conservative reserves are $70 Critical and $40 Fluff. The aggressive cap is true value minus $1. Both respect the available wallet. " +
                "These outcomes reflect full information and one response cadence. Human newspaper-reading and reaction time are outside this batch.");
            Line();
            Line("Unsold CSV results have blank winner and price. Item profit is zero for every lot the player did not buy. Closing time is the effective engine deadline in game seconds, recorded from the bidding clock.");
            Line();
            return md.ToString();
        }

        private static string Number(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";

        private sealed class Statistics
        {
            public readonly int count;
            public readonly double? mean;
            public readonly double? median;
            public readonly double? min;
            public readonly double? max;

            public Statistics(IEnumerable<double> values)
            {
                double[] sorted = values.OrderBy(value => value).ToArray();
                count = sorted.Length;
                if (count == 0)
                    return;
                mean = sorted.Average();
                median = (sorted[(count - 1) / 2] + sorted[count / 2]) / 2;
                min = sorted[0];
                max = sorted[count - 1];
            }
        }
    }
}
