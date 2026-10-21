# US Treasury Auctions validation

## 1. Structure

- Files: 24, rows: 11153
- Empty files: []
- Problems (column count, non-numeric cells, EndTime order, EndTime after Time): 0
- Duplicate auctions: 0
- EndTime clock times: {'16:10': 4881, '14:10': 6272}
- Points carrying more than one auction (ticker, EndTime): 127, by ticker {'CMB': 121, 'NOTE10Y': 3, 'NOTE4Y': 1, 'NOTE5Y': 2}

## 2. Coverage

| ticker | rows | first | last | rows per year |
|---|---|---|---|---|
| BILL13W | 2443 | 1979-12-28 | 2026-10-19 | 52.2 |
| BILL26W | 2443 | 1979-12-28 | 2026-10-19 | 52.2 |
| BILL4W | 1316 | 2001-07-31 | 2026-10-15 | 52.2 |
| CMB | 934 | 1980-03-04 | 2026-05-21 | 20.2 |
| NOTE2Y | 545 | 1980-01-23 | 2026-09-22 | 11.7 |
| BILL52W | 508 | 1980-01-02 | 2026-09-29 | 10.9 |
| NOTE5Y | 440 | 1980-02-26 | 2026-09-23 | 9.4 |
| BILL8W | 419 | 2018-10-16 | 2026-10-15 | 52.4 |
| NOTE10Y | 362 | 1979-10-31 | 2026-10-07 | 7.7 |
| NOTE3Y | 301 | 1980-05-06 | 2026-10-06 | 6.5 |
| BOND30Y | 294 | 1979-11-01 | 2026-10-08 | 6.3 |
| NOTE7Y | 274 | 1980-02-06 | 2026-09-24 | 5.9 |
| BILL17W | 210 | 2022-10-19 | 2026-10-21 | 52.4 |
| FRN2Y | 155 | 2014-01-29 | 2026-09-23 | 12.3 |
| TIPS10Y | 140 | 1997-01-29 | 2026-09-17 | 4.7 |
| BOND20Y | 98 | 1981-01-06 | 2026-10-21 | 2.1 |
| BILL6W | 88 | 2025-02-18 | 2026-10-20 | 52.8 |
| TIPS5Y | 70 | 1997-07-09 | 2026-06-18 | 2.4 |
| TIPS30Y | 48 | 1998-04-08 | 2026-08-20 | 1.7 |
| NOTE4Y | 46 | 1980-03-25 | 1990-12-27 | 4.3 |
| TIPS20Y | 11 | 2004-07-27 | 2009-07-27 | 2.2 |
| BOND15Y | 4 | 1980-01-03 | 1980-10-07 | 4.0 |
| NOTE3Y6M | 3 | 1980-02-05 | 1981-02-03 | 3.0 |
| NOTE5Y5M | 1 | 1981-05-28 | 1981-05-28 | 1.0 |

Per-column coverage over all rows (non-empty, first year with a value):

| column | non-empty | first year |
|---|---|---|
| interest_rate | 2637 of 11153 | 1979 |
| spread | 155 of 11153 | 2014 |
| offering_amount | 11153 of 11153 | 1979 |
| total_tendered | 11153 of 11153 | 1979 |
| total_accepted | 11153 of 11153 | 1979 |
| competitive_tendered | 6274 of 11153 | 2008 |
| competitive_accepted | 6274 of 11153 | 2008 |
| noncompetitive_accepted | 6274 of 11153 | 2008 |
| bid_to_cover_ratio | 7939 of 11153 | 1994 |
| high_yield | 2637 of 11153 | 1979 |
| low_yield | 2637 of 11153 | 1979 |
| median_yield | 2637 of 11153 | 1979 |
| high_discount_rate | 8361 of 11153 | 1979 |
| low_discount_rate | 8361 of 11153 | 1979 |
| median_discount_rate | 8361 of 11153 | 1979 |
| high_investment_rate | 8361 of 11153 | 1979 |
| high_discount_margin | 155 of 11153 | 2014 |
| low_discount_margin | 155 of 11153 | 2014 |
| median_discount_margin | 155 of 11153 | 2014 |
| high_price | 11153 of 11153 | 1979 |
| allocation_percentage | 11153 of 11153 | 1979 |
| direct_bidder_tendered | 6274 of 11153 | 2008 |
| direct_bidder_accepted | 6274 of 11153 | 2008 |
| indirect_bidder_tendered | 6274 of 11153 | 2008 |
| indirect_bidder_accepted | 6274 of 11153 | 2008 |
| primary_dealer_tendered | 6274 of 11153 | 2008 |
| primary_dealer_accepted | 6274 of 11153 | 2008 |
| soma_accepted | 6274 of 11153 | 2008 |
| fima_noncompetitive_accepted | 6274 of 11153 | 2008 |

- Columns that are zero or empty everywhere: []

## 3. Statistics

