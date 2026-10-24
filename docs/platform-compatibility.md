# Platform compatibility record

Status: **complete for this feature, except two runs that need the rebuilt devcontainer** (tasks T059
and T060 of `specs/001-platform-foundations/tasks.md`): the verification inside the devcontainer with
the Compose Elasticsearch service, and the offline run with the 10-minute budget. Everything below was
observed on a macOS host with TokenSave 7.16.0 and a local Elasticsearch 9.5.5 (Basic).

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

Reading a database that TokenSave left in WAL mode (no `-wal` or `-shm` file present) makes SQLite
create empty `-wal` and `-shm` files next to it, even with `Mode=ReadOnly`. The database bytes are
unchanged (checked by hash). The check treats a missing and an empty `-wal` as the same, so this is
not reported as `TS_DB_MODIFIED`; any real change to the main file or to a non-empty log still is.

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

Observed with TokenSave 7.16.0 on the synthetic fixture (`src/fixtures/synthetic-csharp`, recorded in
`expectations/*.json`) and on the pinned public corpus `testrepo/cisharpai` (445 C# files, 602 indexed
files, about 4.4k call edges; indexing takes about 2 s).

### Symbol kinds and relationships

| | Fixture | Corpus (additionally) |
|---|---|---|
| node kinds | `class`, `inner_class`, `interface`, `record`, `enum`, `enum_variant`, `method`, `constructor`, `csharp_property`, `field`, `module` (a Markdown heading), `doc`, `file`, `namespace`, `use` | `annotation_usage`, `const`, `struct` |
| edge kinds | `calls`, `uses`, `implements`, `documents` | `annotates`, `extends` |

Representation details the identity code depends on:
- `qualified_name` is `<path>::<path>::Type::member` for code and `<path>::Title` for Markdown. It has no
  namespace and no generic arguments. The in-file name is what is left after the path prefixes.
- Partial classes are not merged: each file has its own `class` node, so the path tells them apart.
- Nested types with the same simple name differ by their parent (`Cache::Entry`, `Index::Entry`).
- Overloads share a qualified name. Only the signature and the source order tell them apart.
- `start_line` and `end_line` are **0-based** and inclusive. A method that has an attribute starts at the
  attribute line. A class does not start at its XML documentation comment.
- The `signature` of an expression-bodied member **contains the body** (`public Money Add(Money other) => new(...)`).
  Identities therefore cut the signature at the first `=>` outside brackets.
- `node_id` values are hashes that are not meaningful across runs; they are provenance only.

### Documentation association

- **Sidecar:** `Foo.readme.md` next to `Foo.cs` documents that file. `Order.readme.md` documents both
  `Order.cs` and `Order.Totals.cs`. TokenSave creates a `doc` node and `documents` edges.
- **Central:** a Markdown file under `docs_dir` (`tokensave-docs/`) with an `applies_to` list in its front
  matter documents the files that match. The fixture lists literal paths.
- **Stale:** an `applies_to` entry that matches no file produces neither a `doc` node nor an edge. The
  document is indexed as plain Markdown only, so staleness is visible only as a missing association.
- **Plain Markdown** outside `docs_dir` without an association (`docs/architecture.md`) is indexed as a
  `module` node for its heading and linked to nothing.

### Branches and worktrees

- `.tokensave/branch-meta.json` is `{ default_branch, branches: { <name>: { db_file, created_at, last_synced_at } } }`.
  A fresh `tokensave init` writes one entry with `db_file: "tokensave.db"`. A detached checkout still gets a
  `main` entry.
- Each working directory has its own `.tokensave/` and database. A second git worktree of the same clone, with
  uncommitted edits, indexes its own content: the `content_hash` of the edited file differs between the two
  databases and every untouched file has the same hash.
- `tokensave init` adds `.tokensave/` to the repository's `.git/info/exclude`. Preparing a sample therefore
  indexes a clone under `.index/work/` and never the sample itself.
- A global git hook installed by `tokensave install` can run during clone and checkout. All git calls in this
  project run with the user and system configuration disabled and an empty `core.hooksPath`.

### Unsupported and ambiguous cases

Recorded in the expectation records with their status. None of them is counted as a pass.

| Case | Observed behavior |
|---|---|
| Extension-method call (`"Spring Sale".ToSlug()`, `services.AddOrders()`, a generic `AddScoped<..>()`) | `unresolved`: the reference text is kept, no edge. |
| Object creation (`new Order(...)`) | `unresolved`: not linked to a constructor. |
| Overload chosen by argument type (`Money.Add(decimal)`) | `unsupported`: the call is linked to the **first** overload, `Add(Money)`. The edge exists but is wrong. |
| Call on an interface-typed variable that the DI container fills | Linked to the interface member only, never to an implementation (`unsupported` for the implementation). |
| Call whose name exists on several types | `ambiguous`. Candidates are chosen **by name only**: `List<OrderLine>.Add` is reported as ambiguous between the two `Money.Add` overloads, and `List<string>.AddRange` in the corpus between the `InMemoryRetriever.AddRange` overloads. The candidates can be unrelated to the real target. |
| Overload calling its sibling (`Format(order, "EUR")`) | `ambiguous`, and the candidate list includes the caller itself. |
| File moved to another folder | After the `rename-file` mutation, `Order.Total` is no longer linked to `OrderLine.LineTotal`, although the code is unchanged. The result is deterministic. A move can therefore drop cross-file edges until something re-resolves them. |
| UTF-8 with BOM, CRLF, ISO-8859-1 and **UTF-16 LE with BOM** files | All four are extracted with correct symbols. |

Tree-sitter style extraction is not compiler-resolved: these relationships must never be presented as
exact (Constitution VI).

No folder under `experiments/` is created by this feature (Constitution VII: folders only when a
deliverable needs them).
