# Data Model: Privacy and Authorized Scope

| Entity | Key fields | Rules |
|---|---|---|
| ScopeRequest | repository, branch, worktree, snapshot (optional) | input only; never used for retrieval directly |
| ResolvedScope | repository id, branch, worktree, snapshot id, boundary | constructible only by `ScopeResolver` |
| ContentManifest | ordered (path, content hash) pairs | hashed with commit to form the snapshot id |
| BoundaryKey | authorization boundary prefix | first segment of every cache and de-dup key |
| EndpointPolicy | capability, allowed hosts | validated per capability; URL never echoed |
| ScreeningDecision | rule id, action (exclude/redact/reject), location | never carries matched text |
| DiagnosticEvent | code, identifiers | payload-like field names refused; values scrubbed |
| DataStorePolicy | store, owner, protection, documented | one per data-bearing store and channel |

Identity: repository, branch, worktree and snapshot ids stay separate. Snapshot lifecycle: unknown -> published (by F04); F02 only resolves published snapshots and fails explicitly otherwise.
