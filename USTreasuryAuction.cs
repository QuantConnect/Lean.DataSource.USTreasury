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
using NodaTime;
using QuantConnect.Data;
using QuantConnect.Util;

namespace QuantConnect.DataSource
{
    /// <summary>
    /// The result of one U.S. Treasury marketable securities auction: how much was offered and sold,
    /// the yield or rate it cleared at, the demand it drew and who bought it.
    /// </summary>
    public class USTreasuryAuction : BaseData
    {
        /// <summary>Number of columns a line of the file carries.</summary>
        public const int ExpectedColumns = 35;

        private const string DateFormat = "yyyyMMdd";
        private const string EndTimeFormat = "yyyyMMdd HH:mm";

        private DateTime? _endTime;

        /// <summary>
        /// The term of the security as auctioned, for example "10-Year" for a new issue or "9-Year 10-Month"
        /// for a reopening of an earlier 10-year note
        /// </summary>
        public string SecurityTerm { get; set; }

        /// <summary>Date the security is issued</summary>
        public DateTime? IssueDate { get; set; }

        /// <summary>Date the security matures</summary>
        public DateTime? MaturityDate { get; set; }

        /// <summary>True when the auction sold more of a security that was already outstanding</summary>
        public bool IsReopening { get; set; }

        /// <summary>Annual coupon rate of the security in percent, empty for bills and floating rate notes</summary>
        public decimal? InterestRate { get; set; }

        /// <summary>Fixed spread over the index rate of a floating rate note in percent</summary>
        public decimal? Spread { get; set; }

        /// <summary>Amount offered to the public, in dollars of face value</summary>
        public decimal? OfferingAmount { get; set; }

        /// <summary>Total amount of bids received, in dollars of face value</summary>
        public decimal? TotalTendered { get; set; }

        /// <summary>Total amount sold, in dollars of face value</summary>
        public decimal? TotalAccepted { get; set; }

        /// <summary>Amount of competitive bids received, in dollars of face value</summary>
        public decimal? CompetitiveTendered { get; set; }

        /// <summary>Amount of competitive bids accepted, in dollars of face value</summary>
        public decimal? CompetitiveAccepted { get; set; }

        /// <summary>Amount of noncompetitive bids accepted, in dollars of face value</summary>
        public decimal? NoncompetitiveAccepted { get; set; }

        /// <summary>Ratio of the amount bid to the amount accepted, as published by the Treasury</summary>
        public decimal? BidToCoverRatio { get; set; }

        /// <summary>Highest accepted yield in percent, the rate the auction cleared at for notes, bonds and TIPS</summary>
        public decimal? HighYield { get; set; }

        /// <summary>Lowest accepted yield in percent</summary>
        public decimal? LowYield { get; set; }

        /// <summary>Median yield of the accepted competitive bids in percent</summary>
        public decimal? MedianYield { get; set; }

        /// <summary>Highest accepted discount rate in percent, the rate the auction cleared at for bills</summary>
        public decimal? HighDiscountRate { get; set; }

        /// <summary>Lowest accepted discount rate in percent</summary>
        public decimal? LowDiscountRate { get; set; }

        /// <summary>Median discount rate of the accepted competitive bids in percent</summary>
        public decimal? MedianDiscountRate { get; set; }

        /// <summary>Bond equivalent yield of the high discount rate of a bill in percent</summary>
        public decimal? HighInvestmentRate { get; set; }

        /// <summary>Highest accepted discount margin in percent, the rate the auction cleared at for floating rate notes</summary>
        public decimal? HighDiscountMargin { get; set; }

        /// <summary>Lowest accepted discount margin in percent</summary>
        public decimal? LowDiscountMargin { get; set; }

        /// <summary>Median discount margin of the accepted competitive bids in percent</summary>
        public decimal? MedianDiscountMargin { get; set; }

        /// <summary>Price per 100 of face value at the highest accepted yield or rate</summary>
        public decimal? HighPrice { get; set; }

        /// <summary>Percent of the bids at the highest accepted yield or rate that were filled</summary>
        public decimal? AllocationPercentage { get; set; }

        /// <summary>Amount bid by direct bidders, in dollars of face value</summary>
        public decimal? DirectBidderTendered { get; set; }

        /// <summary>Amount accepted from direct bidders, in dollars of face value</summary>
        public decimal? DirectBidderAccepted { get; set; }

        /// <summary>Amount bid by indirect bidders, which include foreign central banks, in dollars of face value</summary>
        public decimal? IndirectBidderTendered { get; set; }

