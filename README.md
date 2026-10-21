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
On 2026-10-20 TreasuryDirect renamed most fields (`auctionDate` became `competitiveAuctionCloseDate`, `totalAccepted`
`grandTotalParAwarded`, and so on). The processor maps the new names back, matched value by value against the 11,154
records downloaded before the change: a rebuild from the renamed API gives the same 390,040 values and stamps, only
written with the precision the API now uses. A source field missing from a renamed record fails the run.

## Format

One file per benchmark, one line per auction, no header, sorted by `EndTime`. A reopening belongs to the benchmark of
the security it reopens (a 9-year 10-month note is NOTE10Y); a term within three months of a whole year is that year (the
29-year 9-month bond of August 2007 is BOND30Y); bills use the term as auctioned; every cash management bill is in CMB.
Retired benchmarks keep a ticker built the same way (NOTE4Y, BOND15Y, NOTE3Y6M). The CUSIP is not published.

Several auctions of one benchmark can share an `EndTime` (cash management bills, and six days in 1984, 1985, 1986 and
2008), so a data point is a `USTreasuryAuctions` collection holding every `USTreasuryAuction` of that moment.
Subscribing to `USTreasuryAuction` directly also works, but a slice keeps one auction per symbol, so it misses the
auctions that share an `EndTime` with another: 118 of the 213 cash management bills of 2020 and early 2021 arrive.

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

- the first scheduled run (13:30 or 15:30 New York) after the posting time, taken as the competitive close plus five
  minutes (a result posted the minute a run starts is left to the next run),
- plus 40 minutes: LEAN live re-reads a local file every 30 minutes, and the job (or its redundancy five minutes later)
  starts, runs and syncs in about one minute (measured 36 seconds to 1 minute 22 seconds).

So 14:10 for a result posted before 13:30, 16:10 for one posted before 15:30 or with no published closing time (the API has
closing times only from 2008), and 14:10 of the next business day after that. That is the stamp of the history and of
every result posted on time; it never reads the run's clock, so a run that reads a published folder not yet showing an
earlier run's rows rewrites them with the same lines, even across days.

A result posted late, after the run its close points to, is stamped at the first run that can have it, plus 40 minutes.
Two signs show it, either enough:

- Every successful run writes `lastrun.txt` next to the benchmark files with its scheduled run (`yyyyMMdd HH:mm`, New
  York). A new result whose own run is at or before that one was not there when that run fetched.
- The record's update time is after its own run started: posting a result updates the record.

Both come from the folder and the record, not the clock, so a run whose folder is behind rewrites the same line. A
correction of the date, term or benchmark flags of a published auction never counts as late: it is recognized as
published first (see below). The history rebuild can not tell past late postings from
same-day corrections (the update time moved past their run for 10 of the 11,148 auctions, 2015 to 2025) and keeps
their close-based stamp.

Documented risks:

- Runs that fail: if the 13:30 job and its redundancy both fail, the 15:30 run publishes that morning's results with
  their 14:10 stamp, and a late result is stamped at the first run after it was posted even if that run failed; if every run of the day fails, the next scheduled day's first run publishes them stamped on their own
  day, a day before live gets them, or three after a Friday.
- A result corrected after its own run and rewritten by a run whose folder does not show it yet looks late, and a late
  result rewritten from a folder more than about a day behind can look on time: either gets another stamp, so live
  receives it twice. A replay of 2025 and 2026 with 5% late postings, 3% corrections, 3% failed runs and the folder up
  to 14 hours behind moved no stamp.

A published auction is recognized by its date and term, so a record the Treasury corrects later is not taken for a new
one; when the API holds more auctions of one date and term than the file, the extra ones are new. An auction already in
another benchmark's file (a correction of the flags that pick the benchmark), or a security already in the file under
another date or term (a correction of those), is not added again. A new auction stamped
at or before the last row of its file is held back with an error in the log, and the run goes on with the other
auctions and benchmarks: live has already emitted that time and would never deliver it. A record with an unreadable
date or closing time is skipped with an error; a daily run fails when no record is readable, or when it selects no
other new result and a new one has an unreadable closing time; a rebuild fails on any unreadable date or closing time.

## Production job

- Production: `30 13,15 * * 1-5`, America/New_York, Date Offset 0, chain "U.S. Treasury Auctions Processing"
- Redundancy: `35 13,15 * * 1-5`, America/New_York, Date Offset 0, the same chain name
- Scheduled jobs sync Once per scheduled run of the job that synced (`ConsumerSynchronizer.ShouldSync` and
  `JobSchedule.Ready` in LeanCloud.Data): both production runs publish, and a redundancy run publishes only when the
  production run of its slot did not
- Processing date: the New York date of the run, in summer and winter
- Config: `dataset-name` = `auctions`
- Every successful run writes `lastrun.txt`, so every run syncs even without new results

DataFleet sets the deployment date to the UTC date of the run minus the Date Offset (`MySqlDatabaseJobProvider` in
LeanCloud.Data), which at 13:30 and 15:30 New York is the New York date too. The processor refuses any other date, so a job with Date Offset 1 fails instead of stamping a
day's results as if they had arrived on time. It also refuses to append to an empty published folder; the history is
seeded once with `process-all` = `true`.

## Processing duration

- Full history (`process-all`): 49 seconds, 11,151 auctions in 24 files
- Daily run: about 1 second
