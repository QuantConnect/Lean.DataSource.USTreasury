/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.DataProcessing;
using QuantConnect.DataSource;

namespace QuantConnect.DataLibrary.Tests
{
    [TestFixture]
    public class USTreasuryAuctionTests
    {
        private static readonly string SampleFolder = Path.Combine("output", "alternative", "ustreasury", "auctions");

        [TestCase(USTreasuryAuctionBenchmarks.Notes.TenYear)]
        [TestCase(USTreasuryAuctionBenchmarks.Bonds.ThirtyYear)]
        [TestCase(USTreasuryAuctionBenchmarks.CashManagementBills)]
        public void ReadsEverySampleLine(string ticker)
        {
            var config = CreateConfig(ticker);
            var lines = File.ReadAllLines(Path.Combine(SampleFolder, $"{ticker.ToLowerInvariant()}.csv"));
            Assert.IsNotEmpty(lines);

            var previous = DateTime.MinValue;
            foreach (var line in lines)
            {
                var point = (USTreasuryAuction)new USTreasuryAuction().Reader(config, line, DateTime.MinValue, false);
                Assert.IsNotNull(point, line);
                Assert.AreEqual(config.Symbol, point.Symbol);
                Assert.Greater(point.EndTime, point.Time);
                Assert.That(point.EndTime.TimeOfDay, Is.EqualTo(new TimeSpan(14, 10, 0)).Or.EqualTo(new TimeSpan(16, 10, 0)));
                Assert.GreaterOrEqual(point.EndTime, previous, "EndTime goes back in time");
                Assert.IsNotNull(point.TotalAccepted);
                Assert.AreEqual(point.ClearingRate ?? 0m, point.Value);
                Assert.IsNotEmpty(point.ToString());
                previous = point.EndTime;
            }
        }

        [Test]
        public void ParsesTheColumns()
        {
            var line = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")).First(l => l.StartsWith("20200311"));
            var point = (USTreasuryAuction)new USTreasuryAuction().Reader(CreateConfig(USTreasuryAuctionBenchmarks.Notes.TenYear), line, DateTime.MinValue, false);

            Assert.AreEqual(new DateTime(2020, 3, 11), point.Time);
            Assert.AreEqual(new DateTime(2020, 3, 11, 14, 10, 0), point.EndTime);
            Assert.AreEqual("9-Year 11-Month", point.SecurityTerm);
            Assert.IsTrue(point.IsReopening);
            Assert.AreEqual(0.849m, point.HighYield);
            Assert.AreEqual(2.36m, point.BidToCoverRatio);
            Assert.IsNull(point.HighDiscountRate);
            Assert.IsNull(point.Spread);
        }

        [Test]
        public void SkipsTruncatedLine()
        {
            var line = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")).First();
            var truncated = string.Join(",", line.Split(',').Take(USTreasuryAuction.ExpectedColumns - 1));
            Assert.IsNull(new USTreasuryAuction().Reader(CreateConfig(USTreasuryAuctionBenchmarks.Notes.TenYear), truncated, DateTime.MinValue, false));
        }

        [Test]
        public void Clone()
        {
            var expected = CreateInstance();
            AssertAreEqual(expected, expected.Clone());
        }

        [Test]
        public void JsonRoundTrip()
        {
            var expected = CreateInstance();
            var result = JsonConvert.DeserializeObject<USTreasuryAuction>(JsonConvert.SerializeObject(expected));
            AssertAreEqual(expected, result);
        }

        [Test]
        public void CollectionClonesItsAuctions()
        {
            var collection = new USTreasuryAuctions
            {
                Symbol = Symbol.Create(USTreasuryAuctionBenchmarks.CashManagementBills, SecurityType.Base, Market.USA),
                Time = new DateTime(2020, 3, 31),
                EndTime = new DateTime(2020, 3, 31, 14, 10, 0),
                Data = [CreateInstance(), CreateInstance()]
            };
            var clone = (USTreasuryAuctions)collection.Clone();

            Assert.AreEqual(collection.EndTime, clone.EndTime);
            Assert.AreEqual(2, clone.Data.Count);
            AssertAreEqual(collection.Data[0], clone.Data[0]);
        }

        [Test]
        public void SourceAndProperties()
        {
            var config = CreateConfig(USTreasuryAuctionBenchmarks.Notes.TenYear);
            var source = new USTreasuryAuctions().GetSource(config, new DateTime(2026, 10, 12), true);

            Assert.AreEqual(SubscriptionTransportMedium.LocalFile, source.TransportMedium);
            Assert.AreEqual(FileFormat.FoldingCollection, source.Format);
            StringAssert.EndsWith(Path.Combine("alternative", "ustreasury", "auctions", "note10y.csv"), source.Source);
            Assert.AreEqual(Resolution.Daily, new USTreasuryAuctions().DefaultResolution());
            Assert.IsFalse(new USTreasuryAuctions().RequiresMapping());
            Assert.IsTrue(new USTreasuryAuctions().IsSparseData());
        }

