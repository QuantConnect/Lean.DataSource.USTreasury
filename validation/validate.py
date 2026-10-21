"""Validates the processed US Treasury Auctions output and writes a markdown report to stdout.

Usage: python validation/validate.py <folder with the benchmark csv files> <raw API json folder (optional)>
"""
import glob
import json
import os
import sys
from collections import Counter, defaultdict
from datetime import datetime

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

COLUMNS = ["time", "endtime", "security_term", "issue_date", "maturity_date", "is_reopening", "interest_rate",
           "spread", "offering_amount", "total_tendered", "total_accepted", "competitive_tendered",
           "competitive_accepted", "noncompetitive_accepted", "bid_to_cover_ratio", "high_yield", "low_yield",
           "median_yield", "high_discount_rate", "low_discount_rate", "median_discount_rate",
           "high_investment_rate", "high_discount_margin", "low_discount_margin", "median_discount_margin",
           "high_price", "allocation_percentage", "direct_bidder_tendered", "direct_bidder_accepted",
           "indirect_bidder_tendered", "indirect_bidder_accepted", "primary_dealer_tendered",
           "primary_dealer_accepted", "soma_accepted", "fima_noncompetitive_accepted"]
NUMERIC = COLUMNS[6:]
ACTIVE = ["BILL4W", "BILL13W", "BILL26W", "BILL52W", "NOTE2Y", "NOTE5Y", "NOTE10Y", "BOND30Y", "TIPS10Y", "FRN2Y"]

folder = sys.argv[1]
raw = sys.argv[2] if len(sys.argv) > 2 else None
out = os.path.dirname(os.path.abspath(__file__))


def log(text=""):
    print(text)


rows = defaultdict(list)
problems = []
for path in sorted(glob.glob(os.path.join(folder, "*.csv"))):
    ticker = os.path.basename(path)[:-4].upper()
    previous = None
    for number, line in enumerate(open(path, encoding="utf-8").read().splitlines(), 1):
        cells = line.split(",")
        if len(cells) != len(COLUMNS):
            problems.append(f"{ticker}:{number} has {len(cells)} columns")
            continue
        record = dict(zip(COLUMNS, cells))
        record["time"] = datetime.strptime(record["time"], "%Y%m%d")
        record["endtime"] = datetime.strptime(record["endtime"], "%Y%m%d %H:%M")
        for column in NUMERIC:
            value = record[column]
            try:
                record[column] = float(value) if value else None
            except ValueError:
                problems.append(f"{ticker}:{number} {column} is not numeric: {value}")
                record[column] = None
        if previous and record["endtime"] < previous:
            problems.append(f"{ticker}:{number} EndTime goes back in time")
        if record["endtime"] <= record["time"]:
            problems.append(f"{ticker}:{number} EndTime is not after Time")
        previous = record["endtime"]
        rows[ticker].append(record)

total = sum(len(v) for v in rows.values())
log("# US Treasury Auctions validation")
log()
log("## 1. Structure")
log()
log(f"- Files: {len(rows)}, rows: {total}")
log(f"- Empty files: {[t for t, v in rows.items() if not v]}")
log(f"- Problems (column count, non-numeric cells, EndTime order, EndTime after Time): {len(problems)}")
for problem in problems[:20]:
    log(f"  - {problem}")
duplicates = sum(n - 1 for n in Counter((t, r["time"], r["security_term"], r["issue_date"], r["maturity_date"], r["interest_rate"], r["high_price"]) for t, v in rows.items() for r in v).values() if n > 1)
log(f"- Duplicate auctions: {duplicates}")
clocks = Counter(r["endtime"].strftime("%H:%M") for v in rows.values() for r in v)
log(f"- EndTime clock times: {dict(clocks)}")
same_stamp = Counter((t, r["endtime"]) for t, v in rows.items() for r in v)
log(f"- Points carrying more than one auction (ticker, EndTime): {sum(1 for n in same_stamp.values() if n > 1)}, "
    f"by ticker {dict(Counter(t for (t, _), n in same_stamp.items() if n > 1))}")
log()

log("## 2. Coverage")
log()
log("| ticker | rows | first | last | rows per year |")
log("|---|---|---|---|---|")
for ticker, values in sorted(rows.items(), key=lambda item: -len(item[1])):
    first, last = values[0]["time"], values[-1]["time"]
    years = max((last - first).days / 365.25, 1)
    log(f"| {ticker} | {len(values)} | {first:%Y-%m-%d} | {last:%Y-%m-%d} | {len(values) / years:.1f} |")
