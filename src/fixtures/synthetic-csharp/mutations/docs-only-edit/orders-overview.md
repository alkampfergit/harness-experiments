---
applies_to:
  - "src/Services/OrderService.cs"
  - "src/Services/IOrderService.cs"
  - "src/Domain/Money.cs"
---

# Orders overview

`OrderService.PlaceOrderAsync` marks an order as placed, rejects a total that is not positive,
saves the order, publishes `OrderPlaced` through `INotifier` and returns. Amounts are `Money` values and can be added
with `Money.Add`.
