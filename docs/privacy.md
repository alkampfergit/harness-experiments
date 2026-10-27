# Privacy, local-only operation and authorized scope

Implements F02 (F02-01 local-only policy, F02-02 authorized scope, F02-03 sensitive data). The rules come
from AGENTS.md and constitution principles I and VI. Everything here is enforced in code under
`src/Polifemo.Platform/Privacy/` and `src/Polifemo.Contracts/Scope/`, and tested in `src/Polifemo.Tests/`.

This is not production security. Development defaults (HTTP without TLS on loopback, a shared local
password) are acceptable only because every endpoint is loopback or the local Compose network. Hosted
processing is out of scope and needs a separate, authorized change to AGENTS.md; no configuration flag enables it.

## 1. Local-only processing (F02-01)

| Rule | Enforced by |
|---|---|
| Embedding, reranking, query transmission, store and telemetry destinations must be loopback or an approved single-label local service name (`localhost`, `elasticsearch`). Names are never resolved, so checks work offline. | `LocalEndpointPolicy` |
| Each capability is validated independently, for profile defaults and for caller overrides. An override pointing outside the boundary is rejected (`POLICY_EXTERNAL_ENDPOINT`), not ignored. The message names the capability and a fingerprint, never the URL. | `ProcessingPolicy.Resolve` |
| Every `HttpClient` that carries sample-derived data is built by `LocalOnlyHttp.CreateClient`: the destination is checked per request before anything is sent, redirects are not followed, system proxies are not used. The public factory takes no handler (handler injection is internal, test-only), so the hardened transport is the only one exposed. The Elasticsearch smoke test uses it. | `LocalOnlyHandler` |
| Child processes always run with `HF_HUB_OFFLINE=1`, `TRANSFORMERS_OFFLINE=1`, `HF_HUB_DISABLE_TELEMETRY=1`, `DO_NOT_TRACK=1`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `OTEL_SDK_DISABLED=true`. A caller cannot override them. | `PrivacyEnvironment`, `ProcessRunner` |
| TokenSave usage reporting must be off: `report_savings: false` in `.tokensave/config.json`. `platform check` reports `TS_UPLOAD_ENABLED` (failure) or `TS_UPLOAD_SETTING_ABSENT` (warning). | `TokenSaveUploadGuard`, `CompatibilityChecker` |
| OpenTelemetry exporter endpoints (`OTEL_EXPORTER_OTLP_*ENDPOINT`) must pass the local policy. | `TelemetryPolicy` |
| URLs never carry credentials (`POLICY_URL_HAS_CREDENTIALS`); credentials come from the environment (`ELASTICSEARCH_PASSWORD`, `POLIFEMO_INFERENCE_TOKEN`) and never from tracked files. | `LocalEndpointPolicy`, `InferenceServiceSecurity` |

The settings above reduce accidental egress; they are **not a network control**. A run that must prove it is
offline needs an egress-disabled network (for example a container with `--network none` plus the Compose
network for Elasticsearch only, or host firewall rules that drop non-loopback traffic). That run was not
recorded (see platform-compatibility.md, section 5). Constitution Principle I requires network-level enforcement, so
this is a known deviation until the egress-disabled run (task T017 in `specs/002-privacy-and-scope/tasks.md`) is recorded and
before any real corpus is processed.

### Model acquisition

Acquisition is a separate, explicit step; runs never download models.

1. Review the model: license, immutable commit revision, and whether it needs `trust_remote_code`
   (remote code is arbitrary code from the model repository and needs its own review).
2. Record a `ModelApproval` (model, revision, license, remote-code flag and review, review date).
3. Download once, with network access, into `.index/models/` in the Hugging Face cache layout
   (`models--org--name/snapshots/<revision>`). No sample content is sent.
4. Runs call `ModelArtifactGate.EnsureReady`, which fails with `MODEL_NOT_APPROVED`,
   `MODEL_REMOTE_CODE_UNREVIEWED`, `MODEL_PROVIDER_NOT_LOCAL` or `MODEL_ARTIFACT_MISSING` and never fetches anything. An artifact is present only when a provider-specific manifest (`ModelArtifactGate.DefaultManifest`, overridable per approval) is satisfied: each required file must resolve, through cache links, to a non-empty file under the model root; dangling links, empty files and a bare `config.json` fail.

## 2. Authorized scope and snapshots (F02-02)

- `ScopeRequest` keeps authorization boundary, repository, branch, worktree and snapshot as separate ids.
- `ScopeResolver` is the one boundary for indexing (`ResolveForIndexing`) and queries (`ResolveForQuery`).
  It returns a `ResolvedScope`, which has no public constructor: every retrieval arm, file lookup, documentation
  or graph expansion and cache access takes one, so none can run without a validated context.
- Unknown scopes and scopes of another boundary fail with the same code and message (`SCOPE_NOT_AUTHORIZED`),
  so a caller learns nothing about what exists elsewhere. A snapshot that is not published fails with
  `SNAPSHOT_UNPUBLISHED`. Incomplete requests fail with `SCOPE_INVALID`.
- A snapshot id is derived from the source commit and a `ContentManifest` (path and file hash of every file),
  so dirty content at the same HEAD is a different snapshot. A caller-supplied id that disagrees is refused
  (`SNAPSHOT_MISMATCH`).
- `ResolvedScope.FilterTerms` are exact-match terms applied while candidates are generated, never only after
  ranking or fusion. `RequireContains` is a second check on results coming back from a store (`SCOPE_LEAK`).