| column | min | max | mean | std | beyond 3 std |
|---|---|---|---|---|---|
| interest_rate | 0.125 | 16.25 | 4.106 | 3.319 | 34 |
| spread | -0.075 | 0.3 | 0.1094 | 0.07821 | 0 |
| offering_amount | 2.5e+07 | 1.1e+11 | 2.773e+10 | 2.162e+10 | 117 |
| total_tendered | 7.295e+07 | 3.21826e+11 | 8.644e+10 | 6.492e+10 | 48 |
| total_accepted | 2.5e+07 | 1.19349e+11 | 2.953e+10 | 2.226e+10 | 87 |
| competitive_tendered | 7.295e+07 | 3.1299e+11 | 1.255e+11 | 5.65e+10 | 10 |
| competitive_accepted | 2.5e+07 | 1.03502e+11 | 3.977e+10 | 1.983e+10 | 17 |
| noncompetitive_accepted | 0 | 6.96676e+09 | 6.937e+08 | 1.076e+09 | 174 |
| bid_to_cover_ratio | 1.11 | 10.72 | 3.064 | 0.7956 | 47 |
| high_yield | -1.685 | 16.28 | 4.151 | 3.347 | 34 |
| low_yield | -2 | 16.2 | 3.978 | 3.405 | 32 |
| median_yield | -1.75 | 16.26 | 4.11 | 3.357 | 32 |
| high_discount_rate | 0 | 18.24 | 3.503 | 3.211 | 148 |
| low_discount_rate | 0 | 18.624 | 3.439 | 3.242 | 156 |
| median_discount_rate | 0 | 18.48 | 3.488 | 3.227 | 152 |
| high_investment_rate | 0 | 18.63 | 3.643 | 3.399 | 166 |
| high_discount_margin | -0.075 | 0.33 | 0.1151 | 0.07834 | 0 |
| low_discount_margin | -0.16 | 0.25 | 0.06685 | 0.07098 | 2 |
| median_discount_margin | -0.1 | 0.29 | 0.1004 | 0.07379 | 0 |
| high_price | 81.7542 | 132.953 | 98.98 | 1.972 | 263 |
| allocation_percentage | 0.01 | 100 | 49.68 | 28.67 | 0 |
| direct_bidder_tendered | 0 | 3.2884e+10 | 7.624e+09 | 4.926e+09 | 56 |
| direct_bidder_accepted | 0 | 2.3809e+10 | 3.102e+09 | 2.695e+09 | 113 |
| indirect_bidder_tendered | 0 | 1.07862e+11 | 2.635e+10 | 1.968e+10 | 48 |
| indirect_bidder_accepted | 0 | 7.30323e+10 | 1.969e+10 | 1.42e+10 | 33 |
| primary_dealer_tendered | 7.295e+07 | 2.1025e+11 | 9.148e+10 | 3.98e+10 | 0 |
| primary_dealer_accepted | 2.5e+07 | 5.39219e+10 | 1.698e+10 | 8.251e+09 | 14 |
| soma_accepted | 0 | 3.23935e+10 | 1.709e+09 | 2.859e+09 | 78 |
| fima_noncompetitive_accepted | 0 | 2e+09 | 2.184e+08 | 4.03e+08 | 172 |

- Negative values outside discount margins: 166 [('FRN2Y', datetime.date(2022, 1, 26), 'spread', -0.015), ('FRN2Y', datetime.date(2022, 2, 23), 'spread', -0.015), ('FRN2Y', datetime.date(2022, 3, 23), 'spread', -0.015), ('FRN2Y', datetime.date(2022, 4, 27), 'spread', -0.075), ('FRN2Y', datetime.date(2022, 5, 25), 'spread', -0.075), ('FRN2Y', datetime.date(2022, 6, 22), 'spread', -0.075), ('FRN2Y', datetime.date(2022, 7, 14), 'spread', -0.075), ('TIPS10Y', datetime.date(2011, 9, 22), 'low_yield', -0.1), ('TIPS10Y', datetime.date(2011, 11, 17), 'low_yield', -0.107), ('TIPS10Y', datetime.date(2012, 1, 19), 'high_yield', -0.046)]
- Accepted above tendered: 0 []

## 4. Context

Known events, 30-year bond and 10-year note:
```
BOND30Y 2008-11-13  29-Year 6-Month high yield 4.31 bid-to-cover 2.07 dealers 7709200000.0
BOND30Y 2009-02-12          30-Year high yield 3.54 bid-to-cover 2.02 dealers 6932003500.0
NOTE10Y 2020-03-11  9-Year 11-Month high yield 0.849 bid-to-cover 2.36 dealers 7145470000.0
NOTE10Y 2020-04-07  9-Year 10-Month high yield 0.782 bid-to-cover 2.43 dealers 6897224000.0
NOTE10Y 2020-05-12          10-Year high yield 0.7 bid-to-cover 2.69 dealers 6563637500.0
NOTE10Y 2022-05-11          10-Year high yield 2.943 bid-to-cover 2.49 dealers 4132000000.0
NOTE10Y 2022-06-08  9-Year 11-Month high yield 3.03 bid-to-cover 2.41 dealers 5619095000.0
NOTE10Y 2022-07-12  9-Year 10-Month high yield 2.96 bid-to-cover 2.34 dealers 6827870000.0
NOTE10Y 2022-08-10          10-Year high yield 2.755 bid-to-cover 2.53 dealers 3469593000.0
NOTE10Y 2022-09-12  9-Year 11-Month high yield 3.33 bid-to-cover 2.37 dealers 6336744000.0
NOTE10Y 2022-10-12  9-Year 10-Month high yield 3.93 bid-to-cover 2.34 dealers 6300632500.0
```
- Cross-check against the raw API download: 11153 rows match, 0 do not

Plots: 29 files, one per numeric column, 10 active benchmarks each.
