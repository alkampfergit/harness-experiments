# Tasks: Privacy and Authorized Scope

**Input**: [spec.md](spec.md), [plan.md](plan.md), [data-model.md](data-model.md), [contracts/policies.md](contracts/policies.md)
**Tests**: required (spec success criteria; constitution zero-leakage gate). Tasks marked `[X]` already exist on this branch and are verified by `/speckit-analyze` and `/speckit-converge`.

## Phase 1: Setup

- [X] T001 Create `src/Polifemo.Platform/Privacy/` and `src/Polifemo.Contracts/Scope/` folders and test folders `src/Polifemo.Tests/Platform/Privacy/`, `src/Polifemo.Tests/Contracts/`

## Phase 2: Foundational

- [X] T002 [P] `DataStoreCatalog` and `ProtectedStorage` (owner-only under `.index/`) in `src/Polifemo.Platform/Privacy/`
- [X] T003 [P] `Scrubber` and `DiagnosticEvent` (codes and identifiers only; payload-like field names refused) in `src/Polifemo.Platform/Privacy/`

## Phase 3: User Story 1 - Nothing leaves the machine (P1)

**Independent test**: `LocalOnlyPolicyTests`, `ModelAndEnvironmentTests` pass.

- [X] T004 [P] [US1] `LocalEndpointPolicy`, `ProcessingPolicy`: per-capability loopback or approved local service name; reject overrides and credentialed URLs; no URL in errors
- [X] T005 [P] [US1] `LocalOnlyHttp` (check before send, no redirects, no proxy); wire into the Elasticsearch smoke test
- [X] T006 [US1] `PrivacyEnvironment` offline/no-telemetry variables in `ProcessRunner`; `TelemetryPolicy`, `TokenSaveUploadGuard` in `CompatibilityChecker`
- [X] T007 [US1] `ModelArtifactGate` and model acquisition procedure in `docs/privacy.md`
- [X] T008 [US1] Tests in `src/Polifemo.Tests/Platform/Privacy/LocalOnlyPolicyTests.cs` and `ModelAndEnvironmentTests.cs` (recording handler)

## Phase 4: User Story 2 - Authorized scope and isolation (P1)

**Independent test**: `ScopeResolutionTests`, `ScopedRetrievalTests` pass.

- [X] T009 [P] [US2] `ContentManifest`, `ScopeRequest`, `ScopeRegistry`, `ScopeResolver`, `ResolvedScope`, `BoundaryKey` in `src/Polifemo.Contracts/Scope/`
- [X] T010 [US2] Scoped retrieval tests with `ScopedTestStore` in `src/Polifemo.Tests/Contracts/ScopedRetrievalTests.cs` (same content in two scopes, dirty content at one HEAD, filter before candidates, `SCOPE_LEAK`)

## Phase 5: User Story 3 - Protected payloads, secrets and diagnostics (P2)

**Independent test**: `SourceInputPolicyTests`, `DiagnosticsTests`, `StorageAndServiceTests` pass.

- [X] T011 [P] [US3] `SecretRules`, `SourceInputPolicy` with decisions that never show matched text
- [X] T012 [US3] CLI error output scrubbed
- [X] T013 [P] [US3] `ServiceSecurity` (loopback bind, bearer token from environment, `RequestLimits`)
- [X] T014 [US3] Catalogue-versus-`docs/privacy.md` test in `StorageAndServiceTests.cs`

## Phase 6: Polish and follow-ups

- [X] T015 `docs/privacy.md` (policies, development versus production)
- [ ] T016 Follow-up (F04/F07): persist `ScopeRegistry`; use `LocalOnlyHttp`, `SourceInputPolicy` and `ModelArtifactGate` in the exporter and embedding client
- [ ] T017 Follow-up before real corpora: record an egress-disabled run (network control, not setting); closes the constitution Principle I deviation in plan.md
- [ ] T018 Follow-up (F03/F04): sync and publication coverage required by the constitution (additions, edits, deletions, renames, line shifts, docs-only changes, changed excludes, concurrent sync, crash boundaries); not applicable until publication exists

## Dependencies

Phase 2 precedes US3 only (diagnostics/storage). US1 and US2 are independent and parallel. T016-T018 depend on F03/F04/F07.

## Phase 7: Convergence

- [X] T019 CRITICAL State in `docs/privacy.md` that offline operation is not yet enforced by a network control and name the egress-disabled run (T017) as the closing evidence, per Constitution I (contradicts)
- [X] T020 Add a test that fails when a payload-carrying `HttpClient` is created outside `LocalOnlyHttp` in `src/Polifemo.Platform/`, per FR-002 (partial)
