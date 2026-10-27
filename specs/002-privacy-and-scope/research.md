# Research: Privacy and Authorized Scope

## R1 Endpoint validation
- **Decision**: per-capability allow-list of loopback addresses and approved local service names; reject credentials in URLs; never echo the URL in errors.
- **Rationale**: a name check alone is bypassable; per-capability validation prevents a local-only embedder setting from legitimising a remote reranker.
- **Alternatives**: global allow-list (rejected: one override legitimises all); DNS resolution check (rejected: adds network I/O and TOCTOU).

## R2 HTTP clients carrying payloads
- **Decision**: a single `LocalOnlyHttp` factory: destination check before send, redirects off, system proxy off.
- **Rationale**: redirects and proxies are the two ways a local URL reaches a remote host.
- **Alternatives**: documentation only (rejected: not enforceable).

## R3 Offline enforcement
- **Decision**: child processes always get offline and no-telemetry variables; callers cannot override. Network-level enforcement deferred (constitution deviation, T017).
- **Alternatives**: container network policy now (rejected for F02: environment work, not code).

## R4 Scope resolution and snapshot identity
- **Decision**: one resolver returns an unforgeable `ResolvedScope`; snapshot id = hash(commit + content manifest); unknown and unauthorized return the same error.
- **Rationale**: commit alone cannot distinguish dirty worktrees; identical errors prevent scope enumeration.
- **Alternatives**: commit-only ids (rejected: dirty content collides); distinct error codes (rejected: oracle).

## R5 Sensitive data
- **Decision**: explicit exclude/redact/reject rules, pattern based; decisions reported without matched text. Diagnostics are typed codes plus identifiers; payload-like field names refused.
- **Alternatives**: entropy scanning (rejected: unmeasured heuristic, constitution II); free-text logs (rejected).

## R6 Storage
- **Decision**: derived data owner-only under `.index/`; `DataStoreCatalog` lists every store with a policy; a test fails when a store lacks one or is missing from `docs/privacy.md`.

## R7 Sync/publication coverage
- **Decision**: not applicable to F02 (no publication code). Owned by F03/F04; recorded as T018.
