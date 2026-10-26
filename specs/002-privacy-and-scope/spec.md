# Feature Specification: Privacy and Authorized Scope

**Feature Branch**: `feature/f02-privacy-and-scope`
**Created**: 2026-10-26
**Status**: Implemented (library level)
**Input**: GitHub issue #9 [F02] and children #6 [F02-01], #7 [F02-02], #8 [F02-03], implemented together as requested on the issue.

## Overview

Make local-only operation and repository/worktree isolation enforceable server-side. No ingestion, retrieval or
inference pipeline exists yet (F03 onward), so this feature delivers the enforcement primitives, wires them into
the code that already exists (`ProcessRunner`, the Elasticsearch smoke test, the TokenSave compatibility check,
CLI error output) and fixes the contract later features must call. Reference: `docs/privacy.md`.

**Out of scope**: authorizing hosted processing; production security (TLS, encryption at rest, durable
authorization state); the retrieval arms themselves; the embedding sidecar (F07).

## Requirements

- **FR-001** Endpoints for embedding, reranking, query transmission, store and telemetry MUST be loopback or an
  approved local service name; each capability is validated independently, including caller overrides, with
  diagnostics that do not echo the URL. (F02-01)
- **FR-002** Every `HttpClient` carrying sample-derived data MUST refuse a non-local destination before sending,
  not follow redirects and not use system proxies. (F02-01)
- **FR-003** Child processes MUST always run with offline and no-telemetry variables; TokenSave usage reporting
  MUST be off (`TS_UPLOAD_ENABLED` fails the compatibility check). (F02-01)
- **FR-004** A model profile is enabled only with a license/revision/remote-code review record and pinned
  artifacts already on disk; a missing artifact is an actionable error and nothing is downloaded. (F02-01)
- **FR-005** Credentials MUST NOT be part of a URL or tracked configuration. (F02-01, F02-03)
- **FR-006** One resolver MUST turn a request into a `ResolvedScope` for indexing and for queries; unknown and
  unauthorized scopes are indistinguishable; unpublished snapshots fail explicitly. (F02-02)
- **FR-007** Snapshot ids MUST derive from the source commit and a content manifest. Repository, branch,
  worktree and snapshot ids stay separate. (F02-02)
- **FR-008** Every retrieval arm, file lookup, expansion and cache takes a `ResolvedScope` and filters before
  candidates exist; returned hits are re-checked (`SCOPE_LEAK`). Cache and de-duplication keys start with the
  authorization boundary. (F02-02)
- **FR-009** Inputs are screened by explicit exclude/redact/reject rules before indexing or inference; every
  decision is reported and never alters content silently. (F02-03)
- **FR-010** Diagnostics carry codes and identifiers only; payload-bearing field names are refused and values are
  scrubbed; untyped exception messages are dropped. Telemetry exporters MUST be local. (F02-03)
- **FR-011** The inference service binds to loopback, requires a bearer token from the environment and bounds
  requests. Derived data lives owner-only under `.index/`. Every data store and diagnostic channel has a
  protection policy, and development-versus-production responsibilities are documented. (F02-03)

## Constitution Check

| Principle | Result |
|---|---|
| I Local-only | Implements it; no configuration flag enables a hosted provider. |
| II Evidence | No technique is adopted; no ranking or heuristic weight is introduced. |
| III Pinned runs | Models need a pinned revision and a review record. |
| IV Identity | Snapshot ids are content-bound; `BoundaryKey` prefixes the boundary. |
| V Isolation | No interface added (boundary hygiene test stays green); policy lives in Platform, scope rules in Contracts. |
| VI Scope-correct | Resolver, filter-before-retrieval contract and leak re-check are tested. |
| VII Simplicity | Library primitives only; no service, dependency or folder beyond what is used. |

## Success Criteria

- No payload-bearing request reaches a non-local destination in the indexing, search and reranking paths (tests with a recording handler).
- Cross-scope and dirty-same-HEAD retrieval tests return zero foreign occurrences and examine zero foreign candidates.
- A test fails when a data store or diagnostic channel lacks a policy or is missing from `docs/privacy.md`.

## Known limits

- The offline guarantee is a setting, not a network control; an egress-disabled run is not recorded.
- `ScopeRegistry` is in memory until F04 persists it. The retrieval arms are exercised through a test double (`ScopedTestStore`) until F05/F07 exist.
- Content screening is pattern based and will miss secrets that match no rule.
