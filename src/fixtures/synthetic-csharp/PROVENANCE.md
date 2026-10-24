# Provenance: synthetic-csharp

A small order-processing library written for this repository. It is not derived from any
third-party or private code.

- **Source:** authored here, in `src/`, `docs/` and `tokensave-docs/` of this folder.
- **License:** the same as the repository (no external material).
- **Kind:** synthetic.

## How to rebuild

```bash
dotnet run --project src/Polifemo.Cli -- fixture build \
  --fixture src/fixtures/synthetic-csharp --out .index/fixtures/synthetic-csharp
```

The command copies the three content folders into a fresh git repository and commits them with a
fixed identity and date (`2026-01-01T00:00:00Z`), with the user and system git configuration
disabled. The same input always produces the same commit. A test asserts that the result equals
the commit recorded below, so any change to the fixture files must update this line in the same
commit.

Commit: 9954cbfa498f85b66c1340d89077f676905045ac

## Contents that are deliberate

| Case | Where |
|---|---|
| overloads | `Money.Add(Money)` / `Money.Add(decimal)`, `OrderFormatter.Format` (1 and 2 parameters) |
| generics, nested generic types with the same simple name | `IRepository<T>`, `Cache<TKey>.Entry<TValue>` and `Index<TKey>.Entry<TValue>` |
| partial class across files | `Order.cs` and `Order.Totals.cs` |
| extension methods | `StringExtensions.ToSlug`, `ServiceCollectionExtensions.AddOrders` |
| interfaces and implementations | `IOrderService`, `INotifier`, `IRepository<T>` |
| dependency-injection registration and a DI-resolved call | `AddOrders`, `Program.RunAsync` |
| async methods with cancellation | `OrderService`, repositories, notifier |
| encodings and line endings | `BomMarker` (UTF-8 BOM), `CrlfMarker` (CRLF), `Latin1Marker` (ISO-8859-1), `Utf16Marker` (UTF-16 LE) |
| sidecar Markdown | `src/Domain/Order.readme.md` |
| central Markdown with explicit associations | `tokensave-docs/orders-overview.md` (`applies_to`) |
| stale document | `tokensave-docs/legacy-pricing.md` (its target does not exist) |
| plain Markdown | `docs/architecture.md` |

## Mutations

`mutations/<name>/mutation.json` plus payload files. All start from `"base": "initial"`, the root
commit of the built repository. The eight mutations are `add-file`, `edit-method`, `delete-file`,
`rename-file`, `line-shift`, `docs-only-edit`, `change-doc-association` and `rename-and-edit`.

## Expectations

`expectations/<state>.json` records, for the base state and after each mutation, the symbols with
extents and the relationships the pinned TokenSave 7.16.0 extracts, including the cases it cannot
resolve. The base record was reviewed by hand against the source. The records for the mutations
are generated and reviewed as differences against the base record. See
`specs/001-platform-foundations/contracts/expectation-record.schema.json`.
