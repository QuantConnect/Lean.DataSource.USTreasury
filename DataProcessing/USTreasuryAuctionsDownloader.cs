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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantConnect.DataSource;
using QuantConnect.Logging;
using QuantConnect.Util;

namespace QuantConnect.DataProcessing
{
    /// <summary>
    /// Downloads U.S. Treasury auction results from the TreasuryDirect auction query API and writes one file
    /// per benchmark, appending the auctions not published yet
    /// </summary>
    public class USTreasuryAuctionsDownloader
    {
        /// <summary>Value of the dataset-name config key that selects this dataset</summary>
        public const string DatasetName = "auctions";

        /// <summary>
        /// The scheduled runs, New York time: cron 30 13,15 * * 1-5, with a redundancy job five minutes after
        /// each on the same chain name. Synchronize Once lets each scheduled run publish.
        /// </summary>
        public static readonly TimeSpan[] ScheduledRuns = { new(13, 30, 0), new(15, 30, 0) };

        /// <summary>
        /// Time between a scheduled run and the moment a live algorithm is certain to have its rows: live
        /// re-reads a local file every 30 minutes, plus 10 minutes for the job, or its redundancy five minutes
        /// later, to start, run and sync
        /// </summary>
        public static readonly TimeSpan DeliveryMargin = TimeSpan.FromMinutes(40);

        // Results are posted a few minutes after the competitive close: median 3.4 minutes and 95th
        // percentile 4.8 minutes over the 4,560 auctions posted the same day since 2014
        private static readonly TimeSpan PostingDelay = TimeSpan.FromMinutes(5);

        // The auction-to-issue lag is at most 15 days since 2015, and the API filters on the issue date
        private const int IssueDateLookaheadDays = 30;
        private const int LookbackDays = 10;
        private const int FirstYear = 1979;
        private const string ApiUrl = "https://www.treasurydirect.gov/TA_WS/securities/search";

        // Fields every record carries, even when empty. A renamed one would otherwise read as empty and skip,
        // misfile or collapse every auction while the run still succeeds.
        private static readonly string[] RequiredFields =
        {
            "cusip", "auctionDate", "issueDate", "maturityDate", "securityType", "securityTerm", "originalSecurityTerm",
            "tips", "floatingRate", "cashManagementBillCMB", "reopening", "totalAccepted", "closingTimeCompetitive"
        };
        private static readonly string[] ClosingTimeFormats = { "hh:mm tt", "h:mm tt" };

        private readonly string _destinationDirectory;
        private readonly string _processedDirectory;
        private readonly Func<string, string> _get;
        private readonly RateGate _rateGate = new(1, TimeSpan.FromSeconds(1));

        /// <summary>
        /// Creates a downloader
        /// </summary>
        /// <param name="destinationDirectory">Folder the changed files are written to</param>
        /// <param name="processedDirectory">Folder holding the files already published</param>
        /// <param name="get">HTTP GET returning the body, injectable for tests</param>
        public USTreasuryAuctionsDownloader(string destinationDirectory, string processedDirectory, Func<string, string> get = null)
        {
            _destinationDirectory = destinationDirectory;
            _processedDirectory = processedDirectory;
            _get = get ?? HttpGet;
        }

