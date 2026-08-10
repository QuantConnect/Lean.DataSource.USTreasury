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
 *
*/

using System;
using ProtoBuf;
using System.IO;
using System.Linq;
using ProtoBuf.Meta;
using Newtonsoft.Json;
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.DataSource;

namespace QuantConnect.DataLibrary.Tests
{
    [TestFixture]
    public class USTreasuryYieldCurveRateTests
    {
        [Test]
        public void JsonRoundTrip()
        {
            var expected = CreateNewInstance();
            var type = expected.GetType();
            var serialized = JsonConvert.SerializeObject(expected);
            var result = JsonConvert.DeserializeObject(serialized, type);

            AssertAreEqual(expected, result);
        }

        [Test]
        public void ProtobufRoundTrip()
        {
            var expected = CreateNewInstance();
            var type = expected.GetType();

            RuntimeTypeModel.Default[typeof(BaseData)].AddSubType(2000, type);

            using (var stream = new MemoryStream())
            {
                Serializer.Serialize(stream, expected);

                stream.Position = 0;

                var result = Serializer.Deserialize(type, stream);

                AssertAreEqual(expected, result, filterByCustomAttributes: true);
            }
        }

        [Test]
        public void ReaderParsesDateWithoutTimeZoneShift()
        {
            var config = new SubscriptionDataConfig(
                typeof(USTreasuryYieldCurveRate),
                Symbol.Create("USTYCR", SecurityType.Base, Market.USA),
                Resolution.Daily,
                TimeZones.NewYork,
                TimeZones.NewYork,
                false,
                false,
                false,
                isCustom: true);

            var line = "20190724,2.13,,2.08,2.07,1.96,1.80,1.78,1.81,1.94,2.05,2.34,2.58";
            var data = (USTreasuryYieldCurveRate)new USTreasuryYieldCurveRate()
                .Reader(config, line, new DateTime(2019, 7, 24), false);

            Assert.AreEqual(new DateTime(2019, 7, 24, 16, 30, 0), data.Time);

            // The date carries no time zone, so it must stay unspecified. A Local kind here means the
            // parse converted the value to the machine's local time, which shifts the date west of UTC.
            // Asserting the kind catches that regression even when the tests run on a UTC machine.
            Assert.AreEqual(DateTimeKind.Unspecified, data.Time.Kind);

            Assert.AreEqual(2.13m, data.OneMonth);
            Assert.IsNull(data.TwoMonth);
            Assert.AreEqual(2.58m, data.ThirtyYear);
        }

        [Test]
        public void Clone()
        {
            var expected = CreateNewInstance();
            var result = expected.Clone();

            AssertAreEqual(expected, result);
        }

        private void AssertAreEqual(object expected, object result, bool filterByCustomAttributes = false)
        {
            foreach (var propertyInfo in expected.GetType().GetProperties())
            {
                // we skip Symbol which isn't protobuffed
                if (filterByCustomAttributes && propertyInfo.CustomAttributes.Count() != 0)
                {
                    Assert.AreEqual(propertyInfo.GetValue(expected), propertyInfo.GetValue(result));
                }
            }
            foreach (var fieldInfo in expected.GetType().GetFields())
            {
                Assert.AreEqual(fieldInfo.GetValue(expected), fieldInfo.GetValue(result));
            }
        }

        private BaseData CreateNewInstance()
        {
            return new USTreasuryYieldCurveRate
            {
                Symbol = Symbol.Empty,
                Time = DateTime.Today,
                DataType = MarketDataType.Base,

                OneMonth = 0.1m,
                TwoMonth = 0.2m,
                ThreeMonth = 0.3m,
                SixMonth = 0.6m,
                OneYear = 1.2m,
                TwoYear = null,
                ThreeYear = 3.6m,
                FiveYear = 6.0m,
                SevenYear = 8.4m,
                TenYear = 12m,
                TwentyYear = 24m,
                ThirtyYear = 36m,
            };
        }
    }
}
