# Implementation Plan: Privacy and Authorized Scope

**Branch**: `feature/f02-privacy-and-scope` | **Date**: 2026-10-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/002-privacy-and-scope/spec.md` (issue #9 with #6, #7, #8)

## Summary

Make local-only processing and authorized scope enforceable in code. No pipeline exists yet, so the plan delivers enforcement primitives (endpoint policy, local-only HTTP, scoped resolver, screening, scrubbed diagnostics, protected storage and a data-store catalogue), wires them into existing code (`ProcessRunner`, smoke test, compatibility check, CLI errors) and fixes the contracts later features (F03-F07) must call. Implementation already exists; this plan re-derives and verifies it, and tracks gaps against the constitution.

## Technical Context

**Language/Version**: C# / .NET 10 (SDK pinned in `global.json`)

**Primary Dependencies**: none added (BCL only); Python sidecar security rules are specified, not built (F07)

**Storage**: files under `.index/` (owner-only permissions); no new database

**Testing**: xUnit v3 in `src/Polifemo.Tests` (`Contracts/`, `Platform/Privacy/`), recording `HttpMessageHandler` and `ScopedTestStore` test double

**Target Platform**: Linux devcontainer

**Project Type**: library + CLI (`polifemo`)

**Performance Goals**: not applicable; policy checks are per-call string and set operations

**Constraints**: zero warnings, `tools/sonar-local.sh` reports 0 findings, no sample content in diagnostics

**Scale/Scope**: primitives only; retrieval arms, ingestion and inference service do not exist yet

## Constitution Check

*GATE: passed with one justified deviation (see Complexity Tracking).*

| Principle | Result |
|---|---|
| I Local-Only (NON-NEGOTIABLE) | Endpoint, HTTP, process-environment and model-acquisition rules implemented. **Deviation**: the constitution requires offline operation enforced by network controls, not only settings; F02 enforces by settings and client-side refusal. Tracked below and in T017. |
| II Evidence | No technique adopted, no weights or heuristics introduced. |
| III Pinned runs | Models require a pinned revision plus review record; no download at runtime. |
| IV Identity and publication | Snapshot ids are content-bound; `BoundaryKey` prefixes cache keys. No publication code exists, so the sync/publication coverage list (additions, edits, deletions, renames, line shifts, concurrent sync, crash boundaries) is **not applicable to F02** and is owned by F03/F04; recorded in T018. |
| V Isolated contracts | Policy in Platform, scope contracts in Contracts; boundary test stays green; no store interface changed. |
| VI Scope-correct retrieval | Resolver before retrieval, filter-before-candidates, no cross-boundary dedup, leak re-check. Verified against a test double only; real arms owed by F05/F07. |
| VII Staged simplicity | No new service, dependency or folder beyond `Privacy/` and `Scope/`. |

Correctness gates: zero cross-scope leakage is tested on the double; no half-published snapshots and deterministic recovery are out of reach until publication exists.

## Project Structure

### Documentation (this feature)

```text
specs/002-privacy-and-scope/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/policies.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Polifemo.Contracts/Scope/            # ContentManifest, ScopeRequest, ScopeResolver, ResolvedScope, BoundaryKey
src/Polifemo.Platform/Privacy/           # LocalEndpointPolicy, LocalOnlyHttp, ProcessingPolicy, PrivacyEnvironment,
                                         # TelemetryPolicy, TokenSaveUploadGuard, ModelArtifactGate, SourceInputPolicy,
                                         # Scrubber, DiagnosticEvent, ServiceSecurity, ProtectedStorage, DataStoreCatalog
src/Polifemo.Tests/Contracts/            # ScopeResolutionTests, ScopedRetrievalTests
src/Polifemo.Tests/Platform/Privacy/     # policy, diagnostics, screening, storage tests
docs/privacy.md                          # policies, development versus production
```

**Structure Decision**: single solution, existing three projects; no new project.

## Complexity Tracking

| Deviation | Why accepted | Resolution |
|---|---|---|
| Offline guarantee is a setting plus client-side refusal, not a network control | No egress-controlled environment exists in the devcontainer; building one is out of F02 scope | T017: record an egress-disabled run (container network policy) before real corpora are processed; until then `docs/privacy.md` states the limit |
