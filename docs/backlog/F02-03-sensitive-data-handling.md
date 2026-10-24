# [F02-03] Protect sensitive inputs, derived stores and diagnostics

- Type: Child issue
- Parent: [F02](./F02-privacy-and-scope.md)
- Depends on: [F02-01](./F02-01-local-only-policy.md)
- Suggested labels: `task`, `privacy`, `observability`
- Source: architecture 12; addendum 5, 8 and 9

## Scope

Define source/exclusion and secret-handling rules, inference authentication/request
limits, protected local storage and privacy-safe structured diagnostics. Treat
graphs, vectors, checkpoints, caches and logs as confidential source-derived data.

## Acceptance criteria

- [ ] Excluded/secret-bearing fixture inputs follow an explicit tested reject,
  redact or exclude policy before indexing/inference; no silent content alteration.
- [ ] Default logs and traces omit raw code, queries, credentials and private URLs.
- [ ] Error paths retain useful identifiers and failure causes without payload leakage.
- [ ] Local endpoint binding/authentication, file access and development-versus-
  production TLS/encryption responsibilities are documented and tested where applicable.
- [ ] Telemetry exporters cannot send sample-bearing data outside the local boundary.
