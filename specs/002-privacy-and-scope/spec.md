# Feature Specification: Privacy and Authorized Scope

**Feature Branch**: `feature/f02-privacy-and-scope` (spec directory `002-privacy-and-scope`)
**Created**: 2026-10-26
**Status**: Draft (re-specified through Spec Kit; library-level implementation exists)
**Input**: GitHub issue #9 [F02] and children #6 [F02-01], #7 [F02-02], #8 [F02-03], implemented together as requested on the issue.

## Overview

Make local-only operation and repository/worktree isolation enforceable server-side. No ingestion, retrieval or
inference pipeline exists yet (F03 onward), so this feature delivers the enforcement primitives, wires them into
the code that already exists (`ProcessRunner`, the Elasticsearch smoke test, the TokenSave compatibility check,
CLI error output) and fixes the contract later features must call. Reference: `docs/privacy.md`.

**Out of scope**: authorizing hosted processing; production security (TLS, encryption at rest, durable
authorization state); the retrieval arms themselves; the embedding sidecar (F07).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Nothing leaves the machine (Priority: P1)

A maintainer runs an experiment on a real corpus and needs assurance that no source, query text or derived data is sent to any non-local service, whatever configuration, caller override or redirect is involved.

**Why this priority**: mandatory before any real corpus is processed (milestone M0); a single leak is irreversible.

**Independent Test**: configure each capability (embedding, reranking, query, store, telemetry) with local, remote, credential-bearing and redirecting destinations and confirm only local ones are used and nothing is sent to the others.

**Acceptance Scenarios**:

1. **Given** a non-local or credential-bearing endpoint for any capability, **When** the platform starts a run, **Then** it is rejected with a diagnostic that does not contain the URL.
2. **Given** a local service that redirects to a remote host, **When** a payload-bearing call is made, **Then** the redirect is not followed and nothing is sent.
3. **Given** a child process is started, **When** the caller tries to override the offline or no-telemetry settings, **Then** the overrides are ignored.
4. **Given** a model without a review record or without pinned artifacts on disk, **When** it is enabled, **Then** it fails with an actionable error and nothing is downloaded.

---

### User Story 2 - Authorized scope and isolation (Priority: P1)

A caller indexes or queries a repository, branch, worktree or snapshot and must only ever see content from scopes it is authorized for, even when identical or dirty content exists in another scope.

**Why this priority**: isolation must be enforced by the server side, not assumed by callers.

**Independent Test**: populate two scopes with identical content and one scope with dirty content at the same commit, query across them, and count foreign results and foreign candidates examined.

**Acceptance Scenarios**:

1. **Given** an unknown scope and an unauthorized scope, **When** each is resolved, **Then** the errors are indistinguishable.
2. **Given** an unpublished snapshot, **When** it is requested, **Then** the failure is explicit.
3. **Given** dirty content at the same HEAD, **When** snapshots are computed, **Then** the ids differ.
4. **Given** a query in one scope, **When** it runs, **Then** zero foreign candidates are examined and zero foreign hits are returned; a foreign hit is reported as a scope leak.

---

### User Story 3 - Protected payloads, secrets and diagnostics (Priority: P2)

A maintainer needs sensitive inputs screened before indexing or inference, and logs, traces and errors that never contain source, query text or credentials.

**Why this priority**: closes the remaining data-bearing channels once transport and scope are enforced.

**Independent Test**: feed inputs containing secret-like patterns and payload-like diagnostic fields, then inspect decisions, diagnostics and CLI error output.

**Acceptance Scenarios**:

1. **Given** an input matching an exclude, redact or reject rule, **When** it is screened, **Then** the decision is reported without the matched text and content is never altered silently.
2. **Given** a diagnostic with a payload-like field name, **When** it is recorded, **Then** it is refused; values are scrubbed and untyped exception messages are dropped.
3. **Given** the inference service configuration, **When** it lacks loopback binding, a bearer token from the environment or a request size bound, **Then** it does not start.
4. **Given** every data store and diagnostic channel, **When** the policy catalogue is checked, **Then** each has a protection policy documented in `docs/privacy.md`.

---

### Edge Cases

- Local service names that resolve to non-loopback addresses; IPv6 loopback forms; credentials embedded in URLs.
- Same content in two scopes; same commit with different working-tree content.
- A cache or de-duplication key that omits the authorization boundary.
- A secret that matches no screening rule (known limit, see below).
- Telemetry or TokenSave reporting enabled by environment or setting, or absent setting (warn).

## Requirements *(mandatory)*

### Functional Requirements
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

### Key Entities

- **Resolved scope**: the authorized repository/branch/worktree/snapshot a request may touch; only the resolver can create it.
- **Snapshot id**: derived from source commit plus content manifest.
- **Boundary key**: authorization-boundary prefix of every cache and de-duplication key.
- **Processing policy**: per-capability local-only endpoint rules.
- **Diagnostic event**: codes and identifiers only.
- **Data store policy**: protection rule per data-bearing store.

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

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero payload-bearing requests reach a non-local destination in the indexing, search and reranking paths (recording-handler tests).
- **SC-002**: Cross-scope and dirty-same-HEAD retrieval tests return zero foreign results and examine zero foreign candidates.
- **SC-003**: A test fails when any data store or diagnostic channel lacks a policy or is missing from `docs/privacy.md`.
- **SC-004**: No diagnostic or CLI error output in tests contains a seeded secret, URL or payload text.

## Assumptions

- No ingestion, retrieval or inference pipeline exists yet (F03 onward); this feature delivers and tests enforcement primitives and the contract later features must call (follow-ups T016-T018).
- Retrieval arms are exercised through a test double until they exist.
- Development defaults are not production security.

## Known limits

- The offline guarantee is a setting, not a network control; an egress-disabled run is not recorded.
- `ScopeRegistry` is in memory until F04 persists it. The retrieval arms are exercised through a test double (`ScopedTestStore`) until F05/F07 exist.
- Content screening is pattern based and will miss secrets that match no rule.