- `BoundaryKey` (cache and de-duplication keys) always starts with the authorization boundary. Payloads, vectors
  and memberships are never shared across boundaries.
- `ScopeRegistry` is in memory. Persisting authorization and publication state belongs to F04; it must keep these rules.

## 3. Sensitive inputs, stores and diagnostics (F02-03)

### Input rules (`SourceInputPolicy`)

Applied before indexing or inference. Every rule has one explicit action and the most severe wins
(Reject, Exclude, Redact, Include). The decision lists the rule ids and never the matched text.

| Input | Default action |
|---|---|
| `.env*`, key material (`*.pem`, `*.key`, `*.pfx`, `id_rsa`, ...), `credentials`, `secrets.json`, `.git/`, `.tokensave/` | Exclude the file |
| Binary content | Exclude the file |
| Private key block, AWS access key id, GitHub token inside a file | Exclude the file |
| Quoted `password`/`secret`/`token`/`api_key` assignment, `Password=` in a connection string, `Bearer` token | Redact, replaced by `[REDACTED:<rule>]` |
| Anything else | Include, unchanged |

Redaction is never silent: the decision has `Action = Redact`, the rule ids, the redacted content and the hash of
the content actually indexed. A policy can set any rule to `Reject`, which fails the run (`INPUT_REJECTED`).

### Diagnostics (`DiagnosticEvent`, `Scrubber`)

Logs, traces and CLI errors carry a stable code, the component, exception types and identifier fields (ids,
paths, counts, stage names). Field names that suggest payload (`query`, `text`, `source`, `content`, `body`,
`url`, `password`, `token`, ...) are refused. Values are scrubbed of URLs and secrets and bounded. The message of
an untyped exception is dropped because it can echo input; typed failures keep their (payload-free) message.

### Local service security (`InferenceServiceSecurity`)

The embedding and reranking sidecar binds to loopback only, requires `Authorization: Bearer <token>` with the
token from `POLIFEMO_INFERENCE_TOKEN` (at least 16 characters, compared in constant time), and bounds request
body size and batch size (`RequestLimits`).

### Storage (`ProtectedStorage`, `DataStoreCatalog`)

Graphs, vectors, checkpoints, caches and logs are source-derived and confidential. They live only under the
git-ignored `.index/`, owner-only (`0700` directories, `0600` files; on Windows the profile ACLs apply). Symlinks and reparse points on the path, `.index` included, are refused (`STORAGE_SYMLINK`); an existing file is set to `0600` on the opened handle before it is truncated and written. The
catalog below is mirrored by `DataStoreCatalog`; a test fails if a store has no policy or is missing here.

| Store | Location | Protection |
|---|---|---|
| `WorkingCopy` | `.index/work/<name>/` | Copy of the pinned commit; owner-only; never inside `samples/` |
| `TokenSaveGraph` | `.index/work/<name>/.tokensave/` | Owner-only; read-only for the adapter; uploads disabled |
| `SearchIndex` | Elasticsearch volume | Bound to 127.0.0.1; Basic authentication |
| `EmbeddingCache` | `.index/cache/` | Owner-only; keys start with the authorization boundary |
| `ExporterCheckpoint` | `.index/state/` | Owner-only; advanced only after a complete publication |
| `ModelArtifacts` | `.index/models/` | Public weights; written only by the acquisition step |
| `ResultReports` | `results/` | Metrics and provenance only |
| `DiagnosticLogs` | stderr, `.index/logs/` | `DiagnosticEvent` only; owner-only files |
| `Traces` | local OpenTelemetry exporter, off by default | Local endpoint policy; identifiers only |
| `CliOutput` | stdout, stderr | JSON results; scrubbed error messages |

### Development versus production

| Concern | Development (this repository) | Production (not provided) |
|---|---|---|
| Transport | HTTP on loopback / Compose network, no TLS | TLS on every hop, including the inference sidecar and Elasticsearch |
| Elasticsearch | Basic license, authentication on, password defaults to `changeme` unless `ELASTIC_PASSWORD` is set in a git-ignored `.devcontainer/.env` | Secrets manager, role-based access per authorization boundary |
| Encryption at rest | Relies on the host disk | Disk or volume encryption, key management |
| Authorization state | In-memory `ScopeRegistry` | Durable, audited, bound to the caller's identity |
| Offline proof | Environment settings | Network egress controls |

## Safe failure messages

`SafeMessage.For` is the single policy for `DiagnosticEvent` and `CliOutput`: codes, exception types and identifiers are kept; messages of non-Polifemo exceptions are dropped; `GIT_FAILED`, `PREP_INDEX_FAILED` and `MUT_INVALID` (which wrap subprocess stderr or parser text) get a fixed text and their messages now hold only the exit code. Scrubbing redacts a private key from its BEGIN header through the END marker, or to the end of the message when unterminated.

Further hardening (review round 2): `LocalOnlyHandler` validates the endpoint on both `SendAsync` and the synchronous `Send`. `ProtectedStorage.WritePrivateFileAsync` takes the repository root and enforces containment under `.index/` and link-free ancestors at the write boundary. `ModelArtifactGate` resolves every component of a manifest file (ancestor symlinks included) before checking it lies under the artifact root, and tolerates a root with a trailing separator. Quoted secret assignments are redacted through their own closing delimiter (apostrophes, escapes and spaces inside the value are covered), and an `Include` path rule no longer bypasses binary and secret screening.
