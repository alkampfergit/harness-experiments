# Platform compatibility record

Status: **partial**. The pins, licenses, network assumptions and the Elasticsearch capability
findings are final for this feature. The sections marked *pending* are completed after the
synthetic fixture exists (task T056 of `specs/001-platform-foundations/tasks.md`).

Machine-checked parts of this record: `polifemo platform check` (TokenSave) and
`polifemo platform smoke` (Elasticsearch). Where this document and those checks disagree, the
checks and `src/Polifemo.Platform/PlatformPins.cs` win and this document is wrong.

## 1. Pins

| Component | Supported version | Where it is pinned | Notes |
|---|---|---|---|
| TokenSave | **7.16.0** | `.devcontainer/postcreate.sh` (release tag + sha256 per architecture), `PlatformPins.TokenSave` | Both `tokensave --version` and the database's `metadata.last_full_index_version` must equal the pin. |
| Elasticsearch | **9.5.5**, Basic license | `.devcontainer/docker-compose.yml` (image tag), `PlatformPins.Elasticsearch` | Single node, authentication on, HTTP without TLS, bound to `127.0.0.1`. |
| .NET SDK | **10.0.402** minimum, roll forward to the latest 10.0 patch | `global.json` | `dotnet test` uses `Microsoft.Testing.Platform` (opted in through `global.json`), as .NET 10 requires for xUnit v3. |
| Python | **3.12** | devcontainer feature `python:1` (`version: 3.12`) | This feature contains no Python code. The sidecar under `tools/` arrives with a later feature. |
| git | any recent release | not pinned | Fixture determinism depends on fixed dates and identities, not on the git version. |
| uv | not pinned yet | installed by `postcreate.sh` | Needed only by the later sidecar. |

The sha256 values in `postcreate.sh` were checked against the `SHA256SUMS` file published with the
TokenSave v7.16.0 release:

| Asset | sha256 |
|---|---|
| `tokensave-v7.16.0-aarch64-linux.tar.gz` | `b1b28daf21ea51f44ccfd1ea3101505d6db644a055cef261bcdb3beb8369d03f` |
| `tokensave-v7.16.0-x86_64-linux.tar.gz` | `783dcea8bb8b472982b497523ad912a5176b8d8e770d25592dc38b5f749ac7ea` |

**No other Elasticsearch version is supported.** In particular nothing here implies support for
7.x or 8.x, and other 9.x releases are reported as `ES_VERSION_UNSUPPORTED` until this record and
the pin are changed together in one reviewed change.

Verification status of the devcontainer: the compatibility check and the smoke test were run on a
macOS host against TokenSave 7.16.0 and a local Elasticsearch 9.5.5 (Basic). A run inside the
rebuilt devcontainer, with the Compose Elasticsearch service, is still to be recorded (task T059).

## 2. TokenSave database expectation

The adapter relies on six tables. The expected columns were generated with `PRAGMA table_info` from
a database created by the pinned 7.16.0 binary and are embedded as
`src/Polifemo.Platform/TokenSave/expected-schema.v7.16.0.json`. Rules and error codes are in
`specs/001-platform-foundations/contracts/tokensave-schema.md`.

Observed on 7.16.0: the database has many tables that the adapter does not read (full-text,
vector, memory, `read_cache`, `node_fingerprints`, `trait_dispatch_callers`) and 14 extra columns in
`nodes` plus `unresolved_refs.id`. They are reported as warnings (`TS_EXTRA_TABLE`,
`TS_EXTRA_COLUMN`), never silently ignored.

## 3. Elasticsearch capabilities under Basic (verified)

Checked on 9.5.5 with a Basic license:

| Feature | Result | Used by this project |
|---|---|---|
| `match` query (BM25) | works | yes |
| top-level `knn` search on an indexed `dense_vector` | works | yes |
| `retriever.rrf` (Reciprocal Rank Fusion) | **403** `current license is non-compliant for [Reciprocal Rank Fusion (RRF)]` | no |
| `retriever.text_similarity_reranker` | **403** `current license is non-compliant for [text_similarity_reranker]` | no |

Consequence: fusion is reciprocal rank fusion implemented client-side in C# and reranking runs in the
local sidecar. Inference endpoints are not used either.

## 4. Licenses (assumptions, not legal advice)

| Component | License assumption |
|---|---|
| TokenSave | MIT (GitHub license metadata of `aovestdipaperino/tokensave`). |
| Elasticsearch 9.x | Elastic-distributed binary under the Basic license; the source is available under Elastic License 2.0, SSPL or AGPL. Only Basic features are used. |
| .NET | MIT. |
| Python | PSF License. |
| `testrepo/cisharpai` | MIT is declared in `Directory.Build.props` only. The repository has no LICENSE file (see `testrepo/corpora.json`). |

## 5. Network and offline assumptions

Needs the network once:
- pulling the Elasticsearch image;
- downloading the pinned TokenSave release (checked against the sha256 above);
- `git submodule update --init testrepo/cisharpai`;
- restoring NuGet packages.

Must run offline afterwards: `polifemo platform check`, `polifemo platform smoke` (it only talks to
the configured localhost store), fixture preparation and every test.

TokenSave uploads: `tokensave disable-upload-counter` runs in `postcreate.sh`, and
`report_savings` is set to `false` in the project's `.tokensave/config.json`. Constitution I asks
for network controls and not only counters, so the offline check in quickstart step 6 (egress
disabled) remains the actual proof. It is still to be recorded (task T060).

## 6. TokenSave languages

`metadata.language_detection` lists the extensions TokenSave knows. This project targets only C#
(`cs`) and Markdown (`md`). Every other language in that list is out of scope and unverified here.

## 7. Extraction, documentation association, branches and worktrees

*Pending (T056).* This part is filled from observations on the synthetic fixture:
- C# node kinds and edge kinds actually extracted;
- how sidecar and central documentation is associated (`docs_dir`, `applies_to`);
- branch and worktree behavior, including per-branch database files;
- unsupported and ambiguous cases, including the UTF-16 outcome and extension-method and
  dependency-injection calls.

Observed so far: `.tokensave/branch-meta.json` has the shape
`{ default_branch, branches: { <name>: { db_file, created_at, last_synced_at } } }`, and a fresh
`tokensave init` in a git repository creates one entry for the current branch with
`db_file: "tokensave.db"`.