        /// <summary>Amount accepted from indirect bidders, in dollars of face value</summary>
        public decimal? IndirectBidderAccepted { get; set; }

        /// <summary>Amount bid by primary dealers, in dollars of face value</summary>
        public decimal? PrimaryDealerTendered { get; set; }

        /// <summary>Amount accepted from primary dealers, in dollars of face value</summary>
        public decimal? PrimaryDealerAccepted { get; set; }

        /// <summary>Amount accepted from the Federal Reserve's System Open Market Account, in dollars of face value</summary>
        public decimal? SomaAccepted { get; set; }

        /// <summary>Amount of noncompetitive bids accepted from foreign and international monetary authorities, in dollars of face value</summary>
        public decimal? FimaNoncompetitiveAccepted { get; set; }

        /// <summary>
        /// The rate the auction cleared at, in percent: the high yield for notes, bonds and TIPS, the high
        /// investment rate for bills and the high discount margin for floating rate notes
        /// </summary>
        public decimal? ClearingRate => HighYield ?? HighInvestmentRate ?? HighDiscountMargin;

        /// <summary>
        /// Time the result is available to algorithms: 14:10 New York on the auction day, 16:10 for an auction
        /// that closed at 13:25 or later or has no published closing time, and 14:10 of the next business day for
        /// one that closed at 15:25 or later
        /// </summary>
        // The job runs at 13:30 and 15:30 New York; a row is stamped 40 minutes after the first run after its
        // posting (30 minutes for LEAN live to re-read the file, 10 for the job or its redundancy and the
        // sync). The stamp depends only on the auction, so the history follows the same rule, with 16:10 for
        // auctions without a published closing time.
        public override DateTime EndTime
        {
            get => _endTime ?? Time;
            set => _endTime = value;
        }

        /// <summary>
        /// Location of the source file: one file per benchmark holding every auction of it. Read line by line;
        /// <see cref="USTreasuryAuctions"/> folds the auctions that share an end time into one point.
        /// </summary>
        public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
        {
            return new SubscriptionDataSource(
                Path.Combine(
                    Globals.DataFolder,
                    "alternative",
                    "ustreasury",
                    "auctions",
                    $"{config.Symbol.Value.ToLowerInvariant()}.csv"
                ),
                SubscriptionTransportMedium.LocalFile,
                FileFormat.Csv
            );
        }

        /// <summary>
        /// Parses one line of the file into one auction result
        /// </summary>
        public override BaseData Reader(SubscriptionDataConfig config, string line, DateTime date, bool isLiveMode)
        {
            var csv = line.Split(',');
            // Fewer columns than expected is a truncated line, skipped rather than thrown on; more columns
            // leaves room to append one later without muting the dataset
            if (csv.Length < ExpectedColumns)
            {
                return null;
            }

            var point = Parse(csv);
            point.Symbol = config.Symbol;
            return point;
        }

        /// <summary>
        /// Reads one split line into an auction result that has no symbol yet
        /// </summary>
        public static USTreasuryAuction Parse(string[] csv)
        {
            var point = new USTreasuryAuction
            {
                Time = DateTime.ParseExact(csv[0], DateFormat, CultureInfo.InvariantCulture),
                EndTime = DateTime.ParseExact(csv[1], EndTimeFormat, CultureInfo.InvariantCulture),
                SecurityTerm = csv[2],
                IssueDate = ParseDate(csv[3]),
                MaturityDate = ParseDate(csv[4]),
                IsReopening = csv[5] == "1",
                InterestRate = ParseDecimal(csv[6]),
                Spread = ParseDecimal(csv[7]),
                OfferingAmount = ParseDecimal(csv[8]),
                TotalTendered = ParseDecimal(csv[9]),
                TotalAccepted = ParseDecimal(csv[10]),
                CompetitiveTendered = ParseDecimal(csv[11]),
                CompetitiveAccepted = ParseDecimal(csv[12]),
                NoncompetitiveAccepted = ParseDecimal(csv[13]),
                BidToCoverRatio = ParseDecimal(csv[14]),
                HighYield = ParseDecimal(csv[15]),
                LowYield = ParseDecimal(csv[16]),
                MedianYield = ParseDecimal(csv[17]),
                HighDiscountRate = ParseDecimal(csv[18]),
                LowDiscountRate = ParseDecimal(csv[19]),
                MedianDiscountRate = ParseDecimal(csv[20]),
                HighInvestmentRate = ParseDecimal(csv[21]),
                HighDiscountMargin = ParseDecimal(csv[22]),
                LowDiscountMargin = ParseDecimal(csv[23]),
                MedianDiscountMargin = ParseDecimal(csv[24]),
                HighPrice = ParseDecimal(csv[25]),
                AllocationPercentage = ParseDecimal(csv[26]),
                DirectBidderTendered = ParseDecimal(csv[27]),
                DirectBidderAccepted = ParseDecimal(csv[28]),
                IndirectBidderTendered = ParseDecimal(csv[29]),
                IndirectBidderAccepted = ParseDecimal(csv[30]),
                PrimaryDealerTendered = ParseDecimal(csv[31]),
                PrimaryDealerAccepted = ParseDecimal(csv[32]),
                SomaAccepted = ParseDecimal(csv[33]),
                FimaNoncompetitiveAccepted = ParseDecimal(csv[34])
            };
            point.Value = point.ClearingRate ?? 0m;
            return point;
        }

