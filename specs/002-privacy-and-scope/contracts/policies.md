# Contracts: Privacy and Authorized Scope

Callers in later features MUST use these seams; none may be bypassed.

1. **Scope**: `ScopeResolver.Resolve(ScopeRequest) -> ResolvedScope | error`. Every retrieval arm, file lookup, expansion and cache takes a `ResolvedScope`, filters before candidates exist, and re-checks hits (`SCOPE_LEAK`). Keys start with `BoundaryKey`.
2. **Endpoints**: every embedding, reranking, query, store or telemetry destination passes `LocalEndpointPolicy` for its capability.
3. **HTTP**: every client carrying sample-derived data is created through `LocalOnlyHttp`.
4. **Processes**: child processes are started through `ProcessRunner`, which applies `PrivacyEnvironment`.
5. **Inputs**: content passes `SourceInputPolicy` before indexing or inference.
6. **Diagnostics**: emitted as `DiagnosticEvent`; CLI errors pass `Scrubber`.
7. **Models**: enabled through `ModelArtifactGate`; never downloads.
8. **Inference service** (F07): loopback bind, bearer token from environment, bounded request size.
9. **Stores**: new data-bearing stores or channels are added to `DataStoreCatalog` and `docs/privacy.md`, or the catalogue test fails.
