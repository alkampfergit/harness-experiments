# Quickstart: validating F02

Prerequisites: devcontainer with .NET 10 SDK; `tokensave` on PATH for the 20 TokenSave-dependent tests.

```sh
dotnet build Polifemo.slnx          # expect 0 warnings
dotnet test Polifemo.slnx           # Privacy and Scope tests: all pass
tools/sonar-local.sh                # expect 0 finding(s)
polifemo platform check             # fails if TokenSave reporting is enabled; warns if the setting is absent
```

Expected outcomes map to spec success criteria: SC-001 (`LocalOnlyPolicyTests`), SC-002 (`ScopedRetrievalTests`), SC-003 (`StorageAndServiceTests`), SC-004 (`DiagnosticsTests`). Contracts: [contracts/policies.md](contracts/policies.md); entities: [data-model.md](data-model.md).
