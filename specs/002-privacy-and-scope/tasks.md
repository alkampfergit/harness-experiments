# Tasks: Privacy and Authorized Scope

- [X] T001 F02-01 `LocalEndpointPolicy`, `ProcessingPolicy`, `LocalOnlyHttp`, `PrivacyEnvironment`, `TelemetryPolicy`, `TokenSaveUploadGuard` in `src/Polifemo.Platform/Privacy/`; wired into `ProcessRunner`, `SmokeTest`, `CompatibilityChecker`
- [X] T002 F02-01 `ModelArtifactGate` and model acquisition procedure in `docs/privacy.md`
- [X] T003 F02-02 `ContentManifest`, `ScopeRequest`, `ScopeRegistry`, `ScopeResolver`, `ResolvedScope`, `BoundaryKey` in `src/Polifemo.Contracts/Scope/`
- [X] T004 F02-02 scoped retrieval tests with `ScopedTestStore` (same content in two scopes, dirty content at one HEAD, filter before candidates)
- [X] T005 F02-03 `SecretRules`, `Scrubber`, `SourceInputPolicy`
- [X] T006 F02-03 `DiagnosticEvent`; CLI error output scrubbed
- [X] T007 F02-03 `InferenceServiceSecurity`, `RequestLimits`, `ProtectedStorage`, `DataStoreCatalog`
- [X] T008 `docs/privacy.md` (policies, development versus production)
- [ ] T009 Follow-up (F04/F07): persist `ScopeRegistry`; use `LocalOnlyHttp`, `SourceInputPolicy` and `ModelArtifactGate` in the exporter and the embedding client; record an egress-disabled run
