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
    public class USTreasuryAuctionsDownloader : IDisposable
    {
        /// <summary>Value of the dataset-name config key that selects this dataset</summary>
        public const string DatasetName = "auctions";

        /// <summary>
        /// File next to the benchmark files holding the last scheduled run whose rows the folder holds, New York
        /// time. A new result whose run had already passed was posted late and is stamped at the next run.
        /// </summary>
        public const string LastRunFile = "lastrun.txt";

        /// <summary>
        /// The scheduled runs, New York time: cron 30 13,15 * * 1-5, with a redundancy job five minutes after
        /// each on the same chain name. Synchronize Once lets each scheduled run publish.
        /// </summary>
        public static readonly TimeSpan[] ScheduledRuns = [new(13, 30, 0), new(15, 30, 0)];

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
        [
            "cusip", "auctionDate", "issueDate", "maturityDate", "securityType", "securityTerm", "originalSecurityTerm",
            "tips", "floatingRate", "cashManagementBillCMB", "reopening", "totalAccepted", "closingTimeCompetitive", "updatedTimestamp"
        ];
        // TreasuryDirect renamed its fields on 2026-10-20. Each name the processor reads, from the first of its
        // sources that has a value; every source must be present. Matched value by value against the 11,154
        // records downloaded before the change.
        private static readonly (string Name, string[] Sources)[] RenamedFields =
        [
            ("auctionDate", ["competitiveAuctionCloseDate"]),
            ("closingTimeCompetitive", ["competitiveAuctionCloseTime"]),
            ("tips", ["inflationProtected"]),
            ("cashManagementBillCMB", ["cashManagementBill"]),
            ("totalTendered", ["grandTotalParRecognized"]),
            ("totalAccepted", ["grandTotalParAwarded"]),
            ("competitiveTendered", ["subtotalCompetitiveParRecognizedInsideOfOfferingAmount"]),
            ("competitiveAccepted", ["subtotalCompetitiveParAwardedInsideOfOfferingAmount"]),
            ("averageMedianYield", ["medianYield", "averageYield"]),
            ("highDiscountRate", ["highRate"]),
            ("lowDiscountRate", ["lowRate"]),
            ("averageMedianDiscountRate", ["medianRate", "averageRate"]),
            ("highInvestmentRate", ["investmentRate"]),
            ("averageMedianDiscountMargin", ["medianDiscountMargin"]),
            ("allocationPercentage", ["allottedAtHighYieldPercentage", "allottedAtHighRatePercentage", "allottedAtHighDiscountMarginPercentage"]),
            ("directBidderTendered", ["directBiddersCompetitiveParRecognized"]),
            ("directBidderAccepted", ["directBiddersCompetitiveParAwarded"]),
            ("indirectBidderTendered", ["indirectBiddersCompetitiveParRecognized"]),
            ("indirectBidderAccepted", ["indirectBiddersCompetitiveParAwarded"]),
            ("primaryDealerTendered", ["primaryDealersCompetitiveParRecognized"]),
            ("primaryDealerAccepted", ["primaryDealersCompetitiveParAwarded"]),
            ("somaAccepted", ["somaNoncompetitiveParAwarded"]),
            ("fimaNoncompetitiveAccepted", ["fimaNoncompetitiveParAwarded"])
        ];
        private const string NoncompetitiveInsideOffering = "subtotalNoncompetitiveParAwardedInsideOfOfferingAmount";
        private static readonly string[] ClosingTimeFormats = ["hh:mm tt", "h:mm tt"];
        private static readonly int[] StandardTenors = [2, 3, 5, 7, 10, 20, 30];

        private readonly string _destinationDirectory;
        private readonly string _processedDirectory;
        private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(1);

        private readonly Func<string, CancellationToken, Task<string>> _get;
        private readonly Func<DateTime> _utcNow;
        private DateTime _lastRequestUtc = DateTime.MinValue;
        private readonly HttpClient _client;

        /// <summary>
        /// Creates a downloader
        /// </summary>
        /// <param name="destinationDirectory">Folder the changed files are written to</param>
        /// <param name="processedDirectory">Folder holding the files already published</param>
        /// <param name="get">HTTP GET returning the body, injectable for tests</param>
        /// <param name="utcNow">Clock, injectable for tests; names this run in the last run file</param>
        /// <param name="handler">HTTP handler, injectable for tests</param>
        public USTreasuryAuctionsDownloader(string destinationDirectory, string processedDirectory, Func<string, CancellationToken, Task<string>> get = null,
            Func<DateTime> utcNow = null, HttpMessageHandler handler = null)
        {
            _destinationDirectory = destinationDirectory;
            _processedDirectory = processedDirectory;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            if (get is null)
            {
                // One client for every request of the run: a rebuild makes about 49 of them
                _client = handler is null ? new HttpClient() : new HttpClient(handler);
                _client.Timeout = TimeSpan.FromSeconds(60);
                _client.DefaultRequestHeaders.UserAgent.ParseAdd("QuantConnect-DataProcessing/1.0 (support@quantconnect.com)");
            }
            _get = get ?? HttpGetAsync;
        }

        /// <summary>
        /// Releases the HTTP client
        /// </summary>
        public void Dispose()
        {
            _client?.Dispose();
        }

        /// <summary>
        /// Runs the download for one deployment date
        /// </summary>
        /// <param name="deploymentDate">Date of the run, as DataFleet sets it</param>
        /// <param name="processAll">True to rebuild the whole history instead of appending</param>
        /// <param name="cancellationToken">Cancels the requests</param>
        /// <returns>True on success</returns>
        public async Task<bool> RunAsync(DateTime deploymentDate, bool processAll, CancellationToken cancellationToken = default)
        {
            var published = ReadPublished();
            var publishedRows = published.Values.Sum(lines => lines.Count);
            Log.Trace($"USTreasuryAuctionsDownloader.RunAsync(): read {publishedRows} published auctions in {published.Count} files from {_processedDirectory}");

            if (!processAll && publishedRows == 0)
            {
                // An empty published folder means a missing or misconfigured data folder, never a first run:
                // the history is seeded by a rebuild, so appending to nothing would publish a stub
                Log.Error($"USTreasuryAuctionsDownloader.RunAsync(): no published auctions found in {_processedDirectory}; run with process-all to seed the history");
                return false;
            }

            List<JObject> records;
            if (processAll)
            {
                records = [];
                for (var year = FirstYear; year <= deploymentDate.Year + 1; year++)
                {
                    var yearRecords = await FetchAsync(new DateTime(year, 1, 1), new DateTime(year, 12, 31), cancellationToken);
                    // Every year up to this one has issues; only next year can legitimately be empty
                    if (yearRecords.Count == 0 && year <= deploymentDate.Year)
                    {
                        Log.Error($"USTreasuryAuctionsDownloader.RunAsync(): the API answered with no securities issued in {year}");
                        return false;
                    }
                    records.AddRange(yearRecords);
                }
            }
            else
            {
                // Files are sorted by EndTime, so the last lines hold the latest auctions; an earlier date only widens the window
                var lastPublished = published.Values.Where(lines => lines.Count > 0).Max(lines => ParseAuctionDate(lines[^1]));
                var lookbackStart = deploymentDate.AddDays(-LookbackDays);
                var start = lastPublished < lookbackStart ? lastPublished : lookbackStart;
                records = await FetchAsync(start, deploymentDate.AddDays(IssueDateLookaheadDays), cancellationToken);
            }

            if (!processAll && records.Count == 0)
            {
                // Every 40-day window of issue dates holds weekly bills, so an empty answer is an outage
                Log.Error("USTreasuryAuctionsDownloader.RunAsync(): the API answered with no securities");
                return false;
            }

            // Every auction with results, once: the CUSIP and the auction date identify an auction; the CUSIP is
            // not published, only used here
            List<Result> results = [];
            var seen = new HashSet<string>();
            var unreadable = 0;
            List<string> unreadableResults = [];
            foreach (var record in records)
            {
                var auctionDate = ParseApiDate(Text(record, "auctionDate"));
                var badDate = new[] { "auctionDate", "issueDate", "maturityDate" }
                    .FirstOrDefault(field => Text(record, field).Length > 0 && ParseApiDate(Text(record, field)) is null);
                if (badDate is not null)
                {
                    // One unreadable record must not stop the day's other auctions
                    Log.Error($"USTreasuryAuctionsDownloader.RunAsync(): skipped CUSIP {Text(record, "cusip")} with unreadable {badDate} '{Text(record, badDate)}'");
                    unreadable++;
                    continue;
                }
                // Announced auctions are listed before the results, with every result field empty
                if (auctionDate is null || auctionDate > deploymentDate || string.IsNullOrEmpty(Text(record, "totalAccepted"))
                    || !seen.Add($"{Text(record, "cusip")},{auctionDate:yyyyMMdd}"))
                {
                    continue;
                }

                var close = Text(record, "closingTimeCompetitive");
                if (close.Length > 0 && !TryParseClose(close, out _))
                {
                    // A changed format would stamp every new result as if it had no close
                    Log.Error($"USTreasuryAuctionsDownloader.RunAsync(): skipped CUSIP {Text(record, "cusip")} with unreadable closing time '{close}'");
                    unreadableResults.Add($"{GetTicker(record)}|{GetIdentity(auctionDate.Value, Text(record, "securityTerm"))}");
                    continue;
                }

                var endTime = GetEndTime(auctionDate.Value, close);
                var line = ToLine(record, endTime);
                results.Add(new Result(GetTicker(record), GetIdentity(auctionDate.Value, Text(record, "securityTerm")), GetAuctionKey(line), endTime, line,
                    ParseUpdated(Text(record, "updatedTimestamp"))));
            }

            if (unreadable == records.Count)
            {
                // Nothing readable is a changed API format, not a quiet day
                Log.Error($"USTreasuryAuctionsDownloader.RunAsync(): no readable record among the {records.Count} the API answered");
                return false;
            }

            var currentRun = ScheduledRunAtOrBefore(_utcNow().ConvertFromUtc(TimeZones.NewYork));
            var lastRun = processAll ? null : ReadLastRun(currentRun);
            var newRows = processAll ? results : SelectNew(results, published)
                .Select(row => LateStamp(row, lastRun))
                .ToList();

            var publishedIdentities = published.SelectMany(pair => pair.Value.Select(line => $"{pair.Key}|{GetIdentity(line)}")).ToHashSet();
            if (processAll && (unreadable > 0 || unreadableResults.Count > 0))
            {
                // A rebuild replaces every file, so a history missing those auctions would delete them
                Log.Error($"USTreasuryAuctionsDownloader.RunAsync(): {unreadable + unreadableResults.Count} records have an unreadable date or closing time; the rebuild writes nothing");
                return false;
            }
            if (!processAll && newRows.Count == 0 && unreadableResults.Any(key => !publishedIdentities.Contains(key)))
            {
                // New results that all fail to read are a changed closing time format, not a quiet day
                Log.Error("USTreasuryAuctionsDownloader.RunAsync(): every new result has an unreadable closing time");
                return false;
            }
            var changed = new Dictionary<string, List<string>>();
            var written = 0;
            foreach (var group in newRows.GroupBy(row => row.Ticker))
            {
                List<string> existing = processAll ? [] : published.GetValueOrDefault(group.Key) ?? [];
                var lastEndTime = existing.Count == 0 ? DateTime.MinValue : ParseEndTime(existing[^1]);
                List<string> lines = [.. existing];
                foreach (var row in group)
                {
                    // Live has already emitted that time and would never deliver the row, while backtests would.
                    // Only this row is held back, so the other auctions and benchmarks keep publishing.
                    if (row.EndTime <= lastEndTime)
                    {
                        Log.Error($"USTreasuryAuctionsDownloader.RunAsync(): skipped new {group.Key} auction stamped {row.EndTime:yyyy-MM-dd HH:mm}, at or before the file's last row: {row.Line}");
                        continue;
                    }
                    lines.Add(row.Line);
                    written++;
                }
                if (lines.Count > existing.Count)
                {
                    changed[group.Key] = lines;
                }
            }

            foreach (var (ticker, lines) in changed)
            {
                Write(ticker, lines);
            }
            WriteLastRun(currentRun, lastRun);

            Log.Trace($"USTreasuryAuctionsDownloader.RunAsync(): wrote {written} auctions to {changed.Count} files in {_destinationDirectory}");
            return true;
        }

        /// <summary>
        /// Time the result of an auction reaches algorithms: the first scheduled run after the result is posted,
        /// plus the delivery margin. The posting time is the competitive close plus five minutes; without
        /// one (closing times exist only from 2008) it is the afternoon run. 14:10 for a result posted before 13:30,
        /// 16:10 for one posted before 15:30, and 14:10 of the next business day after that. The stamp depends only on
        /// the auction's date and close, never on the run or on the record's update time, which moves whenever the
        /// Treasury corrects it, so a run that rewrites an auction already published writes the same line.
        /// </summary>
        public static DateTime GetEndTime(DateTime auctionDate, string closingTime)
        {
            if (!TryParseClose(closingTime, out var close))
            {
                return auctionDate.Date + ScheduledRuns[^1] + DeliveryMargin;
            }
            var posted = close + PostingDelay;

            foreach (var run in ScheduledRuns)
            {
                // A run can not fetch a result posted the minute it starts
                if (posted < run)
                {
                    return auctionDate.Date + run + DeliveryMargin;
                }
            }
            return NextBusinessDay(auctionDate.Date) + ScheduledRuns[0] + DeliveryMargin;
        }

        /// <summary>
        /// The latest scheduled run at or before a New York time
        /// </summary>
        public static DateTime ScheduledRunAtOrBefore(DateTime newYorkTime)
        {
            for (var day = newYorkTime.Date; ; day = day.AddDays(-1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }
                for (var i = ScheduledRuns.Length - 1; i >= 0; i--)
                {
                    if (day + ScheduledRuns[i] <= newYorkTime)
                    {
                        return day + ScheduledRuns[i];
                    }
                }
            }
        }

        /// <summary>
        /// The first scheduled run after a New York time
        /// </summary>
        public static DateTime ScheduledRunAfter(DateTime newYorkTime)
        {
            for (var day = newYorkTime.Date; ; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }
                foreach (var run in ScheduledRuns)
                {
                    if (day + run > newYorkTime)
                    {
                        return day + run;
                    }
                }
            }
        }

        /// <summary>
        /// Delay before retrying a response, or null when retrying can not help: server errors, 408 and 429
        /// are transient, honouring Retry-After when present (capped at a minute)
        /// </summary>
        public static TimeSpan? RetryDelay(HttpResponseMessage response, int attempt)
        {
            var status = (int)response.StatusCode;
            if (status < 500 && status != 408 && status != 429)
            {
                return null;
            }
            var retryAfter = response.Headers.RetryAfter;
            var delay = retryAfter?.Delta
                ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(5 * attempt));
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
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
            // A reopening's own term is what is left to maturity, so without the original term it belongs to the
            // first standard tenor at or above it (never seen in the API since 1979, every reopening carries one)
            var term = string.IsNullOrEmpty(originalTerm)
                ? Text(record, "reopening") == "Yes"
                    ? StandardTenorAtOrAbove(Text(record, "securityTerm"))
                    : BenchmarkTerm(Text(record, "securityTerm"), isTips)
                : BenchmarkTerm(originalTerm, isTips);
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
            string[] values =
            [
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
            ];
            return string.Join(",", values.Select(Clean));
        }

        private async Task<List<JObject>> FetchAsync(DateTime startIssueDate, DateTime endIssueDate, CancellationToken cancellationToken)
        {
            var url = $"{ApiUrl}?startDate={startIssueDate:yyyy-MM-dd}&endDate={endIssueDate:yyyy-MM-dd}&format=json";
            var body = await _get(url, cancellationToken);
            // Dates stay text: Newtonsoft would otherwise parse them into the runner's local time
            using var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None };
            var records = JArray.Load(reader).OfType<JObject>().Select(Normalize).ToList();
            foreach (var record in records)
            {
                var missing = RequiredFields.FirstOrDefault(field => !record.ContainsKey(field));
                if (missing is not null)
                {
                    throw new InvalidOperationException($"USTreasuryAuctionsDownloader.FetchAsync(): the API answered a record without '{missing}'");
                }
            }
            Log.Trace($"USTreasuryAuctionsDownloader.FetchAsync(): {records.Count} securities issued {startIssueDate:yyyy-MM-dd} to {endIssueDate:yyyy-MM-dd}");
            return records;
        }

        /// <summary>
        /// A record in the names the processor reads: those of the API before 2026-10-20, kept as they come, or
        /// the renamed ones mapped back. A source field missing from a renamed record fails the run, as a missing
        /// field does.
        /// </summary>
        public static JObject Normalize(JObject record)
        {
            if (record.ContainsKey("auctionDate") || !record.ContainsKey("competitiveAuctionCloseDate"))
            {
                return record;
            }
            var normalized = (JObject)record.DeepClone();
            foreach (var (name, sources) in RenamedFields)
            {
                var missing = sources.FirstOrDefault(source => !record.ContainsKey(source));
                if (missing is not null)
                {
                    throw new InvalidOperationException($"USTreasuryAuctionsDownloader.Normalize(): the API answered a record without '{missing}'");
                }
                normalized[name] = sources.Select(source => Text(record, source)).FirstOrDefault(value => value.Length > 0) ?? string.Empty;
            }
            foreach (var required in new[] { NoncompetitiveInsideOffering, "type" })
            {
                if (!record.ContainsKey(required))
                {
                    throw new InvalidOperationException($"USTreasuryAuctionsDownloader.Normalize(): the API answered a record without '{required}'");
                }
            }
            // The flags come back empty on records updated since 2026-10-19, while type always carries TIPS, FRN
            // or CMB, consistent with the flags on all 11,151 records that have them
            var type = Text(record, "type");
            if (type.Length == 0)
            {
                throw new InvalidOperationException($"USTreasuryAuctionsDownloader.Normalize(): the API answered CUSIP {Text(record, "cusip")} without a type");
            }
            normalized["tips"] = type == "TIPS" ? "Yes" : "No";
            normalized["floatingRate"] = type == "FRN" ? "Yes" : "No";
            normalized["cashManagementBillCMB"] = type == "CMB" ? "Yes" : "No";
            // Noncompetitive accepted, as published before, left out the FIMA noncompetitive awards
            var inside = Text(record, NoncompetitiveInsideOffering);
            normalized["noncompetitiveAccepted"] = decimal.TryParse(inside, NumberStyles.Any, CultureInfo.InvariantCulture, out var awarded)
                ? (awarded - (decimal.TryParse(Text(record, "fimaNoncompetitiveParAwarded"), NumberStyles.Any, CultureInfo.InvariantCulture, out var fima) ? fima : 0m)).ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            return normalized;
        }

        private async Task<string> HttpGetAsync(string url, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                // At most one request a second
                var wait = _lastRequestUtc + RequestInterval - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken);
                }
                _lastRequestUtc = DateTime.UtcNow;
                try
                {
                    using var response = await _client.GetAsync(url, cancellationToken);
                    if (!response.IsSuccessStatusCode && attempt < 5 && RetryDelay(response, attempt) is { } delay)
                    {
                        Log.Trace($"USTreasuryAuctionsDownloader.HttpGetAsync(): {(int)response.StatusCode} from {url}, retry {attempt} in {delay.TotalSeconds:0} s");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        // A rejected request fails the same way on every attempt: fail now
                        throw new InvalidOperationException($"{(int)response.StatusCode} from {url}");
                    }
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (Exception error) when ((error is HttpRequestException || error is IOException || error is TaskCanceledException)
                    && attempt < 5 && !cancellationToken.IsCancellationRequested)
                {
                    Log.Trace($"USTreasuryAuctionsDownloader.HttpGetAsync(): {error.Message} from {url}, retry {attempt}");
                    await Task.Delay(TimeSpan.FromSeconds(5 * attempt), cancellationToken);
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

        // A published auction is its date and term as auctioned, so a corrected record is not new; the CUSIP is not published
        private readonly record struct Result(string Ticker, string Identity, string AuctionKey, DateTime EndTime, string Line, DateTime? Updated);

        // Date, term, issue, maturity, coupon and spread: what a correction of the flags that pick a benchmark
        // leaves alone, and distinct across benchmarks (the 2-year note and FRN of 2015-01-28 differ in coupon)
        private static string GetAuctionKey(string line)
        {
            var csv = line.Split(',');
            return string.Join("|", csv[0], csv[2], csv[3], csv[4], csv[6], csv[7]);
        }

        private static string GetSecurityKey(string line)
        {
            var csv = line.Split(',');
            return string.Join("|", csv[3], csv[4], csv[6], csv[7]);
        }

        private static DateTime? ParseUpdated(string value)
        {
            return value is { Length: >= 19 } && DateTime.TryParseExact(value[..19], "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated)
                ? updated
                : null;
        }

        private static string GetIdentity(DateTime auctionDate, string term)
        {
            return $"{auctionDate:yyyyMMdd}|{Clean(term)}";
        }

        private static string GetIdentity(string line)
        {
            var csv = line.Split(',');
            return $"{csv[0]}|{csv[2]}";
        }

        /// <summary>
        /// The results not published yet. Date and term match a published auction; when the API holds more
        /// auctions of one date and term than the file (two cash management bills of the same term on one day,
        /// one with results at 13:30 and the other later), the extra ones are those whose line differs from
        /// every published one.
        /// </summary>
        private static List<Result> SelectNew(List<Result> results, Dictionary<string, List<string>> published)
        {
            var publishedByIdentity = published
                .SelectMany(pair => pair.Value.Select(line => (Key: $"{pair.Key}|{GetIdentity(line)}", Line: line)))
                .ToLookup(entry => entry.Key, entry => WithoutEndTime(entry.Line));
            var publishedTickerByAuction = published
                .SelectMany(pair => pair.Value.Select(line => (Key: GetAuctionKey(line), Ticker: pair.Key)))
                .ToLookup(entry => entry.Key, entry => entry.Ticker);
            var publishedBySecurity = published
                .SelectMany(pair => pair.Value.Select(line => (Key: $"{pair.Key}|{GetSecurityKey(line)}", Line: line)))
                .ToLookup(entry => entry.Key, entry => entry.Line);
            List<Result> selected = [];
            foreach (var group in results.GroupBy(row => $"{row.Ticker}|{row.Identity}"))
            {
                var known = publishedByIdentity[group.Key].ToList();
                var missing = group.Count() - known.Count;
                if (missing > 0)
                {
                    selected.AddRange(group.Where(row => !known.Contains(WithoutEndTime(row.Line)))
                        // Latest first: a corrected published auction keeps its earlier stamp and must not
                        // take the place of a later new one
                        .OrderByDescending(row => row.EndTime).ThenBy(row => row.Line, StringComparer.Ordinal).Take(missing));
                }
            }
            return selected.Where(row =>
            {
                // A correction of the flags that pick a benchmark moves an auction to another file
                var other = publishedTickerByAuction[row.AuctionKey].FirstOrDefault(ticker => ticker != row.Ticker);
                if (other is not null)
                {
                    Log.Error($"USTreasuryAuctionsDownloader.RunAsync(): skipped {row.Ticker} auction already published in {other}: {row.Line}");
                    return false;
                }
                // A correction of the date or term gives a published security another identity in the same file;
                // issue, maturity, coupon and spread never repeat with another date or term within a benchmark
                var corrected = publishedBySecurity[$"{row.Ticker}|{GetSecurityKey(row.Line)}"].FirstOrDefault(line => GetIdentity(line) != row.Identity);
                if (corrected is not null)
                {
                    Log.Error($"USTreasuryAuctionsDownloader.RunAsync(): skipped {row.Ticker} auction whose date or term was corrected from {corrected.Split(',')[0]} {corrected.Split(',')[2]}: {row.Line}");
                    return false;
                }
                return true;
            }).ToList();
        }

        private static string WithoutEndTime(string line)
        {
            var csv = line.Split(',');
            return string.Join(",", csv.Where((_, index) => index != 1));
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Replace(",", string.Empty).Trim();
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
            // The date part only, so a change in the time part (milliseconds, a zone) still reads
            return value is { Length: >= 10 } && DateTime.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
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

        private static string StandardTenorAtOrAbove(string term)
        {
            var years = Regex.Match(term, @"(\d+)-Year");
            var months = Regex.Match(term, @"(\d+)-Month");
            var totalMonths = (years.Success ? int.Parse(years.Groups[1].Value, CultureInfo.InvariantCulture) * 12 : 0)
                + (months.Success ? int.Parse(months.Groups[1].Value, CultureInfo.InvariantCulture) : 0);
            var tenor = StandardTenors.FirstOrDefault(years => years * 12 >= totalMonths);
            return tenor > 0 ? $"{tenor}Y" : CompactTerm(term);
        }

        private static bool TryParseClose(string value, out TimeSpan close)
        {
            var parsed = DateTime.TryParseExact(value, ClosingTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
            close = time.TimeOfDay;
            return parsed;
        }

        /// <summary>
        /// A new result not there when its own run fetched was posted late, and reaches live with a later run.
        /// Two signs, either enough: its run is at or before the last run the folder holds (that run fetched
        /// without it), or the record was updated after its run started (the posting moves it). It is stamped
        /// at the first run after both. Neither reads the clock, so a run whose folder is behind rewrites the
        /// same line. Corrections of the date, term or benchmark flags of a published auction never get here:
        /// identity, the security and the cross-file key catch them first.
        /// </summary>
        private static Result LateStamp(Result row, DateTime? lastRun)
        {
            var ownRun = row.EndTime - DeliveryMargin;
            var run = ownRun;
            if (lastRun >= ownRun)
            {
                run = Later(run, ScheduledRunAfter(lastRun.Value));
            }
            if (row.Updated > ownRun)
            {
                run = Later(run, ScheduledRunAfter(row.Updated.Value));
            }
            if (run == ownRun)
            {
                return row;
            }
            var endTime = run + DeliveryMargin;
            var csv = row.Line.Split(',');
            csv[1] = endTime.ToStringInvariant("yyyyMMdd HH:mm");
            return row with { EndTime = endTime, Line = string.Join(",", csv) };
        }

        private static DateTime Later(DateTime first, DateTime second) => first > second ? first : second;

        private DateTime? ReadLastRun(DateTime currentRun)
        {
            var path = Path.Combine(_processedDirectory, LastRunFile);
            if (!File.Exists(path) || !DateTime.TryParseExact(File.ReadAllText(path).Trim(), "yyyyMMdd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var run))
            {
                return null;
            }
            if (run > ScheduledRunAfter(currentRun))
            {
                // Ahead of the clock by more than a run: a bad clock or a stray file, not a run that happened
                Log.Error($"USTreasuryAuctionsDownloader.RunAsync(): ignored {LastRunFile} {run:yyyy-MM-dd HH:mm}, after this run {currentRun:yyyy-MM-dd HH:mm}");
                return null;
            }
            return run;
        }

        private void WriteLastRun(DateTime currentRun, DateTime? previous)
        {
            // Written on every successful run, rows or not, so the next run knows this one fetched
            var run = previous > currentRun ? previous.Value : currentRun;
            Directory.CreateDirectory(_destinationDirectory);
            File.WriteAllText(Path.Combine(_destinationDirectory, LastRunFile), run.ToStringInvariant("yyyyMMdd HH:mm"));
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
