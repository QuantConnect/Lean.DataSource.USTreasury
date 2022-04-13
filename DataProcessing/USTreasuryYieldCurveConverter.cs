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

using QuantConnect.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace QuantConnect.DataProcessing
{
    public class USTreasuryYieldCurveConverter
    {
        private readonly DirectoryInfo _sourceDirectory;
        private readonly DirectoryInfo _destinationDirectory;

        public USTreasuryYieldCurveConverter(string sourceDirectory, string destinationDirectory)
        {
            _sourceDirectory = new DirectoryInfo(sourceDirectory);
            _destinationDirectory = new DirectoryInfo(destinationDirectory);
            _destinationDirectory.Create();
        }

        /// <summary>
        /// Converts the U.S. Treasury yield curve data to CSV format
        /// </summary>
        public void Convert()
        {
            Log.Trace("USTreasuryYieldCurveRateConverter.Convert(): Begin converting U.S. Treasury yield curve rate data");

            var finalPath = Path.Combine(_destinationDirectory.FullName, "yieldcurverates.csv");
            var csv = new List<string>();
            var sortedFilteredData = new List<string>();

            // data starts at 1990
            for (int year = 1990; year <= DateTime.Now.Year; year++)
            {
                var rawFile = new FileInfo(Path.Combine(_sourceDirectory.FullName, $"yieldcurverates_{year}.xml"));
                if (!rawFile.Exists)
                {
                    throw new FileNotFoundException($"Failed to find yield curve rates file: {rawFile.FullName}");
                }

                using (var stream = rawFile.OpenText())
                {
                    Log.Trace("USTreasuryYieldCurveConverter.Convert(): Begin deserialization of raw XML data");
                    var xmlData = (feed) new XmlSerializer(typeof(feed))
                        .Deserialize(stream);

                    // I don't think this should happen, but let's make sure before we work with the type
                    if (xmlData == null)
                    {
                        throw new InvalidOperationException("XML data is null. Perhaps we're deserializing the wrong XML data?");
                    }

                    sortedFilteredData.AddRange(xmlData.entry.SelectMany(x => x.content)
                        .OrderBy(x => Parse.DateTime(x.properties.NEW_DATE.Value))
                        .Select(entry =>
                        {
                            var data = new List<string>
                            {
                                Parse.DateTime(entry.properties.NEW_DATE.Value).Date.ToStringInvariant(DateFormat.EightCharacter),
                                entry.properties.BC_1MONTH != null ? entry.properties.BC_1MONTH.Value : null,
                                entry.properties.BC_2MONTH != null ? entry.properties.BC_2MONTH.Value : null,
                                entry.properties.BC_3MONTH != null ? entry.properties.BC_3MONTH.Value : null,
                                entry.properties.BC_6MONTH != null ? entry.properties.BC_6MONTH.Value : null,
                                entry.properties.BC_1YEAR != null ? entry.properties.BC_1YEAR.Value : null,
                                entry.properties.BC_2YEAR != null ? entry.properties.BC_2YEAR.Value : null,
                                entry.properties.BC_3YEAR != null ? entry.properties.BC_3YEAR.Value : null,
                                entry.properties.BC_5YEAR != null ? entry.properties.BC_5YEAR.Value : null,
                                entry.properties.BC_7YEAR != null ? entry.properties.BC_7YEAR.Value : null,
                                entry.properties.BC_10YEAR != null ? entry.properties.BC_10YEAR.Value : null,
                                entry.properties.BC_20YEAR != null ? entry.properties.BC_20YEAR.Value : null,
                                entry.properties.BC_30YEAR != null ? entry.properties.BC_30YEAR.Value : null
                            };

                            return string.Join(",", data);
                        })
                        .ToList());
                }
            }

            var finalCsv = sortedFilteredData
                .OrderBy(x => DateTime.ParseExact(x.Split(',').First(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal))
                .ToList();

            Log.Trace($"USTreasuryYieldCurveConverter.Convert(): Appending {finalCsv.Count} lines to file: {finalPath}");
            File.WriteAllLines(finalPath, finalCsv);

            Log.Trace($"USTreasuryYieldCurveConverter.Convert(): Data conversion complete!");
        }
    }
}
