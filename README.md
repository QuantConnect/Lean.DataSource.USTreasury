# Lean.DataSource.USTreasury
Example production implementation for our data marketplace U.S. Department of Treasury datasets: the Yield Curve Rates and the Treasury Auctions.

# Implementing your own data source
To learn more about implementing your own data source for our marketplace, visit the [LeanDataSdk](https://github.com/QuantConnect/LeanDataSdk) repository for more information.

# Datasets

The processor builds one dataset per run, selected by the `dataset-name` config key:

| dataset-name | Dataset | Output |
| --- | --- | --- |
| `yieldcurve` (default) | US Treasury Yield Curve, class `USTreasuryYieldCurveRate` | `alternative/ustreasury/yieldcurverates.csv` |
| `auctions` | US Treasury Auctions, classes `USTreasuryAuctions` and `USTreasuryAuction` | `alternative/ustreasury/auctions/{benchmark}.csv` |

The yield curve stays the default because its production jobs predate the key and do not set it.

# US Treasury Auctions

## Source

The [TreasuryDirect auction query API](https://www.treasurydirect.gov/TA_WS/securities/search), public, no key and no
authentication. The data is a U.S. government work in the public domain. The API filters on the issue date, not on the
auction date, and lists an announced auction before its results with every result field empty; the processor skips those.

## Format

One file per benchmark, one line per auction, no header, sorted by `EndTime`. A reopening belongs to the benchmark of
the security it reopens (a 9-year 10-month note is NOTE10Y); a term within three months of a whole year is that year (the
29-year 9-month bond of August 2007 is BOND30Y); bills use the term as auctioned; every cash management bill is in CMB.
Retired benchmarks keep a ticker built the same way (NOTE4Y, BOND15Y, NOTE3Y6M). The CUSIP is not published.

Several auctions of one benchmark can share an `EndTime` (cash management bills, and six days in 1984, 1985, 1986 and
2008), so a data point is a `USTreasuryAuctions` collection holding every `USTreasuryAuction` of that moment.

Columns: auction date, end time (`yyyyMMdd HH:mm`), security term as auctioned, issue date, maturity date, reopening
(1/0), interest rate, spread, offering amount, total tendered, total accepted, competitive tendered, competitive accepted,
noncompetitive accepted, bid-to-cover ratio, high yield, low yield, median yield, high discount rate, low discount rate,
median discount rate, high investment rate, high discount margin, low discount margin, median discount margin, high price,
allocation percentage, direct bidder tendered, direct bidder accepted, indirect bidder tendered, indirect bidder accepted,
primary dealer tendered, primary dealer accepted, SOMA accepted, FIMA noncompetitive accepted.

Coverage: offering, tendered, accepted, rates and price from 1979; bid-to-cover from late 1999 (three earlier auctions carry one); the
competitive, noncompetitive and bidder breakdown from 2008-04-07; floating rate notes from 2014. Values are copied as
published: TIPS yields and floating rate note spreads can be negative, and the Treasury held $25 million operational
auctions in 2019 and 2022.

## Publication lag and EndTime

Results are posted a few minutes after the competitive close (11:30 for bills, 13:00 for coupons): median 3.4 minutes and
95th percentile 4.8 minutes over the 4,560 auctions posted the same day since 2014. `EndTime` is not the posting time but
the time a live algorithm is certain to have the result, the same rule for the history and for new rows:

- the first scheduled run (13:30 or 15:30 New York) at or after the posting time: the record's update time when it
  falls on the auction day and after the close, otherwise the close plus five minutes,
- plus 40 minutes: LEAN live re-reads a local file every 30 minutes, and the job (or its redundancy five minutes later)
  starts, runs and syncs in about one minute (measured 36 seconds to 1 minute 22 seconds).

So 14:10 for a result posted by 13:30, 16:10 for one posted by 15:30 or with no published closing time (the API has
closing times only from 2008), and 14:10 of the next business day after that. A result first seen on a later day, because
every run of its day failed, is stamped at that day's first run. The stamp depends only on the auction, so a run that
reads a published folder not yet showing an earlier run's rows rewrites them with the same lines. The one case it does not
cover is the 13:30 job and its redundancy both failing: the 15:30 run then publishes that morning's results with their
14:10 stamp. `EndTime` never goes backwards within a file.

## Production job

- Production: `30 13,15 * * 1-5`, America/New_York, Date Offset 0, chain "U.S. Treasury Auctions Processing"
- Redundancy: `35 13,15 * * 1-5`, America/New_York, Date Offset 0, the same chain name
- Scheduled jobs sync Once per scheduled run of the job that synced (`ConsumerSynchronizer.ShouldSync` and
  `JobSchedule.Ready` in LeanCloud.Data): both production runs publish, and a redundancy run publishes only when the
  production run of its slot did not
- Processing date: the New York date of the run, in summer and winter
- Config: `dataset-name` = `auctions`
- A run with no new results writes nothing, so it does not count as a sync

The processor refuses a deployment date other than today or yesterday (UTC) and refuses to append to an empty published
folder. The history is seeded once with `process-all` = `true`.

## Processing duration

- Full history (`process-all`): 51 seconds, 11,142 auctions in 24 files
- Daily run: about 1 second
