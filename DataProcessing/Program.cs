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
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using QuantConnect.Configuration;
using QuantConnect.Logging;

namespace QuantConnect.DataProcessing
{
    /// <summary>
    /// Entrypoint for the data downloader/converter
    /// </summary>
    public class Program
    {
        /// <summary>Value of the dataset-name config key that selects the yield curve</summary>
        public const string YieldCurveDatasetName = "yieldcurve";

        /// <summary>
        /// Entrypoint of the program
        /// </summary>
        /// <returns>Exit code. 0 equals successful, and any other value indicates the downloader/converter failed.</returns>
        public static async Task<int> Main()
        {
            var dataset = GetDatasetName();
            int code;
            switch (dataset)
            {
                case YieldCurveDatasetName:
                    code = RunYieldCurve();
                    break;
                case USTreasuryAuctionsDownloader.DatasetName:
                    code = await RunAuctionsAsync();
                    break;
                default:
                    Log.Error($"QuantConnect.DataProcessing.Program.Main(): unknown dataset-name '{dataset}'");
                    code = 1;
                    break;
            }
            // Exit as the yield curve always did, so a foreground thread left running can not hang the job
            Environment.Exit(code);
            return code;
        }

        /// <summary>
        /// Dataset the run builds. The yield curve jobs predate the key and carry none, so the yield curve
        /// stays the default.
        /// </summary>
        public static string GetDatasetName()
        {
            return Config.Get("dataset-name", YieldCurveDatasetName).Trim().ToLowerInvariant();
        }

        /// <summary>
        /// DataFleet sets the date to the UTC date of the run minus the job's Date Offset. The jobs run at 13:30
        /// and 15:30 New York with Date Offset 0, so a scheduled run carries today. Yesterday would be a job with
        /// Date Offset 1, which writes a day's results a day late but stamps them as if on time.
        /// </summary>
        public static bool IsScheduledDeploymentDate(DateTime deploymentDate, DateTime utcNow)
        {
            return deploymentDate == utcNow.Date;
        }

        private static int RunYieldCurve()
        {
            // Get the config values first before running. These values are set for us
            // automatically to the value set on the website when defining this data type
            var downloadDestinationDirectory = Directory.CreateDirectory(Path.GetTempPath());
            var destinationDirectory = Path.Combine(Config.Get("temp-output-directory", "/temp-output-directory"), "alternative", "ustreasury");
            
            USTreasuryYieldCurveDownloader instance = null;
            try
            {
                // Pass in the values we got from the configuration into the downloader/converter.
                instance = new USTreasuryYieldCurveDownloader(downloadDestinationDirectory.FullName);
            }
            catch (Exception err)
            {
                Log.Error(err, $"The downloader {nameof(USTreasuryYieldCurveDownloader)} failed to be constructed");
                return 1;
            }

            // No need to edit anything below here for most use cases.
            // The downloader/converter is ran and cleaned up for you safely here.
            try
            {
                // Run the data downloader.
                instance.Download();
            }
            catch (Exception err)
            {
                Log.Error(err, $"The downloader {nameof(USTreasuryYieldCurveDownloader)} exited unexpectedly");
                return 1;
            }
            
            try 
            {
                var converter = new USTreasuryYieldCurveConverter(downloadDestinationDirectory.FullName, destinationDirectory);
                converter.Convert();
            }
            catch (Exception err) 
            {
                Log.Error(err, $"The converter {nameof(USTreasuryYieldCurveConverter)} exited unexpectedly");
                return 1;
            }

            Log.Trace($"QuantConnect.DataProcessing.Program.Main(): Successfully completed download/conversion of U.S. Treasury Yield Curve data");
            return 0;
        }

        private static async Task<int> RunAuctionsAsync()
        {
            var deploymentDateValue = Environment.GetEnvironmentVariable("QC_DATAFLEET_DEPLOYMENT_DATE");
            if (!DateTime.TryParseExact(deploymentDateValue, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var deploymentDate))
            {
                Log.Error($"QuantConnect.DataProcessing.Program.RunAuctionsAsync(): invalid QC_DATAFLEET_DEPLOYMENT_DATE '{deploymentDateValue}'");
                return 1;
            }

            if (!IsScheduledDeploymentDate(deploymentDate, DateTime.UtcNow))
            {
                Log.Error($"QuantConnect.DataProcessing.Program.RunAuctionsAsync(): deployment date {deploymentDate:yyyy-MM-dd} is not today (UTC); the jobs run with Date Offset 0");
                return 1;
            }

            var relativePath = Path.Combine("alternative", "ustreasury", "auctions");
            var destinationDirectory = Path.Combine(Config.Get("temp-output-directory", "/temp-output-directory"), relativePath);
            var processedDirectory = Path.Combine(Config.Get("processed-data-directory", Globals.DataFolder), relativePath);
            var processAll = Config.GetBool("process-all", false);

            try
            {
                using var downloader = new USTreasuryAuctionsDownloader(destinationDirectory, processedDirectory);
                if (!await downloader.RunAsync(deploymentDate, processAll))
                {
                    return 1;
                }
            }
            catch (Exception err)
            {
                Log.Error(err, $"The downloader {nameof(USTreasuryAuctionsDownloader)} exited unexpectedly");
                return 1;
            }

            Log.Trace($"QuantConnect.DataProcessing.Program.RunAuctionsAsync(): Successfully completed U.S. Treasury auction results for {deploymentDate:yyyy-MM-dd}");
            return 0;
        }
    }
}
