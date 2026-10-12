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
                Data = new List<BaseData> { CreateInstance(), CreateInstance() }
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

        [TestCase("2026-10-08", "11:30 AM", "", "2026-10-08 14:10")]
        [TestCase("2026-10-08", "01:00 PM", "", "2026-10-08 14:10")]
        [TestCase("2026-10-08", "1:00 PM", "", "2026-10-08 14:10")]
        [TestCase("2026-10-08", "01:00 PM", "2026-10-08T13:04:12", "2026-10-08 14:10")]
        [TestCase("2026-10-08", "01:00 PM", "2026-10-08T13:50:00", "2026-10-08 16:10")]
        [TestCase("2026-10-08", "01:00 PM", "2026-10-08T09:00:00", "2026-10-08 14:10")]
        [TestCase("2026-10-08", "01:00 PM", "2031-04-02T10:00:00", "2026-10-08 14:10")]
        [TestCase("2025-07-07", "02:30 PM", "", "2025-07-07 16:10")]
        [TestCase("1995-05-10", "", "", "1995-05-10 16:10")]
        [TestCase("2026-10-09", "03:30 PM", "", "2026-10-12 14:10")]
        public void EndTimeFollowsTheJobSchedule(string auctionDate, string closingTime, string updated, string expected)
        {
            var endTime = USTreasuryAuctionsDownloader.GetEndTime(DateTime.Parse(auctionDate), closingTime, updated);
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
        public void AppendsOnlyNewResultsStampedAtTheRun()
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
            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, _ => api.ToString());

            Assert.IsTrue(downloader.Run(new DateTime(2026, 10, 7), false));

            var written = File.ReadAllLines(Path.Combine(folders.Output, "note10y.csv"));
            var added = written.Except(published).ToList();
            Assert.AreEqual(1, added.Count, "only the 2026-10-07 result is new; the announced auction has no results yet");
            StringAssert.StartsWith("20261007,20261007 14:10,9-Year 10-Month", added[0]);
            CollectionAssert.AreEqual(published, written.Take(published.Length));
        }

        [Test]
        public void WritesNothingWhenNothingIsNew()
        {
            using var folders = new TemporaryFolders();
            File.Copy(Path.Combine(SampleFolder, "note10y.csv"), Path.Combine(folders.Processed, "note10y.csv"));

            var known = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")).Last().Split(',');
            var api = new JArray
            {
                Record($"{known[0][..4]}-{known[0][4..6]}-{known[0][6..]}", known[2], $"{known[3][..4]}-{known[3][4..6]}-{known[3][6..]}",
                    $"{known[4][..4]}-{known[4][4..6]}-{known[4][6..]}", known[6], "01:00 PM", "99.000000", known[10])
            };
            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, _ => api.ToString());

            // The known auction comes back with a corrected price and is still recognized as published
            Assert.IsTrue(downloader.Run(new DateTime(2026, 10, 7), false));
            Assert.IsFalse(Directory.Exists(folders.Output) && Directory.EnumerateFiles(folders.Output).Any());
        }

        [Test]
        public void FailsOnAnEmptyAnswer()
        {
            using var folders = new TemporaryFolders();
            File.Copy(Path.Combine(SampleFolder, "note10y.csv"), Path.Combine(folders.Processed, "note10y.csv"));

            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, _ => "[]");

            Assert.IsFalse(downloader.Run(new DateTime(2026, 10, 7), false));
        }

        [Test]
        public void RefusesToAppendWithoutPublishedHistory()
        {
            using var folders = new TemporaryFolders();
            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, _ => throw new InvalidOperationException("must not fetch"));

            Assert.IsFalse(downloader.Run(new DateTime(2026, 10, 7), false));
        }

        [Test]
        public void RebuildFailsWhenAYearComesBackEmpty()
        {
            using var folders = new TemporaryFolders();
            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, _ => "[]");

            Assert.IsFalse(downloader.Run(new DateTime(2026, 10, 7), true));
        }

        [Test]
        public void FailsWhenARequiredFieldIsMissing()
        {
            using var folders = new TemporaryFolders();
            File.Copy(Path.Combine(SampleFolder, "note10y.csv"), Path.Combine(folders.Processed, "note10y.csv"));
            var record = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900");
            record.Remove("cusip");

            var downloader = new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, _ => new JArray { record }.ToString());

            Assert.Throws<InvalidOperationException>(() => downloader.Run(new DateTime(2026, 10, 7), false));
        }

        [Test]
        public void RewritesAnAuctionWithTheSameLineWhenThePublishedFolderIsStale()
        {
            using var folders = new TemporaryFolders();
            var published = File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv"));
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), published);
            var record = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900");
            record["updatedTimestamp"] = "2026-10-07T13:04:12";
            var api = new JArray { record }.ToString();

            // The 13:30 run publishes it; the 15:30 run reads a folder that does not show it yet
            new USTreasuryAuctionsDownloader(Path.Combine(folders.Output, "1330"), folders.Processed, _ => api).Run(new DateTime(2026, 10, 7), false);
            new USTreasuryAuctionsDownloader(Path.Combine(folders.Output, "1530"), folders.Processed, _ => api).Run(new DateTime(2026, 10, 7), false);

            CollectionAssert.AreEqual(File.ReadAllLines(Path.Combine(folders.Output, "1330", "note10y.csv")),
                File.ReadAllLines(Path.Combine(folders.Output, "1530", "note10y.csv")));
        }

        [Test]
        public void StampsAResultCaughtUpTheNextDayAtThatDaysFirstRun()
        {
            using var folders = new TemporaryFolders();
            File.WriteAllLines(Path.Combine(folders.Processed, "note10y.csv"), File.ReadAllLines(Path.Combine(SampleFolder, "note10y.csv")));
            var record = Record("2026-10-07", "9-Year 10-Month", "2026-10-15", "2036-08-15", "4.625000", "01:00 PM", "94.864261", "39926321900");

            new USTreasuryAuctionsDownloader(folders.Output, folders.Processed, _ => new JArray { record }.ToString()).Run(new DateTime(2026, 10, 8), false);

            StringAssert.StartsWith("20261007,20261008 14:10,", File.ReadAllLines(Path.Combine(folders.Output, "note10y.csv")).Last());
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
