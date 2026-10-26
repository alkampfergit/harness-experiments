# Order

An `Order` collects `OrderLine` entries and moves from `Draft` to `Placed`.
The class is partial: `Order.cs` holds the state and `Order.Totals.cs` holds `Total()`.
