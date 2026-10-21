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
using System.Linq;
using NodaTime;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Util;

namespace QuantConnect.DataSource
{
    /// <summary>
    /// Every U.S. Treasury auction of one benchmark whose results arrive at the same time. Almost always a
    /// single auction; cash management bills, and a few days in 1984, 1985, 1986 and 2008, carry several.
    /// Auctions are folded by end time, so they can come from different days (a result posted late reaches
    /// the next day's run): read each auction's own <see cref="BaseData.Time"/> for its date, not the
    /// collection's.
    /// </summary>
    public class USTreasuryAuctions : BaseDataCollection
    {
        // Nothing may be declared here but the overrides below. LEAN builds the collection itself and sets
        // only its symbol and timestamps, so a member added here would stay empty on every point read.

        private static readonly USTreasuryAuction _factory = new();

        /// <summary>
        /// Location of the source file: one file per benchmark holding every auction of it, folded by end time
        /// </summary>
        public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
        {
            var source = _factory.GetSource(config, date, isLiveMode);
            return new SubscriptionDataSource(source.Source, source.TransportMedium, FileFormat.FoldingCollection);
        }

        /// <summary>
        /// Reads one line of the file into one auction result. The engine folds the lines with the same end
        /// time into one collection.
        /// </summary>
        public override BaseData Reader(SubscriptionDataConfig config, string line, DateTime date, bool isLiveMode)
        {
            return _factory.Reader(config, line, date, isLiveMode);
        }

        /// <summary>
        /// Creates a copy of the instance
        /// </summary>
        public override BaseData Clone()
        {
            return new USTreasuryAuctions
            {
                Symbol = Symbol,
                Time = Time,
                EndTime = EndTime,
                Data = Data?.ToList(point => point.Clone())
            };
        }

        /// <summary>
        /// Data time zone, New York
        /// </summary>
        public override DateTimeZone DataTimeZone() => _factory.DataTimeZone();

        /// <summary>
        /// Supported resolutions
        /// </summary>
        public override List<Resolution> SupportedResolutions() => _factory.SupportedResolutions();

        /// <summary>
        /// Default resolution
        /// </summary>
        public override Resolution DefaultResolution() => _factory.DefaultResolution();

        /// <summary>
        /// Sparse data: a benchmark is auctioned weekly at most
        /// </summary>
        public override bool IsSparseData() => _factory.IsSparseData();

        /// <summary>
        /// Not linked to a traded security
        /// </summary>
        public override bool RequiresMapping() => _factory.RequiresMapping();

        /// <summary>
        /// String representation for debugging
        /// </summary>
        public override string ToString()
        {
            return $"[{string.Join(",", Data?.Select(point => point.ToString()) ?? [])}]";
        }
    }
}
