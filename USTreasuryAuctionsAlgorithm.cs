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

using System.Linq;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.DataSource;

namespace QuantConnect.DataLibrary.Tests
{
    /// <summary>
    /// Example algorithm using the US Treasury Auctions dataset: it holds less SPY after a 10-year note
    /// auction that drew weak demand and returns to a full position after a strong one
    /// </summary>
    public class USTreasuryAuctionsAlgorithm : QCAlgorithm
    {
        private Symbol _spy;
        private Symbol _tenYearNote;

        public override void Initialize()
        {
            SetStartDate(2020, 1, 1);
            SetEndDate(2021, 3, 31);
            SetCash(100000);

            _spy = AddEquity("SPY", Resolution.Daily).Symbol;
            _tenYearNote = AddData<USTreasuryAuctions>(USTreasuryAuctionBenchmarks.Notes.TenYear).Symbol;
        }

        public override void OnData(Slice slice)
        {
            if (!slice.Get<USTreasuryAuctions>().TryGetValue(_tenYearNote, out var auctions))
            {
                return;
            }

            foreach (USTreasuryAuction auction in auctions.Data)
            {
                if (!auction.BidToCoverRatio.HasValue)
                {
                    continue;
                }

                Log($"{auction.Time:yyyy-MM-dd} {auction.SecurityTerm}: high yield {auction.HighYield}, bid-to-cover {auction.BidToCoverRatio}");

                // Weak demand at the 10-year auction (bid-to-cover below 2.4) is read as risk-off
                SetHoldings(_spy, auction.BidToCoverRatio < 2.4m ? 0.5m : 1m);
            }
        }
    }
}
