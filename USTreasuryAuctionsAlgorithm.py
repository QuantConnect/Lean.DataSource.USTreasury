# QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
# Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

from AlgorithmImports import *


class USTreasuryAuctionsAlgorithm(QCAlgorithm):
    """Holds less SPY after a 10-year note auction that drew weak demand and returns to a full position after a strong one."""

    def initialize(self):
        self.set_start_date(2020, 1, 1)
        self.set_end_date(2021, 3, 31)
        self.set_cash(100000)

        self._spy = self.add_equity("SPY", Resolution.DAILY).symbol
        self._ten_year_note = self.add_data(USTreasuryAuctions, USTreasuryAuctionBenchmarks.Notes.TEN_YEAR).symbol

    def on_data(self, slice):
        auctions = slice.get(USTreasuryAuctions).get(self._ten_year_note)
        if not auctions:
            return

        for auction in auctions.data:
            if auction.bid_to_cover_ratio is None:
                continue

            self.log(f"{auction.time:%Y-%m-%d} {auction.security_term}: high yield {auction.high_yield}, bid-to-cover {auction.bid_to_cover_ratio}")

            # Weak demand at the 10-year auction (bid-to-cover below 2.4) is read as risk-off
            self.set_holdings(self._spy, 0.5 if auction.bid_to_cover_ratio < 2.4 else 1)