        /// <summary>
        /// Runs the download for one deployment date
        /// </summary>
        /// <param name="deploymentDate">Date of the run, as DataFleet sets it</param>
        /// <param name="processAll">True to rebuild the whole history instead of appending</param>
        /// <returns>True on success</returns>
        public bool Run(DateTime deploymentDate, bool processAll)
        {
            var published = ReadPublished();
            var publishedRows = published.Values.Sum(lines => lines.Count);
            Log.Trace($"USTreasuryAuctionsDownloader.Run(): read {publishedRows} published auctions in {published.Count} files from {_processedDirectory}");

            if (!processAll && publishedRows == 0)
            {
                // An empty published folder means a missing or misconfigured data folder, never a first run:
                // the history is seeded by a rebuild, so appending to nothing would publish a stub
                Log.Error($"USTreasuryAuctionsDownloader.Run(): no published auctions found in {_processedDirectory}; run with process-all to seed the history");
                return false;
            }

            List<JObject> records;
            if (processAll)
            {
                records = new List<JObject>();
                for (var year = FirstYear; year <= deploymentDate.Year + 1; year++)
                {
                    var yearRecords = Fetch(new DateTime(year, 1, 1), new DateTime(year, 12, 31));
                    // Every year up to this one has issues; only next year can legitimately be empty
                    if (yearRecords.Count == 0 && year <= deploymentDate.Year)
                    {
                        Log.Error($"USTreasuryAuctionsDownloader.Run(): the API answered with no securities issued in {year}");
                        return false;
                    }
                    records.AddRange(yearRecords);
                }
            }
            else
            {
                var lastPublished = published.Values.SelectMany(lines => lines).Max(line => ParseAuctionDate(line));
                var start = new[] { deploymentDate.AddDays(-LookbackDays), lastPublished }.Min();
                records = Fetch(start, deploymentDate.AddDays(IssueDateLookaheadDays));
            }

            if (!processAll && records.Count == 0)
            {
                // Every 40-day window of issue dates holds weekly bills, so an empty answer is an outage
                Log.Error("USTreasuryAuctionsDownloader.Run(): the API answered with no securities");
                return false;
            }

            var publishedIdentities = published.ToDictionary(pair => pair.Key, pair => pair.Value.Select(GetIdentity).ToHashSet());
            // A result first seen on a later day than it was posted (every run of that day failed) can only reach
            // live from this deployment date's first run on
            var catchUpFloor = processAll ? DateTime.MinValue : deploymentDate.Date + ScheduledRuns[0] + DeliveryMargin;
            var newRows = new Dictionary<string, List<(DateTime EndTime, string Line)>>();
            var seen = new HashSet<string>();
            foreach (var record in records)
            {
                var auctionDate = ParseApiDate(Text(record, "auctionDate"));
                // Announced auctions are listed before the results, with every result field empty
                if (auctionDate == null || auctionDate > deploymentDate || string.IsNullOrEmpty(Text(record, "totalAccepted")))
                {
                    continue;
                }

                var ticker = GetTicker(record);
                // The CUSIP and the auction date identify an auction; the CUSIP is not published, only used here
                if (!seen.Add($"{Text(record, "cusip")},{auctionDate:yyyyMMdd}"))
                {
                    continue;
                }

                if (!processAll && publishedIdentities.TryGetValue(ticker, out var identities) && identities.Contains(GetIdentity(record)))
                {
                    continue;
                }

                var endTime = GetEndTime(auctionDate.Value, Text(record, "closingTimeCompetitive"), Text(record, "updatedTimestamp"));
                if (endTime < catchUpFloor)
                {
                    endTime = catchUpFloor;
                }
                if (!newRows.TryGetValue(ticker, out var rows))
                {
                    newRows[ticker] = rows = new List<(DateTime, string)>();
                }
                rows.Add((endTime, ToLine(record, endTime)));
            }

            foreach (var (ticker, rows) in newRows)
            {
                var existing = processAll ? new List<string>() : published.GetValueOrDefault(ticker) ?? new List<string>();
                var lastEndTime = existing.Count == 0 ? DateTime.MinValue : existing.Max(ParseEndTime);
                var lines = existing.ToList();
                foreach (var (endTime, line) in rows)
                {
                    // A run never stamps a row before one already in the file: LEAN's backtest reader skips
                    // a point that goes back in time
                    if (endTime < lastEndTime)
                    {
                        Log.Trace($"USTreasuryAuctionsDownloader.Run(): {ticker} auction stamped {endTime:yyyy-MM-dd HH:mm} is older than the file's last row, skipped: {line}");
                        continue;
                    }
                    lines.Add(line);
                }
                Write(ticker, lines);
            }

            Log.Trace($"USTreasuryAuctionsDownloader.Run(): wrote {newRows.Values.Sum(rows => rows.Count)} auctions to {newRows.Count} files in {_destinationDirectory}");
            return true;
        }

        /// <summary>
        /// Time the result of an auction reaches algorithms: the first scheduled run at or after the result is
        /// posted, plus the delivery margin. The posting time is the record's update time when it falls on the
        /// auction day, otherwise the competitive close plus five minutes; with neither (closing times exist
        /// only from 2008) it is the afternoon run. 14:10 for a result posted by 13:30, 16:10 for one posted by
        /// 15:30, and 14:10 of the next business day after that. The stamp depends only on the record, so a run
        /// that rewrites an auction already published writes the same line.
        /// </summary>
        public static DateTime GetEndTime(DateTime auctionDate, string closingTime, string updatedTimestamp = null)
        {
            var hasClose = DateTime.TryParseExact(closingTime, ClosingTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var close);
            var posted = hasClose ? close.TimeOfDay + PostingDelay : ScheduledRuns[^1];
            // A result can not be posted before the close: an update earlier that day touched the announcement
            if (DateTime.TryParseExact(updatedTimestamp, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated)
                && updated.Date == auctionDate.Date && hasClose && updated.TimeOfDay > close.TimeOfDay)
            {
                posted = updated.TimeOfDay;
            }

            foreach (var run in ScheduledRuns)
            {
                if (posted <= run)
                {
                    return auctionDate.Date + run + DeliveryMargin;
                }
            }
            return NextBusinessDay(auctionDate.Date) + ScheduledRuns[0] + DeliveryMargin;
        }

