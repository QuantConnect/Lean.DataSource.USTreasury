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

namespace QuantConnect.DataSource
{
    /// <summary>
    /// Tickers of the U.S. Treasury auction benchmarks auctioned today. A reopening belongs to the benchmark
    /// of the security it reopens, so a 9-year 10-month note is a 10-year note. Retired benchmarks keep their
    /// own ticker built the same way, for example NOTE4Y or BOND15Y.
    /// </summary>
    public static class USTreasuryAuctionBenchmarks
    {
        /// <summary>Treasury bills</summary>
        public static class Bills
        {
            /// <summary>4-week bill</summary>
            public const string FourWeek = "BILL4W";
            /// <summary>6-week bill</summary>
            public const string SixWeek = "BILL6W";
            /// <summary>8-week bill</summary>
            public const string EightWeek = "BILL8W";
            /// <summary>13-week bill</summary>
            public const string ThirteenWeek = "BILL13W";
            /// <summary>17-week bill</summary>
            public const string SeventeenWeek = "BILL17W";
            /// <summary>26-week bill</summary>
            public const string TwentySixWeek = "BILL26W";
            /// <summary>52-week bill</summary>
            public const string FiftyTwoWeek = "BILL52W";
        }

        /// <summary>Treasury notes</summary>
        public static class Notes
        {
            /// <summary>2-year note</summary>
            public const string TwoYear = "NOTE2Y";
            /// <summary>3-year note</summary>
            public const string ThreeYear = "NOTE3Y";
            /// <summary>5-year note</summary>
            public const string FiveYear = "NOTE5Y";
            /// <summary>7-year note</summary>
            public const string SevenYear = "NOTE7Y";
            /// <summary>10-year note</summary>
            public const string TenYear = "NOTE10Y";
        }

        /// <summary>Treasury bonds</summary>
        public static class Bonds
        {
            /// <summary>20-year bond</summary>
            public const string TwentyYear = "BOND20Y";
            /// <summary>30-year bond</summary>
            public const string ThirtyYear = "BOND30Y";
        }

        /// <summary>Treasury Inflation-Protected Securities</summary>
        public static class Tips
        {
            /// <summary>5-year TIPS</summary>
            public const string FiveYear = "TIPS5Y";
            /// <summary>10-year TIPS</summary>
            public const string TenYear = "TIPS10Y";
            /// <summary>30-year TIPS</summary>
            public const string ThirtyYear = "TIPS30Y";
        }

        /// <summary>Floating rate notes</summary>
        public static class FloatingRateNotes
        {
            /// <summary>2-year floating rate note</summary>
            public const string TwoYear = "FRN2Y";
        }

        /// <summary>Cash management bills of every term, auctioned irregularly to cover short funding needs</summary>
        public const string CashManagementBills = "CMB";
    }
}
