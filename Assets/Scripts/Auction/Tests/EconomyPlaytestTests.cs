using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AuctionGame.Auction.Tests
{
    public sealed class EconomyPlaytestTests
    {
        internal const string TargetVariable = "ECONOMY_PLAYTEST_TARGET";
        private static readonly Regex CsvField = new Regex("\\G(?:\"((?:[^\"]|\"\")*)\"|([^\",\\r\\n]*))(,|\\z)");

        [Test]
        public void FixtureMatchesApprovedCatalogAndOpeningEconomy()
        {
            var fixture = new EconomyPlaytest();
            string[] folders = { "Assets/AuctionMvp/ItemsV08", "Assets/AuctionDemo" };
            var approved = folders.Select(folder => AssetDatabase.FindAssets("t:AuctionItem", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AuctionItem>).ToArray()).ToArray();
            Assert.That(approved.Select(items => items.Length), Is.EqualTo(new[] { 20, 6 }));
            Assert.That(fixture.Level.Database.Items, Is.EquivalentTo(approved.SelectMany(items => items)));
            Assert.That(fixture.Level.Database.Items.Select(item => item.ItemId), Is.Unique);
            Assert.That(fixture.Items.Select(item => (item.ItemId, item.TrueValue)), Is.EqualTo(new[]
            {
                ("arch_chalice_a", 315), ("silly_tears_b", 20), ("art_venus_a", 320),
                ("pop_cd_a", 175), ("arch_sword_b", 20), ("art_vase_a", 190)
            }));
            Assert.That(fixture.Level.StartingBudget, Is.EqualTo(1000));
            Assert.That(fixture.Level.AiStartingBudget, Is.EqualTo(600));
            Assert.That(fixture.Level.ProfitTarget, Is.EqualTo(50));
            Assert.That(fixture.Personalities.Select(p => p.StartingBudget), Is.All.EqualTo(600));
            Assert.That(AuctionManager.MinimumOpeningBid, Is.EqualTo(10));
            Assert.That(fixture.Items.Sum(item => item.TrueValue), Is.EqualTo(1040));
            var subsets = EconomyPlaytestReport.OpeningSubsets(fixture.Items);
            Assert.That(subsets.Single(set => set.Count == 6).Profit, Is.EqualTo(980));
            Assert.That(subsets.Where(set => set.Count == 2).Max(set => set.Profit), Is.EqualTo(615));
            var best = subsets.Where(set => set.Spend <= fixture.Level.StartingBudget).OrderByDescending(set => set.Profit).First();
            Assert.That(best, Is.EqualTo((60, 980, 6)));
            var favorable = fixture.Items.Where(item => item.TrueValue > AuctionManager.MinimumOpeningBid).ToArray();
            var triples = EconomyPlaytestReport.OpeningSubsets(favorable).Where(set => set.Count == 3).Select(set => set.Profit).ToArray();
            Assert.That(triples.Min(), Is.EqualTo(185));
            Assert.That(triples.Max(), Is.EqualTo(795));
        }

        [Test]
        public void SteppedAiClockMatchesSingleTickWithNoPlayerInput()
        {
            var fixture = new EconomyPlaytest();
            var stepped = fixture.Run(1000, EconomyPolicy.SkipAll);
            var single = fixture.Run(1000, EconomyPolicy.SkipAll, 240);
            Assert.That(stepped.Error, Is.Null);
            Assert.That(single.Error, Is.Null);
            Assert.That(Fingerprint(stepped), Is.EqualTo(Fingerprint(single)));
        }

        [Test]
        public void BatchProducesConsistentReports()
        {
            string configuredTarget = Environment.GetEnvironmentVariable(TargetVariable);
            int? target = string.IsNullOrEmpty(configuredTarget) ? (int?)null : Int(configuredTarget);
            Assert.That(target == null || target > 0, Is.True, $"{TargetVariable} must be a positive amount.");
            var fixture = new EconomyPlaytest(target);
            var runs = (from seed in EconomyPlaytest.Seeds
                        from policy in EconomyPlaytest.Policies
                        select fixture.Run(seed, policy)).ToArray();
            var replayFailures = runs.Where(run =>
            {
                var replay = fixture.Run(run.Seed, run.Policy);
                return !run.Completed || !replay.Completed || Fingerprint(run) != Fingerprint(replay);
            }).Select(run => $"{run.Seed}/{run.Policy}").ToArray();

            string reproducibility = replayFailures.Length == 0
                ? $"PASS (all {runs.Length} runs replayed, including full bid sequences)"
                : $"FAILED (outcome mismatch or integration error for {string.Join(", ", replayFailures)})";
            string project = Directory.GetParent(Application.dataPath).FullName;
            string revision = GitRevision(project);
            string revisionLabel = revision == null ? "current" : revision.Substring(0, 7);
            string directory = Path.Combine(project, "Logs", "EconomyPlaytest", "zero-opening-price", revisionLabel,
                $"target-{fixture.ProfitTarget.ToString(CultureInfo.InvariantCulture)}");
            new EconomyPlaytestReport(fixture, runs, reproducibility, revision).Write(directory);
            TestContext.Progress.WriteLine($"Economy playtest reports: {directory}");

            Assert.That(runs.Where(run => !run.Completed).Select(run => $"{run.Seed}/{run.Policy}: {run.Error}"), Is.Empty);
            Assert.That(replayFailures, Is.Empty);
            Assert.That(runs, Has.Length.EqualTo(90));
            Assert.That(runs.Sum(run => run.Lots.Length), Is.EqualTo(540));
            Assert.That(runs.SelectMany(run => run.Lots).Any(lot => lot.Duration > AuctionManager.InitialBiddingTime), Is.True);
            ValidateExports(directory, fixture, runs);
        }

        [Test]
        public void CsvEscapesQuotesCommasNewlinesAndNullFields()
        {
            string[] expected = { "first", "comma, and \"quotes\"", "line\r\nbreak", "" };
            string row = EconomyPlaytestReport.Csv(expected[0], expected[1], expected[2], null);
            Assert.That(row, Is.EqualTo("first,\"comma, and \"\"quotes\"\"\",\"line\r\nbreak\","));
            Assert.That(ParseRow(row), Is.EqualTo(expected));
        }

        private static string Fingerprint(EconomyRun run) => JsonConvert.SerializeObject(new
        {
            run.EndBalances,
            Lots = run.Lots.Select(lot => new
            {
                lot.Item.ItemId, lot.Result, lot.Bids, Duration = Math.Round(lot.Duration, 6),
                lot.Before, lot.After, lot.Estimates, lot.EffectiveInterests, lot.EffectiveAccuracies,
                lot.AiUsedLastBid, lot.AiChickenedOut, lot.WalletBlockedPlayer
            })
        });

        private static void ValidateExports(string directory, EconomyPlaytest fixture, EconomyRun[] expected)
        {
            var runs = ReadCsv(Path.Combine(directory, EconomyPlaytestReport.RunFile), EconomyPlaytestReport.RunHeader);
            var lots = ReadCsv(Path.Combine(directory, EconomyPlaytestReport.LotFile), EconomyPlaytestReport.LotHeader);
            var json = JObject.Parse(File.ReadAllText(Path.Combine(directory, EconomyPlaytestReport.SummaryFile)));
            Assert.That(runs, Has.Length.EqualTo(90));
            Assert.That(lots, Has.Length.EqualTo(540));
            Assert.That((int)json["completedRuns"], Is.EqualTo(runs.Length));
            Assert.That((int)json["completedLots"], Is.EqualTo(lots.Length));
            Assert.That((int)json["profitTarget"], Is.EqualTo(fixture.ProfitTarget));
            Assert.That((int)json["openingPrice"], Is.Zero);
            Assert.That((int)json["minimumOpeningBid"], Is.EqualTo(AuctionManager.MinimumOpeningBid));
            Assert.That(json["integrationErrors"], Is.Empty);
            Assert.That(json["catalogItemIds"].Values<string>(), Is.EqualTo(fixture.Level.Database.Items.Select(item => item.ItemId)));
            Assert.That(json["seeds"].Values<int>(), Is.EqualTo(EconomyPlaytest.Seeds));
            Assert.That(json["runs"].Count(), Is.EqualTo(runs.Length));

            var keys = runs.Select(row => (row[0], row[1])).ToArray();
            Assert.That(keys.Distinct().Count(), Is.EqualTo(90));
            var lotGroups = lots.ToLookup(row => (row[0], row[1]));
            for (int i = 0; i < runs.Length; i++)
            {
                var run = runs[i];
                var simulation = expected[i];
                Assert.That(keys[i], Is.EqualTo((simulation.Seed.ToString(CultureInfo.InvariantCulture), simulation.Policy.ToString())));
                var rows = lotGroups[keys[i]].ToArray();
                Assert.That(rows, Has.Length.EqualTo(6));
                Assert.That(Int(run[2]), Is.EqualTo(rows.Length));
                Assert.That(rows.Select(row => row[3]), Is.EqualTo(fixture.Items.Select(item => item.ItemId)));
                for (int lotIndex = 0; lotIndex < rows.Length; lotIndex++)
                {
                    var row = rows[lotIndex];
                    var result = simulation.Lots[lotIndex];
                    Assert.That(Int(row[2]), Is.EqualTo(lotIndex + 1));
                    Assert.That(row[4], Is.EqualTo(result.Item.ItemType.Role.ToString()));
                    Assert.That(Int(row[5]), Is.EqualTo(AuctionManager.MinimumOpeningBid));
                    Assert.That(Int(row[6]), Is.EqualTo(result.Item.TrueValue));
                    Assert.That(bool.Parse(row[9]), Is.EqualTo(result.Result.sold));
                    Assert.That(row[7], Is.EqualTo(result.Result.winnerId));
                    if (result.Result.sold)
                        Assert.That(Int(row[8]), Is.EqualTo(result.Result.finalPrice));
                    else
                        Assert.That(row[8], Is.Empty);
                    Assert.That(Int(row[10]), Is.EqualTo(result.Bids.Length));
                    Assert.That(double.Parse(row[11], CultureInfo.InvariantCulture), Is.EqualTo(result.Duration).Within(0.000001));
                    for (int bidder = 0; bidder < 4; bidder++)
                    {
                        Assert.That(Int(row[12 + 2 * bidder]), Is.EqualTo(result.Before[bidder]));
                        Assert.That(Int(row[13 + 2 * bidder]), Is.EqualTo(result.After[bidder]));
                    }
                    Assert.That(bool.Parse(row[20]), Is.EqualTo(result.PlayerBought));
                    Assert.That(Int(row[21]), Is.EqualTo(result.PlayerBought ? Int(row[6]) - Int(row[8]) : 0));
                }
                var purchases = rows.Where(row => row[7] == BidderIds.Player).ToArray();
                int profit = purchases.Sum(row => Int(row[6]) - Int(row[8]));
                Assert.That(Int(run[3]), Is.EqualTo(purchases.Length));
                Assert.That(Int(run[4]), Is.EqualTo(purchases.Sum(row => Int(row[8]))));
                Assert.That(Int(run[5]), Is.EqualTo(profit));
                Assert.That(Int(run[6]), Is.EqualTo(fixture.ProfitTarget));
                Assert.That(bool.Parse(run[7]), Is.EqualTo(profit >= Int(run[6])));
                Assert.That(run[12], Is.EqualTo("Completed"));
                Assert.That(run[13], Is.Empty);
                var jsonRun = json["runs"][i];
                Assert.That((int)jsonRun["seed"], Is.EqualTo(Int(run[0])));
                Assert.That((string)jsonRun["scenario"], Is.EqualTo(run[1]));
                Assert.That((int)jsonRun["playerProfit"], Is.EqualTo(profit));
                Assert.That((bool)jsonRun["success"], Is.EqualTo(bool.Parse(run[7])));
                for (int bidder = 0; bidder < 4; bidder++)
                {
                    Assert.That(Int(run[8 + bidder]), Is.EqualTo(Int(rows.Last()[13 + 2 * bidder])));
                    Assert.That((int)jsonRun["endBalances"][EconomyPlaytest.BidderOrder[bidder]], Is.EqualTo(Int(run[8 + bidder])));
                }
            }
            foreach (var scenario in json["scenarios"])
            {
                string policy = (string)scenario["scenario"];
                var policyRuns = runs.Where(row => row[1] == policy).ToArray();
                Assert.That(policyRuns.Select(row => Int(row[0])), Is.EqualTo(EconomyPlaytest.Seeds));
                Assert.That((int)scenario["completedRuns"], Is.EqualTo(policyRuns.Length));
                Assert.That((int)scenario["successes"], Is.EqualTo(policyRuns.Count(row => bool.Parse(row[7]))));
                Assert.That(scenario["lotStatistics"].Count(), Is.EqualTo(6));
                foreach (var stats in scenario["lotStatistics"])
                {
                    var itemRows = lots.Where(row => row[1] == policy && row[3] == (string)stats["itemId"]).ToArray();
                    var prices = itemRows.Where(row => bool.Parse(row[9])).Select(row => Int(row[8])).OrderBy(price => price).ToArray();
                    Assert.That((int)stats["sold"], Is.EqualTo(prices.Length));
                    Assert.That((int)stats["unsold"], Is.EqualTo(itemRows.Length - prices.Length));
                    Assert.That((double?)stats["winningPrice"]["mean"], Is.EqualTo(prices.Length == 0 ? (double?)null : prices.Average()));
                }
            }
        }

        private static int Int(string value) => int.Parse(value, CultureInfo.InvariantCulture);

        private static string[][] ReadCsv(string path, string expectedHeader)
        {
            string[] lines = File.ReadAllLines(path);
            Assert.That(lines[0], Is.EqualTo(expectedHeader));
            var rows = lines.Skip(1).Select(ParseRow).ToArray();
            Assert.That(rows.Select(row => row.Length), Is.All.EqualTo(ParseRow(expectedHeader).Length));
            return rows;
        }

        private static string[] ParseRow(string row)
        {
            var fields = new System.Collections.Generic.List<string>();
            for (Match match = CsvField.Match(row); match.Success; match = CsvField.Match(row, match.Index + match.Length))
            {
                fields.Add(match.Groups[1].Success ? match.Groups[1].Value.Replace("\"\"", "\"") : match.Groups[2].Value);
                if (!match.Groups[3].Value.Equals(",", StringComparison.Ordinal))
                    return fields.ToArray();
            }
            throw new FormatException($"Invalid CSV row: {row}");
        }

        private static string GitRevision(string project)
        {
            try
            {
                using var git = Process.Start(new ProcessStartInfo("git", "rev-parse --verify HEAD")
                {
                    WorkingDirectory = project, CreateNoWindow = true, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true
                });
                return git != null && git.WaitForExit(2000) && git.ExitCode == 0
                    ? git.StandardOutput.ReadToEnd().Trim() : null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }
    }
}