        [Test]
        public void SingleAuctionSubscriptionReadsLineByLine()
        {
            // Only a BaseDataCollection can be folded: a USTreasuryAuction subscription reads each line itself
            var config = CreateConfig(USTreasuryAuctionBenchmarks.Notes.TenYear);
            var source = new USTreasuryAuction().GetSource(config, new DateTime(2026, 10, 12), true);

            Assert.AreEqual(FileFormat.Csv, source.Format);
            StringAssert.EndsWith(Path.Combine("alternative", "ustreasury", "auctions", "note10y.csv"), source.Source);
        }

        [Test]
        public void EmptyCollectionToString()
        {
            Assert.AreEqual("[]", new USTreasuryAuctions { Data = null }.ToString());
        }

        [TestCase("2026-10-08", "11:30 AM", "2026-10-08 14:10")]
        [TestCase("2026-10-08", "01:00 PM", "2026-10-08 14:10")]
        [TestCase("2026-10-08", "1:00 PM", "2026-10-08 14:10")]
        [TestCase("2026-10-08", "01:25 PM", "2026-10-08 16:10")]
        [TestCase("2025-07-07", "02:30 PM", "2025-07-07 16:10")]
        [TestCase("1995-05-10", "", "1995-05-10 16:10")]
        [TestCase("2026-10-09", "03:30 PM", "2026-10-12 14:10")]
        public void EndTimeFollowsTheJobSchedule(string auctionDate, string closingTime, string expected)
        {
            var endTime = USTreasuryAuctionsDownloader.GetEndTime(DateTime.Parse(auctionDate), closingTime);
            Assert.AreEqual(DateTime.Parse(expected), endTime);
        }

        [TestCase("Note", "9-Year 10-Month", "10-Year", "No", "No", "No", "NOTE10Y")]
        [TestCase("Bill", "4-Week", "17-Week", "No", "No", "No", "BILL4W")]
        [TestCase("Note", "9-Year 6-Month", "10-Year", "Yes", "No", "No", "TIPS10Y")]
        [TestCase("Note", "1-Year 10-Month", "2-Year", "No", "Yes", "No", "FRN2Y")]
        [TestCase("Bill", "27-Day", "27-Day", "No", "No", "Yes", "CMB")]
        [TestCase("Bond", "29-Year 9-Month", "29-Year 9-Month", "No", "No", "No", "BOND30Y")]
        [TestCase("Note", "3-Year 6-Month", "3-Year 6-Month", "No", "No", "No", "NOTE3Y6M")]
        [TestCase("Note", "4-Year 6-Month", "5-Year 6-Month", "Yes", "No", "No", "TIPS5Y")]
        [TestCase("Bond", "30-Year 6-Month", "30-Year 6-Month", "Yes", "No", "No", "TIPS30Y")]
        public void MapsAuctionsToBenchmarks(string type, string term, string originalTerm, string tips, string floating, string cashManagement, string expected)
        {
            var record = new JObject
            {
                ["securityType"] = type,
                ["securityTerm"] = term,
                ["originalSecurityTerm"] = originalTerm,
                ["tips"] = tips,
                ["floatingRate"] = floating,
                ["cashManagementBillCMB"] = cashManagement
            };
            Assert.AreEqual(expected, USTreasuryAuctionsDownloader.GetTicker(record));
        }

        [Test]
        public async Task AppendsOnlyNewResultsStampedAtTheRun()
        {
            using var folders = new TemporaryFolders();
            var published = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv"));
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), published);

