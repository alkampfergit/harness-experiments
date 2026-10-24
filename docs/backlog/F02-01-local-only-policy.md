# [F02-01] Enforce local-only processing and offline model execution

- Type: Child issue
- Parent: [F02](./F02-privacy-and-scope.md)
- Depends on: [F01-03](./F01-03-application-contracts.md)
- Suggested labels: `task`, `privacy`
- Source: architecture 11-12; addendum 9; repository local-only policy

## Scope

Add validated server-side configuration for approved local inference/service
endpoints and offline artifacts. Deny hosted embeddings, hosted reranking and
external query transmission independently. Disable optional TokenSave uploads and
document network restrictions needed beyond that setting.

## Acceptance criteria

- [ ] Default and supported experiment profiles reject external inference endpoints,
  including attempted caller overrides, with explicit diagnostics.
- [ ] Tests verify no payload-bearing egress for indexing, search and reranking paths.
- [ ] Missing pinned model artifacts produce an actionable error, not a runtime download.
- [ ] Model acquisition is an explicit separate step; licenses, revisions and any
  remote model code are reviewed before enabling a profile.
- [ ] Offline runs remain functional without internet; credentials never enter tracked config.