        /// <summary>
        /// Ticker of the benchmark an auction belongs to. A reopening belongs to the benchmark of the security
        /// it reopens; bills use the term as auctioned, since a 13-week bill is a reopening of a 26-week one.
        /// </summary>
        public static string GetTicker(JObject record)
        {
            if (Text(record, "cashManagementBillCMB") == "Yes")
            {
                return USTreasuryAuctionBenchmarks.CashManagementBills;
            }

            var type = Text(record, "securityType");
            if (type == "Bill")
            {
                return "BILL" + CompactTerm(Text(record, "securityTerm"));
            }

            var originalTerm = Text(record, "originalSecurityTerm");
            var isTips = Text(record, "tips") == "Yes";
            var term = BenchmarkTerm(string.IsNullOrEmpty(originalTerm) ? Text(record, "securityTerm") : originalTerm, isTips);
            if (isTips)
            {
                return "TIPS" + term;
            }
            if (Text(record, "floatingRate") == "Yes")
            {
                return "FRN" + term;
            }
            return type.ToUpperInvariant() + term;
        }

        /// <summary>
        /// Writes one auction as a line of the file
        /// </summary>
        public static string ToLine(JObject record, DateTime endTime)
        {
            var values = new[]
            {
                ParseApiDate(Text(record, "auctionDate"))?.ToStringInvariant("yyyyMMdd"),
                endTime.ToStringInvariant("yyyyMMdd HH:mm"),
                Text(record, "securityTerm"),
                ParseApiDate(Text(record, "issueDate"))?.ToStringInvariant("yyyyMMdd"),
                ParseApiDate(Text(record, "maturityDate"))?.ToStringInvariant("yyyyMMdd"),
                Text(record, "reopening") == "Yes" ? "1" : "0",
                Text(record, "interestRate"),
                Text(record, "spread"),
                Text(record, "offeringAmount"),
                Text(record, "totalTendered"),
                Text(record, "totalAccepted"),
                Text(record, "competitiveTendered"),
                Text(record, "competitiveAccepted"),
                Text(record, "noncompetitiveAccepted"),
                Text(record, "bidToCoverRatio"),
                Text(record, "highYield"),
                Text(record, "lowYield"),
                Text(record, "averageMedianYield"),
                Text(record, "highDiscountRate"),
                Text(record, "lowDiscountRate"),
                Text(record, "averageMedianDiscountRate"),
                Text(record, "highInvestmentRate"),
                Text(record, "highDiscountMargin"),
                Text(record, "lowDiscountMargin"),
                Text(record, "averageMedianDiscountMargin"),
                Text(record, "highPrice"),
                Text(record, "allocationPercentage"),
                Text(record, "directBidderTendered"),
                Text(record, "directBidderAccepted"),
                Text(record, "indirectBidderTendered"),
                Text(record, "indirectBidderAccepted"),
                Text(record, "primaryDealerTendered"),
                Text(record, "primaryDealerAccepted"),
                Text(record, "somaAccepted"),
                Text(record, "fimaNoncompetitiveAccepted")
            };
            return string.Join(",", values.Select(value => (value ?? string.Empty).Replace(",", string.Empty).Trim()));
        }

        private List<JObject> Fetch(DateTime startIssueDate, DateTime endIssueDate)
        {
            var url = $"{ApiUrl}?startDate={startIssueDate:yyyy-MM-dd}&endDate={endIssueDate:yyyy-MM-dd}&format=json";
            var body = _get(url);
            // Dates stay text: Newtonsoft would otherwise parse them into the runner's local time
            using var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None };
            var records = JArray.Load(reader).OfType<JObject>().ToList();
            foreach (var record in records)
            {
                var missing = RequiredFields.FirstOrDefault(field => !record.ContainsKey(field));
                if (missing != null)
                {
                    throw new InvalidOperationException($"USTreasuryAuctionsDownloader.Fetch(): the API answered a record without '{missing}'");
                }
            }
            Log.Trace($"USTreasuryAuctionsDownloader.Fetch(): {records.Count} securities issued {startIssueDate:yyyy-MM-dd} to {endIssueDate:yyyy-MM-dd}");
            return records;
        }