log()
log("Per-column coverage over all rows (non-empty, first year with a value):")
log()
log("| column | non-empty | first year |")
log("|---|---|---|")
for column in NUMERIC:
    present = [r for v in rows.values() for r in v if r[column] is not None]
    first = min(r["time"].year for r in present) if present else "-"
    log(f"| {column} | {len(present)} of {total} | {first} |")
log()
zeros = [c for c in NUMERIC if all((r[c] or 0) == 0 for v in rows.values() for r in v)]
log(f"- Columns that are zero or empty everywhere: {zeros}")
log()

log("## 3. Statistics")
log()
log("| column | min | max | mean | std | beyond 3 std |")
log("|---|---|---|---|---|---|")
outliers = {}
for column in NUMERIC:
    values = [r[column] for v in rows.values() for r in v if r[column] is not None]
    if not values:
        continue
    mean = sum(values) / len(values)
    std = (sum((x - mean) ** 2 for x in values) / len(values)) ** 0.5
    far = [x for x in values if std and abs(x - mean) > 3 * std]
    outliers[column] = far
    log(f"| {column} | {min(values):g} | {max(values):g} | {mean:.4g} | {std:.4g} | {len(far)} |")
log()
negative = [(t, r["time"].date(), c, r[c]) for t, v in rows.items() for r in v for c in NUMERIC
            if r[c] is not None and r[c] < 0 and c not in ("high_discount_margin", "low_discount_margin", "median_discount_margin")]
log(f"- Negative values outside discount margins: {len(negative)} {negative[:10]}")
inconsistent = [(t, r["time"].date()) for t, v in rows.items() for r in v
                if r["total_accepted"] and r["total_tendered"] and r["total_accepted"] > r["total_tendered"] * 1.0001]
log(f"- Accepted above tendered: {len(inconsistent)} {inconsistent[:10]}")
log()

log("## 4. Context")
log()
log("Known events, 30-year bond and 10-year note:")
log("```")
for ticker, start, end in [("BOND30Y", "2008-10-01", "2009-03-01"), ("NOTE10Y", "2020-02-15", "2020-05-15"), ("NOTE10Y", "2022-05-01", "2022-11-01")]:
    for r in rows.get(ticker, []):
        if start <= r["time"].strftime("%Y-%m-%d") <= end:
            log(f"{ticker} {r['time']:%Y-%m-%d} {r['security_term']:>16} high yield {r['high_yield']} bid-to-cover {r['bid_to_cover_ratio']} dealers {r['primary_dealer_accepted']}")
log("```")
# Names the API uses since 2026-10-20 for the fields read here
RENAMED = {"auctionDate": "competitiveAuctionCloseDate", "totalAccepted": "grandTotalParAwarded", "highInvestmentRate": "investmentRate"}


def field(record, name):
    return record[name] if name in record else record[RENAMED[name]]


if raw:
    api = defaultdict(list)
    for path in glob.glob(os.path.join(raw, "*.json")):
        for record in json.load(open(path)):
            if field(record, "totalAccepted"):
                api[(field(record, "auctionDate")[:10].replace("-", ""), record["securityTerm"], record["maturityDate"][:10].replace("-", ""))].append(record)
    # Amounts and the clearing rates, each against its own column
    CHECKED = [("totalAccepted", "total_accepted"), ("offeringAmount", "offering_amount"), ("highYield", "high_yield"),
               ("highInvestmentRate", "high_investment_rate"), ("highDiscountMargin", "high_discount_margin")]

    def same(api_value, row_value):
        if not api_value:
            return row_value is None
        return row_value is not None and abs(float(api_value) - row_value) < 1e-6

    matched = mismatched = 0
    for values in rows.values():
        for r in values:
            candidates = api.get((r["time"].strftime("%Y%m%d"), r["security_term"], r["maturity_date"]), [])
            if any(all(same(field(a, name), r[column]) for name, column in CHECKED) for a in candidates):
                matched += 1
            else:
                mismatched += 1
    log(f"- Cross-check against the raw API download: {matched} rows match, {mismatched} do not")
log()

for column in NUMERIC:
    fig, ax = plt.subplots(figsize=(9, 3.2))
    for ticker in ACTIVE:
        points = [(r["time"], r[column]) for r in rows.get(ticker, []) if r[column] is not None]
        if points:
            ax.plot([p[0] for p in points], [p[1] for p in points], ".", markersize=2, label=ticker)
    ax.set_title(column)
    ax.legend(fontsize=6, ncol=5, markerscale=4)
    fig.tight_layout()
    fig.savefig(os.path.join(out, f"{column}.png"), dpi=70)
    plt.close(fig)
log(f"Plots: {len(NUMERIC)} files, one per numeric column, 10 active benchmarks each.")
