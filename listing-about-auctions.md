## Introduction

The US Treasury Auctions dataset by the Treasury Department tracks the results of U.S. Treasury marketable securities auctions. The data covers 24 bill, note, bond, TIPS and floating rate note benchmarks, starts in October 1979, and is delivered on a daily frequency. This dataset is created by parsing the auction results the Treasury publishes on TreasuryDirect. Bid-to-cover ratios start in late 1999 and the bidder breakdown starts in April 2008.

## About the Provider

The Treasury Department is the executive agency responsible for promoting economic prosperity and ensuring the financial security of the United States. The Department is responsible for a wide range of activities such as advising the President on economic and financial issues, encouraging sustainable economic growth, and fostering improved governance in financial institutions. The Department of the Treasury operates and maintains systems that are critical to the nation's financial infrastructure, such as the production of coin and currency, the disbursement of payments to the American public, revenue collection, and the borrowing of funds necessary to run the federal government.

## Getting Started

The following snippet demonstrates how to request data from the US Treasury Auctions dataset:

```python
self._ten_year_note = self.add_data(USTreasuryAuctions, USTreasuryAuctionBenchmarks.Notes.TEN_YEAR).symbol
```
```csharp
_tenYearNote = AddData<USTreasuryAuctions>(USTreasuryAuctionBenchmarks.Notes.TenYear).Symbol;
```

## Data Summary

The following table describes the dataset properties:

| Property | Value |
| --- | --- |
| Start Date | October 1979 |
| Asset Coverage | 24 Auction Benchmarks |
| Data Density | Sparse |
| Resolution | Daily |
| Timezone | New York |

## Example Applications

The US Treasury Auctions dataset enables you to trade on the demand the Treasury's debt auctions draw. Examples include the following strategies:

- Reducing duration after a long-bond auction that draws weak demand
- Following foreign demand through the share indirect bidders take at each auction
- Timing curve trades around the auction calendar of each benchmark

For more example algorithms, see Examples.

## Supported Benchmarks

The following table shows the accessor code you need to add each benchmark to your algorithm:

| Benchmark | Constant |
| --- | --- |
| 4-Week Bill | `USTreasuryAuctionBenchmarks.Bills.FourWeek` |
| 6-Week Bill | `USTreasuryAuctionBenchmarks.Bills.SixWeek` |
| 8-Week Bill | `USTreasuryAuctionBenchmarks.Bills.EightWeek` |
| 13-Week Bill | `USTreasuryAuctionBenchmarks.Bills.ThirteenWeek` |
| 17-Week Bill | `USTreasuryAuctionBenchmarks.Bills.SeventeenWeek` |
| 26-Week Bill | `USTreasuryAuctionBenchmarks.Bills.TwentySixWeek` |
| 52-Week Bill | `USTreasuryAuctionBenchmarks.Bills.FiftyTwoWeek` |
| 2-Year Note | `USTreasuryAuctionBenchmarks.Notes.TwoYear` |
| 3-Year Note | `USTreasuryAuctionBenchmarks.Notes.ThreeYear` |
| 5-Year Note | `USTreasuryAuctionBenchmarks.Notes.FiveYear` |
| 7-Year Note | `USTreasuryAuctionBenchmarks.Notes.SevenYear` |
| 10-Year Note | `USTreasuryAuctionBenchmarks.Notes.TenYear` |
| 20-Year Bond | `USTreasuryAuctionBenchmarks.Bonds.TwentyYear` |
| 30-Year Bond | `USTreasuryAuctionBenchmarks.Bonds.ThirtyYear` |
| 5-Year TIPS | `USTreasuryAuctionBenchmarks.Tips.FiveYear` |
| 10-Year TIPS | `USTreasuryAuctionBenchmarks.Tips.TenYear` |
| 30-Year TIPS | `USTreasuryAuctionBenchmarks.Tips.ThirtyYear` |
| 2-Year Floating Rate Note | `USTreasuryAuctionBenchmarks.FloatingRateNotes.TwoYear` |
| Cash Management Bills | `USTreasuryAuctionBenchmarks.CashManagementBills` |

## Meta

| Field | Value |
| --- | --- |
| name | US Treasury Auctions |
| url | us-treasury-auctions |
| vendorName | Treasury Department |
| website | https://www.treasurydirect.gov/ |
| history | October 1979 |
| reach | 24 Auction Benchmarks |
| shortDescription | Results of every U.S. Treasury bill, note, bond, TIPS and floating rate note auction since 1979 |
| priceCTA | Free in Cloud |
| delivery | cloud & download |

Tags: Financial Market Data

Licensing card:

```html
<p>Free access to US Treasury Auctions in QuantConnect Cloud for use in backtesting or live trading.</p>
<ul>
    <li>Results of every Treasury auction across 24 benchmarks since 1979</li>
    <li>Bills, notes, bonds, TIPS, floating rate notes and cash management bills</li>
    <li>Curated, clean data</li>
</ul>
```
