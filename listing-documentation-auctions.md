## Requesting Data

To add US Treasury Auctions data to your algorithm, call the **AddData** method. Save a reference to the dataset **Symbol** so you can access the data later in your algorithm.

```python
class USTreasuryAuctionsDataAlgorithm(QCAlgorithm):

    def initialize(self) -> None:
        self.set_start_date(2020, 1, 1)
        self.set_end_date(2021, 1, 1)
        self.set_cash(100000)
        self._dataset_symbol = self.add_data(USTreasuryAuctions, USTreasuryAuctionBenchmarks.Notes.TEN_YEAR).symbol
```
```csharp
public class USTreasuryAuctionsDataAlgorithm : QCAlgorithm
{
    private Symbol _datasetSymbol;

    public override void Initialize()
    {
        SetStartDate(2020, 1, 1);
        SetEndDate(2021, 1, 1);
        SetCash(100000);
        _datasetSymbol = AddData<USTreasuryAuctions>(USTreasuryAuctionBenchmarks.Notes.TenYear).Symbol;
    }
}
```

## Accessing Data

To get the current US Treasury Auctions data, index the current [Slice](https://www.quantconnect.com/docs/v2/writing-algorithms/key-concepts/time-modeling/timeslices) with the dataset **Symbol**. **Slice** objects deliver unique events to your algorithm as they happen, but the **Slice** may not contain data for your dataset at every time step. To avoid issues, check if the **Slice** contains the data you want before you index it.

```python
def on_data(self, slice: Slice) -> None:
    auctions = slice.get(USTreasuryAuctions).get(self._dataset_symbol)
    if auctions:
        for auction in auctions.data:
            high_yield = auction.high_yield
```
```csharp
public override void OnData(Slice slice)
{
    if (slice.Get<USTreasuryAuctions>().TryGetValue(_datasetSymbol, out var auctions))
    {
        foreach (USTreasuryAuction auction in auctions.Data)
        {
            var highYield = auction.HighYield;
        }
    }
}
```

To iterate through all of the dataset objects in the current **Slice**, call the **Get** method.

```python
def on_data(self, slice: Slice) -> None:
    for dataset_symbol, auctions in slice.get(USTreasuryAuctions).items():
        for auction in auctions.data:
            high_yield = auction.high_yield
```
```csharp
public override void OnData(Slice slice)
{
    foreach (var kvp in slice.Get<USTreasuryAuctions>())
    {
        var datasetSymbol = kvp.Key;
        foreach (USTreasuryAuction auction in kvp.Value.Data)
        {
            var highYield = auction.HighYield;
        }
    }
}
```

## Historical Data

To get historical US Treasury Auctions data, call the **History** method with the dataset **Symbol**. If there is no data in the period you request, the history result is empty.

```python
# DataFrame
history_df = self.history(USTreasuryAuctions, self._dataset_symbol, timedelta(days=365), Resolution.DAILY, flatten=True)

# Dataset objects
history_bars = self.history[USTreasuryAuctions](self._dataset_symbol, timedelta(days=365), Resolution.DAILY)
```
```csharp
var history = History<USTreasuryAuctions>(_datasetSymbol, TimeSpan.FromDays(365), Resolution.Daily);
```

For more information about historical data, see [History Requests](https://www.quantconnect.com/docs/v2/writing-algorithms/historical-data/history-requests).

## Remove Subscriptions

To remove your subscription to US Treasury Auctions data, call the **RemoveSecurity** method.

```python
self.remove_security(self._dataset_symbol)
```
```csharp
RemoveSecurity(_datasetSymbol);
```