        /// <summary>
        /// Data time zone, New York, where the Treasury holds the auctions
        /// </summary>
        public override DateTimeZone DataTimeZone() => TimeZones.NewYork;

        /// <summary>
        /// Supported resolutions, Daily only since a benchmark is auctioned at most once a day
        /// </summary>
        public override List<Resolution> SupportedResolutions() => DailyResolution;

        /// <summary>
        /// Default resolution
        /// </summary>
        public override Resolution DefaultResolution() => Resolution.Daily;

        /// <summary>
        /// Sparse data: a benchmark is auctioned weekly at most, and some only a few times a year
        /// </summary>
        public override bool IsSparseData() => true;

        /// <summary>
        /// Not linked to a traded security, so no mapping applies
        /// </summary>
        public override bool RequiresMapping() => false;

        /// <summary>
        /// Creates a copy of the instance
        /// </summary>
        public override BaseData Clone()
        {
            return new USTreasuryAuction
            {
                Symbol = Symbol,
                Time = Time,
                EndTime = EndTime,
                Value = Value,
                SecurityTerm = SecurityTerm,
                IssueDate = IssueDate,
                MaturityDate = MaturityDate,
                IsReopening = IsReopening,
                InterestRate = InterestRate,
                Spread = Spread,
                OfferingAmount = OfferingAmount,
                TotalTendered = TotalTendered,
                TotalAccepted = TotalAccepted,
                CompetitiveTendered = CompetitiveTendered,
                CompetitiveAccepted = CompetitiveAccepted,
                NoncompetitiveAccepted = NoncompetitiveAccepted,
                BidToCoverRatio = BidToCoverRatio,
                HighYield = HighYield,
                LowYield = LowYield,
                MedianYield = MedianYield,
                HighDiscountRate = HighDiscountRate,
                LowDiscountRate = LowDiscountRate,
                MedianDiscountRate = MedianDiscountRate,
                HighInvestmentRate = HighInvestmentRate,
                HighDiscountMargin = HighDiscountMargin,
                LowDiscountMargin = LowDiscountMargin,
                MedianDiscountMargin = MedianDiscountMargin,
                HighPrice = HighPrice,
                AllocationPercentage = AllocationPercentage,
                DirectBidderTendered = DirectBidderTendered,
                DirectBidderAccepted = DirectBidderAccepted,
                IndirectBidderTendered = IndirectBidderTendered,
                IndirectBidderAccepted = IndirectBidderAccepted,
                PrimaryDealerTendered = PrimaryDealerTendered,
                PrimaryDealerAccepted = PrimaryDealerAccepted,
                SomaAccepted = SomaAccepted,
                FimaNoncompetitiveAccepted = FimaNoncompetitiveAccepted
            };
        }

        /// <summary>
        /// String representation for debugging
        /// </summary>
        public override string ToString()
        {
            return $"{Symbol} {Time:yyyy-MM-dd} {SecurityTerm} clearing rate {ClearingRate} bid-to-cover {BidToCoverRatio} accepted {TotalAccepted}";
        }

        private static DateTime? ParseDate(string value)
        {
            return value.IfNotNullOrEmpty<DateTime?>(s => DateTime.ParseExact(s, DateFormat, CultureInfo.InvariantCulture));
        }

        private static decimal? ParseDecimal(string value)
        {
            return value.IfNotNullOrEmpty<decimal?>(s => decimal.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture));
        }
    }
}