        private string HttpGet(string url)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("QuantConnect-DataProcessing/1.0 (support@quantconnect.com)");
            for (var attempt = 1; ; attempt++)
            {
                _rateGate.WaitToProceed();
                try
                {
                    using var response = client.GetAsync(url).SynchronouslyAwaitTaskResult();
                    if ((int)response.StatusCode >= 500 && attempt < 5)
                    {
                        Log.Trace($"USTreasuryAuctionsDownloader.HttpGet(): {(int)response.StatusCode} from {url}, retry {attempt}");
                        Thread.Sleep(TimeSpan.FromSeconds(5 * attempt));
                        continue;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        // A rejected request fails the same way on every attempt: fail now
                        throw new InvalidOperationException($"{(int)response.StatusCode} from {url}");
                    }
                    return response.Content.ReadAsStringAsync().SynchronouslyAwaitTaskResult();
                }
                catch (Exception error) when ((error is HttpRequestException || error is IOException || error is TaskCanceledException) && attempt < 5)
                {
                    Log.Trace($"USTreasuryAuctionsDownloader.HttpGet(): {error.Message} from {url}, retry {attempt}");
                    Thread.Sleep(TimeSpan.FromSeconds(5 * attempt));
                }
            }
        }

        private Dictionary<string, List<string>> ReadPublished()
        {
            var published = new Dictionary<string, List<string>>();
            if (!Directory.Exists(_processedDirectory))
            {
                return published;
            }
            foreach (var file in Directory.EnumerateFiles(_processedDirectory, "*.csv"))
            {
                var ticker = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
                published[ticker] = File.ReadAllLines(file).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            }
            return published;
        }

        private void Write(string ticker, List<string> lines)
        {
            Directory.CreateDirectory(_destinationDirectory);
            var sorted = lines
                .OrderBy(ParseEndTime)
                .ThenBy(line => line.Split(',')[0], StringComparer.Ordinal)
                .ThenBy(line => line, StringComparer.Ordinal)
                .ToList();
            var path = Path.Combine(_destinationDirectory, $"{ticker.ToLowerInvariant()}.csv");
            var temporary = path + ".tmp";
            File.WriteAllLines(temporary, sorted);
            File.Move(temporary, path, true);
        }

        // A published auction is recognized by its date, term, issue and maturity dates and coupon, fields the
        // Treasury does not revise, so a corrected result is not appended a second time. The CUSIP would do it
        // alone but is not published. Four pairs of 1984 to 1986 notes share these fields; only a rebuild reaches them,
        // and the rebuild deduplicates by CUSIP.
        private static string GetIdentity(JObject record)
        {
            return GetIdentity(ToLine(record, DateTime.MinValue));
        }

        private static string GetIdentity(string line)
        {
            var csv = line.Split(',');
            return string.Join("|", csv[0], csv[2], csv[3], csv[4], csv[6]);
        }

        private static DateTime ParseAuctionDate(string line)
        {
            return DateTime.ParseExact(line.Split(',')[0], "yyyyMMdd", CultureInfo.InvariantCulture);
        }

        private static DateTime ParseEndTime(string line)
        {
            return DateTime.ParseExact(line.Split(',')[1], "yyyyMMdd HH:mm", CultureInfo.InvariantCulture);
        }

        private static DateTime? ParseApiDate(string value)
        {
            return string.IsNullOrEmpty(value) ? null : DateTime.ParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string Text(JObject record, string field)
        {
            return record.Value<string>(field) ?? string.Empty;
        }

        private static string CompactTerm(string term)
        {
            // "9-Year 10-Month" becomes "9Y10M", "119-Day" becomes "119D"
            return string.Concat(Regex.Matches(term, @"(\d+)-(Year|Month|Week|Day)").Select(match => match.Groups[1].Value + match.Groups[2].Value[0]));
        }

        /// <summary>
        /// Benchmark term of a note or bond: a term within three months of a whole year is that year, so an
        /// original issue sold with a short or long first coupon, like the 29-year 9-month bond of August 2007,
        /// belongs to its benchmark, the 30-year bond. TIPS were issued with long first coupons of up to six
        /// months (the 5-year 6-month TIPS of October 2004), so for TIPS a remainder of up to six months rounds
        /// down to the year.
        /// </summary>
        public static string BenchmarkTerm(string term, bool isTips = false)
        {
            var years = Regex.Match(term, @"(\d+)-Year");
            var months = Regex.Match(term, @"(\d+)-Month");
            var totalMonths = (years.Success ? int.Parse(years.Groups[1].Value, CultureInfo.InvariantCulture) * 12 : 0)
                + (months.Success ? int.Parse(months.Groups[1].Value, CultureInfo.InvariantCulture) : 0);
            if (isTips && totalMonths >= 12 && totalMonths % 12 <= 6)
            {
                return $"{totalMonths / 12}Y";
            }
            var nearestYear = (int)Math.Round(totalMonths / 12m, MidpointRounding.AwayFromZero);
            return nearestYear > 0 && Math.Abs(totalMonths - nearestYear * 12) <= 3 ? $"{nearestYear}Y" : CompactTerm(term);
        }

        private static DateTime NextBusinessDay(DateTime date)
        {
            // The job's cron runs Monday to Friday, holidays included, so only weekends are skipped
            var next = date.AddDays(1);
            while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                next = next.AddDays(1);
            }
            return next;
        }
    }
}
