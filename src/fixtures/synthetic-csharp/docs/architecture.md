# Architecture

The fixture is a small order-processing library:

- `Domain` holds `Order`, `OrderLine`, `Money` and the generic repository.
- `Services` holds `OrderService` and the notifier.
- `Extensions` holds the string and dependency-injection helpers.