            var known = published.Last().Split(',');
            var api = new JArray
            {
                Record($"{known[0][..4]}-{known[0][4..6]}-{known[0][6..]}", known[2], $"{known[3][..4]}-{known[3][4..6]}-{known[3][6..]}",
                    $"{known[4][..4]}-{known[4][4..6]}-{known[4][6..]}", known[6], "01:00 PM", known[25], known[10]),
                Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900"),
                Record("2026-10-13", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "", "")
            };
            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult(api.ToString()));

            Assert.IsTrue(await downloader.RunAsync(new DateTime(2026, 10, 7), false));

            var written = File.ReadAllLines(Path.Combine(folders.Output, "note10y.csv"));
            var added = written.Except(published).ToList();
            Assert.AreEqual(1, added.Count, "only the 2026-10-07 result is new; the announced auction has no results yet");
            StringAssert.StartsWith("20261007,20261007 14:10,9-Year 10-Month", added[0]);
            CollectionAssert.AreEqual(published, written.Take(published.Length));
        }

        [Test]
        public async Task WritesNothingWhenNothingIsNew()
        {
            using var folders = new TemporaryFolders();
            File.Copy(Path.Combine(SampleFolder, "note10y.csv"), Path.Combine(folders.Processed, "note10y.csv"));

            var known = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")).Last().Split(',');
            var api = new JArray
            {
                Record($"{known[0][..4]}-{known[0][4..6]}-{known[0][6..]}", known[2], $"{known[3][..4]}-{known[3][4..6]}-{known[3][6..]}",
                    $"{known[4][..4]}-{known[4][4..6]}-{known[4][6..]}", known[6], "01:00 PM", "99.000000", known[10])
            };
            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult(api.ToString()));

            // The known auction comes back with a corrected price and is still recognized as published
            Assert.IsTrue(await downloader.RunAsync(new DateTime(2026, 10, 7), false));
            CollectionAssert.AreEqual(new[] { USTreasuryAuctionsDownloader.LastRunFile }, Directory.EnumerateFiles(folders.Output).Select(Path.GetFileName));
        }

        [Test]
        public async Task FailsOnAnEmptyAnswer()
        {
            using var folders = new TemporaryFolders();
            File.Copy(Path.Combine(SampleFolder, "note10y.csv"), Path.Combine(folders.Processed, "note10y.csv"));

            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult("[]"));

            Assert.IsFalse(await downloader.RunAsync(new DateTime(2026, 10, 7), false));
        }

        [Test]
        public async Task RefusesToAppendWithoutPublishedHistory()
        {
            using var folders = new TemporaryFolders();
            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => throw new InvalidOperationException("must not fetch"));

            Assert.IsFalse(await downloader.RunAsync(new DateTime(2026, 10, 7), false));
        }

        [Test]
        public async Task RebuildFailsWhenAYearComesBackEmpty()
        {
            using var folders = new TemporaryFolders();
            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult("[]"));

            Assert.IsFalse(await downloader.RunAsync(new DateTime(2026, 10, 7), true));
        }

        [Test]
        public void FailsWhenARequiredFieldIsMissing()
        {
            using var folders = new TemporaryFolders();
            File.Copy(Path.Combine(SampleFolder, "note10y.csv"), Path.Combine(folders.Processed, "note10y.csv"));
            var record = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900");
            record.Remove("cusip");

            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult(new JArray { record }.ToString()));

            Assert.ThrowsAsync<InvalidOperationException>(() => downloader.RunAsync(new DateTime(2026, 10, 7), false));
        }

        [Test]
        public async Task RewritesAnAuctionWithTheSameLineWhenThePublishedFolderIsStale()
        {
            using var folders = new TemporaryFolders();
            var published = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv"));
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), published);
            var record = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900");
            record["updatedTimestamp"] = "2026-10-07T13:04:12";
            var api = new JArray { record }.ToString();

            // The 13:30 run publishes it; the 15:30 run reads a folder that does not show it yet
            await new USTreasuryAuctionsDownloader(Path.Combine(folders.Output, "1330"), folders.Processed, (_, _) => Task.FromResult(api)).RunAsync(new DateTime(2026, 10, 7), false);
            await new USTreasuryAuctionsDownloader(Path.Combine(folders.Output, "1530"), folders.Processed, (_, _) => Task.FromResult(api)).RunAsync(new DateTime(2026, 10, 7), false);

            CollectionAssert.AreEqual(File.ReadAllLines(Path.Combine(folders.Output, "1330", "note10y.csv")),
                File.ReadAllLines(Path.Combine(folders.Output, "1530", "note10y.csv")));
        }

        [Test]
        public async Task RewritesAnAuctionWithTheSameLineWhenThePublishedFolderIsStaleTheNextDay()
        {
            using var folders = new TemporaryFolders();
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")));
            var api = new JArray { Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "02:30 PM", "94.864261", "39926321900") }.ToString();

            // The D 15:30 run publishes it at D 16:10; the D+1 13:30 run reads a folder that does not show it yet
            await new USTreasuryAuctionsDownloader(Path.Combine(folders.Output, "d"), folders.Processed, (_, _) => Task.FromResult(api)).RunAsync(new DateTime(2026, 10, 7), false);
            await new USTreasuryAuctionsDownloader(Path.Combine(folders.Output, "d1"), folders.Processed, (_, _) => Task.FromResult(api)).RunAsync(new DateTime(2026, 10, 8), false);

            var first = File.ReadAllLines(Path.Combine(folders.Output, "d", "note10y.csv"));
            StringAssert.StartsWith("20261007,20261007 16:10,", first.Last());
            CollectionAssert.AreEqual(first, File.ReadAllLines(Path.Combine(folders.Output, "d1", "note10y.csv")));
        }

        [Test]
        public async Task StampIgnoresTheRecordsUpdateTime()
        {
            using var folders = new TemporaryFolders();
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")));
            var record = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900");
            // Posted at 13:04 and published by the 13:30 run, then corrected after the last run of the day
            record["updatedTimestamp"] = "2026-10-07T15:45:00";

            Assert.IsTrue(await new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult(new JArray { record }.ToString())).RunAsync(new DateTime(2026, 10, 7), false));

            StringAssert.StartsWith("20261007,20261007 14:10,", File.ReadAllLines(Path.Combine(folders.Output, "note10y.csv")).Last());
        }

        [TestCase(0, TestName = "CorrectionOfAnOlderPublishedAuctionDoesNotBlockNewOnes")]
        [TestCase(-1, TestName = "CorrectionOfTheLastPublishedAuctionIsNotAppended")]
        public async Task CorrectedPublishedAuctionIsNotTakenForANewOne(int index)
        {
            using var folders = new TemporaryFolders();
            var published = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv"));
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), published);
            var known = published[index < 0 ? published.Length + index : index].Split(',');
            string Date(string value) => $"{value[..4]}-{value[4..6]}-{value[6..]}";
            // The Treasury moves the maturity of an auction already published by a day
            var maturity = DateTime.ParseExact(known[4], "yyyyMMdd", null).AddDays(1).ToString("yyyy-MM-dd");
            var api = new JArray
            {
                Record(Date(known[0]), known[2], Date(known[3]), maturity, known[6], "01:00 PM", known[25], known[10]),
                Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900")
            };

            Assert.IsTrue(await new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult(api.ToString())).RunAsync(new DateTime(2026, 10, 7), false));

            var written = File.ReadAllLines(Path.Combine(folders.Output, "note10y.csv"));
            CollectionAssert.AreEqual(published, written.Take(published.Length));
            Assert.AreEqual(published.Length + 1, written.Length);
            StringAssert.StartsWith("20261007,20261007 14:10,", written.Last());
        }

        [TestCase("2020-03-02", "9-Year 11-Month", TestName = "SkipsOnlyANewAuctionStampedBeforeTheFilesLastRow")]
        [TestCase(null, "9-Year 1-Month", TestName = "SkipsOnlyANewAuctionStampedAtTheFilesLastRow")]
        public async Task SkipsOnlyAnAuctionLiveHasAlreadyPassed(string auctionDate, string term)
        {
            using var folders = new TemporaryFolders();
            var published = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv"));
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), published);
            var last = published.Last().Split(',')[0];
            auctionDate ??= $"{last[..4]}-{last[4..6]}-{last[6..]}";
            var api = new JArray
            {
                // An auction the API lists only after a later one of the same benchmark was published
                Record(auctionDate, term, "2020-03-16", "2030-02-15", "9.999000", "01:00 PM", "99.000000", "24000000000"),
                Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900")
            };

            Assert.IsTrue(await new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult(api.ToString())).RunAsync(new DateTime(2026, 10, 7), false));

            var written = File.ReadAllLines(Path.Combine(folders.Output, "note10y.csv"));
            Assert.AreEqual(published.Length + 1, written.Length);
            StringAssert.StartsWith("20261007,20261007 14:10,", written.Last());
        }

        [Test]
        public async Task SecondSameTermAuctionOfTheDayIsNotTakenForTheFirst()
        {
            using var folders = new TemporaryFolders();
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")));
            var first = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "11:30 AM", "94.864261", "39926321900");
            var second = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-16", "4.625000", "02:30 PM", "95.000000", "1000000000");
            second["cusip"] = "SECOND";
            var pending = (JObject)second.DeepClone();
            pending["totalAccepted"] = "";

            // The 13:30 run publishes the first; the second has its results only for the 15:30 run
            await new USTreasuryAuctionsDownloader(Path.Combine(folders.Output, "1330"), folders.Processed, (_, _) => Task.FromResult(new JArray { first, pending }.ToString())).RunAsync(new DateTime(2026, 10, 7), false);
            var afterFirst = File.ReadAllLines(Path.Combine(folders.Output, "1330", "note10y.csv"));
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), afterFirst);
            Assert.IsTrue(await new USTreasuryAuctionsDownloader(Path.Combine(folders.Output, "1530"), folders.Processed, (_, _) => Task.FromResult(new JArray { first, second }.ToString())).RunAsync(new DateTime(2026, 10, 7), false));

            var written = File.ReadAllLines(Path.Combine(folders.Output, "1530", "note10y.csv"));
            Assert.AreEqual(afterFirst.Length + 1, written.Length);
            StringAssert.StartsWith("20261007,20261007 16:10,9-Year 10-Month,20261015,20360816,", written.Last());
        }

        [Test]
        public void TipsReopeningWithoutOriginalTermBelongsToItsBenchmark()
        {
            var record = new JObject
            {
                ["securityType"] = "Note",
                ["securityTerm"] = "9-Year 4-Month",
                ["originalSecurityTerm"] = "",
                ["tips"] = "Yes",
                ["floatingRate"] = "No",
                ["cashManagementBillCMB"] = "No",
                ["reopening"] = "Yes"
            };
            Assert.AreEqual("TIPS10Y", USTreasuryAuctionsDownloader.GetTicker(record));
        }

        [Test]
        public async Task SkipsARecordWithAMalformedDate()
        {
            using var folders = new TemporaryFolders();
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")));
            var malformed = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900");
            malformed["auctionDate"] = "2026-1-7";
            malformed["cusip"] = "MALFORMED";
            var api = new JArray { malformed, Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900") };

            Assert.IsTrue(await new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult(api.ToString())).RunAsync(new DateTime(2026, 10, 7), false));

            var written = File.ReadAllLines(Path.Combine(folders.Output, "note10y.csv"));
            Assert.AreEqual(File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")).Length + 1, written.Length);
            StringAssert.StartsWith("20261007,20261007 14:10,", written.Last());
        }

        [TestCase("2026-10-07T00:00:00.000")]
        [TestCase("2026-10-07T00:00:00Z")]
        [TestCase("2026-10-07")]
        public async Task ReadsTheDateOfAnyApiTimestampFormat(string date)
        {
            using var folders = new TemporaryFolders();
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")));
            var record = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900");
            record["auctionDate"] = date;
            record["issueDate"] = date.Replace("2026-10-07", "2026-10-15");

            Assert.IsTrue(await new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult(new JArray { record }.ToString())).RunAsync(new DateTime(2026, 10, 7), false));

            StringAssert.StartsWith("20261007,20261007 14:10,9-Year 10-Month,20261015,20360815,", File.ReadAllLines(Path.Combine(folders.Output, "note10y.csv")).Last());
        }

        [Test]
        public async Task FailsWhenNoRecordHasAReadableDate()
        {
            using var folders = new TemporaryFolders();
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")));
            var record = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900");
            record["auctionDate"] = "10/07/2026";

            Assert.IsFalse(await new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, (_, _) => Task.FromResult(new JArray { record }.ToString())).RunAsync(new DateTime(2026, 10, 7), false));
        }

        // October 2026 is daylight saving time: New York is UTC-4
        private static Func<DateTime> NewYorkClock(int year, int month, int day, int hour, int minute)
        {
            return () => new DateTime(year, month, day, hour, minute, 0).AddHours(4);
        }

        private static USTreasuryAuctionsDownloader Downloader(string output, string processed, JArray api, Func<DateTime> clock)
        {
            return new USTreasuryAuctionsDownloader(output, processed, (_, _) => Task.FromResult(api.ToString()), clock);
        }

        private static string[] PublishedSample(TemporaryFolders folders, string lastRun)
        {
            var published = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv"));
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), published);
            if (lastRun is not null)
            {
                File.WriteAllText(Path.Combine(folders.Processed, USTreasuryAuctionsDownloader.LastRunFile), lastRun);
            }
            return published;
        }

        [TestCase("20261007 13:30", 15, 30, "20261007 16:10", TestName = "ResultPostedAfterItsRunIsStampedAtTheNextRun")]
        [TestCase("20261007 15:30", 13, 30, "20261008 14:10", TestName = "ResultPostedAfterTheLastRunOfTheDayIsStampedNextMorning")]
        [TestCase("20261006 15:30", 15, 30, "20261007 14:10", TestName = "WithoutTheMorningRunTheCloseStampStays")]
        [TestCase(null, 15, 30, "20261007 14:10", TestName = "WithoutALastRunFileTheCloseStampStays")]
        public async Task LateResultIsStampedAtTheRunAfterTheFoldersLastRun(string lastRun, int hour, int minute, string expected)
        {
            using var folders = new TemporaryFolders();
            PublishedSample(folders, lastRun);
            var api = new JArray { Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900") };
            var runDay = expected.StartsWith("20261008") ? 8 : 7;

            Assert.IsTrue(await Downloader(folders.Output, folders.Processed, api, NewYorkClock(2026, 10, runDay, hour, minute)).RunAsync(new DateTime(2026, 10, runDay), false));

            StringAssert.StartsWith($"20261007,{expected},", File.ReadAllLines(Path.Combine(folders.Output, "note10y.csv")).Last());
        }

        [Test]
        public async Task SecondSameMorningAuctionPostedAfterTheRunIsPublishedAtTheNextRun()
        {
            using var folders = new TemporaryFolders();
            PublishedSample(folders, "20261006 15:30");
            var first = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "11:30 AM", "94.864261", "39926321900");
            var second = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-16", "4.625000", "11:30 AM", "95.000000", "1000000000");
            second["cusip"] = "SECOND";
            var pending = (JObject)second.DeepClone();
            pending["totalAccepted"] = "";

            // 13:30 publishes the first; the second closes the same morning but posts after that run
            var morning = Path.Combine(folders.Output, "1330");
            Assert.IsTrue(await Downloader(morning, folders.Processed, new JArray { first, pending }, NewYorkClock(2026, 10, 7, 13, 30)).RunAsync(new DateTime(2026, 10, 7), false));
            File.Copy(Path.Combine(morning, "note10y.csv"), Path.Combine(folders.Processed, "note10y.csv"), true);
            File.Copy(Path.Combine(morning, USTreasuryAuctionsDownloader.LastRunFile), Path.Combine(folders.Processed, USTreasuryAuctionsDownloader.LastRunFile), true);
            var afternoon = Path.Combine(folders.Output, "1530");
            Assert.IsTrue(await Downloader(afternoon, folders.Processed, new JArray { first, second }, NewYorkClock(2026, 10, 7, 15, 30)).RunAsync(new DateTime(2026, 10, 7), false));

            var written = File.ReadAllLines(Path.Combine(afternoon, "note10y.csv"));
            StringAssert.StartsWith("20261007,20261007 14:10,9-Year 10-Month,20261015,20360815,", written[^2]);
            StringAssert.StartsWith("20261007,20261007 16:10,9-Year 10-Month,20261015,20360816,", written[^1]);

            // The next morning's run reads a folder still at 13:30 and rewrites the late row with the same line
            var stale = Path.Combine(folders.Output, "next");
            Assert.IsTrue(await Downloader(stale, folders.Processed, new JArray { first, second }, NewYorkClock(2026, 10, 8, 13, 30)).RunAsync(new DateTime(2026, 10, 8), false));
            CollectionAssert.AreEqual(written, File.ReadAllLines(Path.Combine(stale, "note10y.csv")));
        }

        [TestCase(2026, 10, 7, 13, 35, null, "20261007 13:30", TestName = "LastRunIsTheRedundancysScheduledRun")]
        [TestCase(2026, 10, 7, 10, 0, null, "20261006 15:30", TestName = "LastRunBeforeTheFirstRunIsThePreviousDay")]
        [TestCase(2026, 10, 10, 12, 0, null, "20261009 15:30", TestName = "LastRunOnAWeekendIsFriday")]
        [TestCase(2026, 10, 7, 13, 35, "20261007 15:30", "20261007 15:30", TestName = "LastRunNeverGoesBack")]
        public async Task WritesTheScheduledRunItFetchedAt(int year, int month, int day, int hour, int minute, string previous, string expected)
        {
            using var folders = new TemporaryFolders();
            PublishedSample(folders, previous);
            var known = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")).Last().Split(',');
            string Date(string value) => $"{value[..4]}-{value[4..6]}-{value[6..]}";
            var api = new JArray { Record(Date(known[0]), known[2], Date(known[3]), Date(known[4]), known[6], "01:00 PM", known[25], known[10]) };

            Assert.IsTrue(await Downloader(folders.Output, folders.Processed, api, NewYorkClock(year, month, day, hour, minute)).RunAsync(new DateTime(2026, 10, 7), false));

            Assert.AreEqual(expected, File.ReadAllText(Path.Combine(folders.Output, USTreasuryAuctionsDownloader.LastRunFile)));
        }

        [Test]
        public async Task FailedRunLeavesTheLastRunFileAlone()
        {
            using var folders = new TemporaryFolders();
            PublishedSample(folders, "20261006 15:30");

            Assert.IsFalse(await Downloader(folders.Output, folders.Processed, new JArray(), NewYorkClock(2026, 10, 7, 13, 30)).RunAsync(new DateTime(2026, 10, 7), false));

            Assert.IsFalse(File.Exists(Path.Combine(folders.Output, USTreasuryAuctionsDownloader.LastRunFile)));
        }

        [TestCase("13:00")]
        [TestCase("1:00 PM ET")]
        public async Task SkipsAResultWithAnUnreadableClosingTime(string close)
        {
            using var folders = new TemporaryFolders();
            var published = PublishedSample(folders, null);
            var unreadable = Record("2026-10-07", "9-Year 9-Month", "2026-10-15", "2036-07-15", "4.625000", close, "94.000000", "1000000000");
            unreadable["cusip"] = "UNREADABLE";
            var api = new JArray { unreadable, Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900") };

            Assert.IsTrue(await Downloader(folders.Output, folders.Processed, api, NewYorkClock(2026, 10, 7, 13, 30)).RunAsync(new DateTime(2026, 10, 7), false));

            var written = File.ReadAllLines(Path.Combine(folders.Output, "note10y.csv"));
            Assert.AreEqual(published.Length + 1, written.Length);
            StringAssert.StartsWith("20261007,20261007 14:10,9-Year 10-Month,", written.Last());
        }

        [Test]
        public async Task FailsWhenNoResultHasAReadableClosingTime()
        {
            using var folders = new TemporaryFolders();
            PublishedSample(folders, null);
            var api = new JArray { Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "13:00", "94.864261", "39926321900") };

            Assert.IsFalse(await Downloader(folders.Output, folders.Processed, api, NewYorkClock(2026, 10, 7, 13, 30)).RunAsync(new DateTime(2026, 10, 7), false));
        }

        [TestCase(429, 7, 1, 7)]
        [TestCase(429, null, 2, 10)]
        [TestCase(408, null, 1, 5)]
        [TestCase(503, null, 3, 15)]
        [TestCase(503, 600, 1, 60)]
        [TestCase(404, null, 1, null)]
        [TestCase(400, 7, 1, null)]
        public void RetriesTransientResponsesHonouringRetryAfter(int status, int? retryAfter, int attempt, int? expectedSeconds)
        {
            using var response = new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)status);
            if (retryAfter is not null)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfter.Value));
            }
            var delay = USTreasuryAuctionsDownloader.RetryDelay(response, attempt);
            Assert.AreEqual(expectedSeconds is null ? null : TimeSpan.FromSeconds(expectedSeconds.Value), delay);
        }

        [Test]
        public void RetryAfterADateWaitsUntilThen()
        {
            using var response = new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(30));
            var delay = USTreasuryAuctionsDownloader.RetryDelay(response, 1).Value;
            Assert.That(delay.TotalSeconds, Is.InRange(25, 31));
        }

        // The 10-year note of 2026-10-07 as the API answers since 2026-10-20, every field the processor reads
        private const string RenamedRecord = """
            {
             "cusip": "91282CRF0",
             "competitiveAuctionCloseDate": "2026-10-07T00:00:00",
             "issueDate": "2026-10-15T00:00:00",
             "maturityDate": "2036-08-15T00:00:00",
             "securityType": "Note",
             "securityTerm": "9-Year 10-Month",
             "originalSecurityTerm": "10-Year",
             "inflationProtected": "No",
             "floatingRate": "No",
             "cashManagementBill": "No",
             "reopening": "Yes",
             "competitiveAuctionCloseTime": "01:00 PM",
             "interestRate": "4.625",
             "spread": "",
             "offeringAmount": "39000000000",
             "grandTotalParRecognized": "108995289900",
             "grandTotalParAwarded": "39926321900",
             "subtotalCompetitiveParRecognizedInsideOfOfferingAmount": "107734406000",
             "subtotalCompetitiveParAwardedInsideOfOfferingAmount": "38665438000",
             "subtotalNoncompetitiveParAwardedInsideOfOfferingAmount": "334593700",
             "bidToCoverRatio": "2.77",
             "highYield": "5.300",
             "lowYield": "4.300",
             "medianYield": "5.255",
             "averageYield": "",
             "highRate": "",
             "lowRate": "",
             "medianRate": "",
             "averageRate": "",
             "investmentRate": "",
             "highDiscountMargin": "",
             "lowDiscountMargin": "",
             "medianDiscountMargin": "",
             "highPrice": "94.864261",
             "allottedAtHighYieldPercentage": "57.60",
             "allottedAtHighRatePercentage": "",
             "allottedAtHighDiscountMarginPercentage": "",
             "directBiddersCompetitiveParRecognized": "14852200000",
             "directBiddersCompetitiveParAwarded": "6618200000",
             "indirectBiddersCompetitiveParRecognized": "40006206000",
             "indirectBiddersCompetitiveParAwarded": "31063238000",
             "primaryDealersCompetitiveParRecognized": "52876000000",
             "primaryDealersCompetitiveParAwarded": "984000000",
             "somaNoncompetitiveParAwarded": "926290200",
             "fimaNoncompetitiveParAwarded": "0",
             "updatedTimestamp": "2026-10-07T13:04:02"
            }
            """;

        // As the processor reads the API: dates stay text
        private static JObject ParseRecord(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }

        [Test]
        public void ReadsTheRenamedApiFields()
        {
            var record = USTreasuryAuctionsDownloader.Normalize(ParseRecord(RenamedRecord));

            Assert.AreEqual("NOTE10Y", USTreasuryAuctionsDownloader.GetTicker(record));
            Assert.AreEqual(new DateTime(2026, 10, 7, 14, 10, 0), USTreasuryAuctionsDownloader.GetEndTime(new DateTime(2026, 10, 7), record.Value<string>("closingTimeCompetitive")));
            Assert.AreEqual("20261007,20261007 14:10,9-Year 10-Month,20261015,20360815,1,4.625,,39000000000,108995289900,39926321900,107734406000,"
                + "38665438000,334593700,2.77,5.300,4.300,5.255,,,,,,,,94.864261,57.60,14852200000,6618200000,40006206000,31063238000,52876000000,"
                + "984000000,926290200,0", USTreasuryAuctionsDownloader.ToLine(record, new DateTime(2026, 10, 7, 14, 10, 0)));
        }

        [Test]
        public void KeepsARecordInTheFormerNames()
        {
            var record = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900");
            Assert.AreSame(record, USTreasuryAuctionsDownloader.Normalize(record));
        }

        [TestCase("grandTotalParAwarded")]
        [TestCase("averageYield")]
        [TestCase("subtotalNoncompetitiveParAwardedInsideOfOfferingAmount")]
        public void FailsWhenARenamedSourceFieldIsMissing(string field)
        {
            var record = ParseRecord(RenamedRecord);
            record.Remove(field);
            Assert.Throws<InvalidOperationException>(() => USTreasuryAuctionsDownloader.Normalize(record));
        }

        [TestCase("", "500", "")]
        [TestCase("1124538100", "722000000", "402538100")]
        [TestCase("1124538100", "", "1124538100")]
        public void NoncompetitiveAcceptedLeavesOutFima(string inside, string fima, string expected)
        {
            var record = ParseRecord(RenamedRecord);
            record["subtotalNoncompetitiveParAwardedInsideOfOfferingAmount"] = inside;
            record["fimaNoncompetitiveParAwarded"] = fima;
            Assert.AreEqual(expected, USTreasuryAuctionsDownloader.Normalize(record).Value<string>("noncompetitiveAccepted"));
        }

        [Test]
        public void MedianFallsBackToTheAverage()
        {
            var record = ParseRecord(RenamedRecord);
            record["medianYield"] = "";
            record["averageYield"] = "5.111";
            Assert.AreEqual("5.111", USTreasuryAuctionsDownloader.Normalize(record).Value<string>("averageMedianYield"));
        }

        [TestCase("2026-10-07", "2026-10-07 17:30", true)]
        [TestCase("2026-10-07", "2026-10-07 23:59", true)]
        [TestCase("2026-10-06", "2026-10-07 17:30", false)]
        [TestCase("2026-10-08", "2026-10-07 17:30", false)]
        public void AcceptsOnlyTodaysDeploymentDate(string deploymentDate, string utcNow, bool expected)
        {
            Assert.AreEqual(expected, Program.IsScheduledDeploymentDate(DateTime.Parse(deploymentDate), DateTime.Parse(utcNow)));
        }

        [Test]
        public void YieldCurveStaysTheDefaultDataset()
        {
            Config.Reset();
            Assert.AreEqual(Program.YieldCurveDatasetName, Program.GetDatasetName());
        }

        private static JObject Record(string auctionDate, string term, string issueDate, string maturityDate, string coupon,
            string closingTime, string highPrice, string totalAccepted)
        {
            return new JObject
            {
                ["cusip"] = $"{auctionDate}{term}",
                ["auctionDate"] = $"{auctionDate}T00:00:00",
                ["issueDate"] = $"{issueDate}T00:00:00",
                ["maturityDate"] = $"{maturityDate}T00:00:00",
                ["securityType"] = "Note",
                ["securityTerm"] = term,
                ["originalSecurityTerm"] = "10-Year",
                ["tips"] = "No",
                ["floatingRate"] = "No",
                ["cashManagementBillCMB"] = "No",
                ["reopening"] = "Yes",
                ["interestRate"] = coupon,
                ["closingTimeCompetitive"] = closingTime,
                ["highPrice"] = highPrice,
                ["totalAccepted"] = totalAccepted,
                ["offeringAmount"] = "39000000000"
            };
        }

        private static SubscriptionDataConfig CreateConfig(string ticker)
        {
            return new SubscriptionDataConfig(typeof(USTreasuryAuction), Symbol.Create(ticker, SecurityType.Base, Market.USA),
                Resolution.Daily, TimeZones.NewYork, TimeZones.NewYork, false, false, false);
        }

        private static USTreasuryAuction CreateInstance()
        {
            var line = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")).First();
            return (USTreasuryAuction)new USTreasuryAuction().Reader(CreateConfig(USTreasuryAuctionBenchmarks.Notes.TenYear), line, DateTime.MinValue, false);
        }

        private static void AssertAreEqual(object expected, object result)
        {
            foreach (var property in expected.GetType().GetProperties())
            {
                Assert.AreEqual(property.GetValue(expected), property.GetValue(result), property.Name);
            }
            foreach (var field in expected.GetType().GetFields())
            {
                Assert.AreEqual(field.GetValue(expected), field.GetValue(result), field.Name);
            }
        }

        private sealed class TemporaryFolders : IDisposable
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            public string Processed => Path.Combine(_root, "processed");
            public string Output => Path.Combine(_root, "output");

            public TemporaryFolders()
            {
                Directory.CreateDirectory(Processed);
            }

            public void Dispose()
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
        }
    }
}
