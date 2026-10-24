# AGENTS.md

This file is the single authoritative source of instructions for every coding agent and harness working in this repository. `CLAUDE.md` only points here. Do not duplicate rules elsewhere.

## Purpose

This repository hosts **experiments on indexing source code and documentation** (focus: C#) so that agent harnesses can find relevant information about a codebase starting from a natural-language prompt.

Each experiment explores a different technique (lexical/BM25, embeddings, hybrid fusion, Roslyn symbol graph, chunking strategies, embedding models, MCP exposure, ...). The goal is to compare techniques with evidence, not to build one definitive system up front.

## Repository layout

```
AGENTS.md        # this file (authoritative)
CLAUDE.md        # pointer to AGENTS.md only
samples/         # git submodules (git-ignored): the code used as test corpora
testrepo/        # committed git submodules of PUBLIC repositories, used only by automated tests
Polifemo.slnx    # the .NET solution (SDK pinned in global.json)
src/             # Polifemo.Contracts, Polifemo.Platform, Polifemo.Cli (the `polifemo` tool),
                 # Polifemo.Tests (one test project, folders named after the section tested),
                 # fixtures/ (plain-file test repositories, never compiled)
docs/            # technology.md, backlog/, platform-compatibility.md (pinned versions and observed behavior)
tools/           # helper processes (e.g. tools/embed-server/ Python sidecar)
eval/            # queries + gold labels + metric runners (shared by all experiments)
experiments/     # one folder per experiment/technique
results/         # metrics output per run, comparable across experiments
```

Create folders only when they are needed; do not add empty scaffolding.

## Samples

- `samples/` contains **git submodules**. Each submodule is a real-world C# project (and optionally its docs) used as test data.
- `samples/` is **git-ignored** because the sample repos may be private. Neither their content nor their URLs are committed here. To add one locally use `git submodule add -f <url> samples/<name>`, and do not commit the resulting `.gitmodules` or gitlink changes. Keep a local, untracked list of the samples used (`samples.local.md`) so runs can be reproduced.
- Treat the **source files** under `samples/` as read-only. Never edit, commit into, or reformat submodule content.
- Pin each submodule to a specific commit so results are reproducible. Record the commit in the results of every run.
- **Public test corpora.** `testrepo/<name>` MAY hold committed git submodules of public repositories whose license permits redistribution and local analysis, used only by automated tests. Each is pinned to a commit; its URL, commit, license and approval date are recorded in `testrepo/corpora.json`. Changing the pin is a reviewed change that updates the expectation files in the same commit. The `samples/` rules for read-only sources and no generated artifacts apply equally: tests index a copy under `.index/work/<name>/`. Private repositories MUST NOT be added under `testrepo/`, and the "do not commit `.gitmodules` or gitlinks" rule above applies to `samples/` only.
- Do not generate artifacts inside `samples/` (this includes tokensave's `.tokensave/` folder). Index a git worktree/copy of the pinned commit under `.index/work/<name>/` instead, and write all indexes and caches under `.index/`.

## Architecture (working hypothesis)

The first experiment is the **tokensave bridge**: tokensave parses the code and keeps a SQLite graph, and a bridge copies that data incrementally into a local Elasticsearch for BM25 + vector retrieval.

```
sample (pinned commit) -> worktree copy -> tokensave sync -> tokensave SQLite DB
   -> bridge (read-only SQLite, hash diff, deletions)
   -> chunk builder (source slice + signature + doc + neighbor names)
   -> embedder (local model, cached by hash+model+settings)
   -> Elasticsearch (bulk upsert/delete, one index per technique+model)
   -> MCP server exposing search tools to harnesses
```

Design points:

- **Stable keys.** Document id = `sample + path + qualified name + kind`, never tokensave's internal node id or line numbers. A content hash decides whether a document changed.
- **Change detection.** Open the tokensave DB read-only; do not watch the file system. Poll per-file hashes or run after an explicit `tokensave sync`. Detect deletions by diffing id sets (or a periodic reconcile). Resolve the active DB file from `.tokensave/branch-meta.json`.
- **Schema isolation.** tokensave's tables (`nodes`, `edges`, `files`) are not a public API. Pin the tokensave version and read them only through one adapter class.
- **Fusion.** Fuse BM25 and vector results client-side (reciprocal rank fusion) so the ranking is ours to ablate; do not depend on licensed Elasticsearch features.
- **Rebuilds.** Build into a new index and swap an alias. Index name encodes technique and embedding model, since vector dimensions are fixed per mapping.
- **Baselines to compare against:** tokensave alone, Elasticsearch BM25 alone, and (later) a Roslyn-based extractor.

## Technology decisions

- **Bridge, indexer, MCP server, eval runner: C# (.NET).** Reasons: team language and target language, Roslyn available as a later comparison, hosted-service model for the daemon, typed Elasticsearch client. Considered and not chosen now: Rust (little gain, since time is spent in embeddings and Elasticsearch) and Node/TypeScript (equally viable; revisit if the team prefers it).
- **Dev environment:** the devcontainer (`.devcontainer/`) is a Docker Compose stack: the `devcontainer` service (.NET 10, Node, Python 3.12, uv, agent CLIs, tokensave) and an `elasticsearch` service. From inside the devcontainer use `ELASTICSEARCH_URL` / `ELASTICSEARCH_USERNAME` / `ELASTICSEARCH_PASSWORD` (preset to `http://elasticsearch:9200`).
- **Store:** Elasticsearch 9 (pinned to a specific version in `.devcontainer/docker-compose.yml`), single node, Basic license, authentication on, HTTP without TLS, port bound to `127.0.0.1`. The password defaults to `changeme`; override it with `ELASTIC_PASSWORD` in a git-ignored `.devcontainer/.env`. Keep a lightweight SQLite (FTS5 + vector) implementation behind the same interface as a baseline.
- **Embeddings and reranking:** behind `IEmbedder` and `IReranker` interfaces. The implementation is an HTTP sidecar in `tools/embed-server/` (Python, FastAPI, `sentence-transformers`, managed with uv) bound to `127.0.0.1`. **Ollama is not used.** An in-process ONNX Runtime implementation may be added later for comparison. Note: inside the container (Docker on macOS) models run on CPU only.
- **Embedding hygiene.** Apply each model's required prefixes, pooling, normalization and max length (e.g. `nomic-embed-text` needs `search_document:` / `search_query:`). Cache embeddings by `hash(text) + model + revision + settings`; never mix vectors from different settings in one index.
- **Model downloads.** Weights may be downloaded from Hugging Face once (no sample content is sent); afterwards run offline (`HF_HUB_OFFLINE=1`). Pin model revisions and record model + revision in every result.

## Experiment rules

1. **Evaluate before optimizing.** Every technique is judged on the shared eval set in `eval/` (queries with expected files/symbols). Metrics: recall@k and MRR at minimum; add token/tool-call cost when testing through a harness.
2. **One experiment, one folder** under `experiments/<name>/` with a short `README.md` stating: hypothesis, technique, how to run, and results summary.
3. **Change one variable at a time** (chunking, embedding model, ranking, store) so differences are attributable.
4. **Keep a baseline.** Always compare against a plain lexical baseline (BM25 or grep-like) on the same eval set.
5. **No hand-tuned magic weights without ablation.** Any boost or heuristic must be justified by a measured gain.
6. **Reproducible runs.** Scripts must take the sample path and commit as inputs and write results to `results/` with the technique name, sample commit and date.
7. **Local only.** Sample code and docs must never leave the machine. Use local models (Hugging Face models served by the local Python sidecar) for embeddings and any LLM step, and local services (Elasticsearch in Docker bound to localhost). No cloud embedding/LLM APIs, hosted vector stores or telemetry may receive sample content. Experiments must work offline.
8. **Primary store: Elasticsearch** (BM25 + dense vector in one engine). Keep store access behind a small interface so other stores can be compared.

## Spec Kit workflow

- Features developed with Spec Kit (`/speckit-*` skills, artifacts under `specs/`) MUST also satisfy the gates in `.specify/memory/constitution.md`: the Constitution Check in every spec and plan, the required sync/publication test coverage and the correctness gates. These gates are defined only there and apply in addition to this file.
- The constitution restates this file in a form Spec Kit can check. If the two conflict, this file prevails and the constitution is amended to match. A constitution amendment that changes repository policy MUST update this file in the same change.

## Conventions

- Primary language of the indexed targets: **C#**. Experiment code is C# (see Technology decisions); other languages only for helper tools under `tools/`.
- Keep generated data (indexes, embeddings, caches) out of git; add it to `.gitignore`.
- **Zero warnings.** `dotnet build` and `dotnet test` MUST finish with 0 warnings, in the code and in the tests. `Directory.Build.props` sets `TreatWarningsAsErrors`, so CI fails on a warning. Fix the cause (for example pass `TestContext.Current.CancellationToken` where an analyzer asks for it). Do not hide a warning with `#pragma warning disable`, `[SuppressMessage]`, `<NoWarn>` or a lowered warning level; if a suppression is truly unavoidable, scope it as narrowly as possible, put the reason in a comment next to it and mention it in the commit message.
- Commit small, focused changes; do not commit unrelated files.
- Do not add dependencies or services without noting why in the experiment README.

## Working with agents

- Use the code-navigation tools available in the environment before reading whole files.
- Never paste sample code into cloud services or external tools; harnesses under test must be configured with local models when they would see sample content.
- Ask before destructive or outward-facing actions (force pushes, deleting data, publishing).
- When results are inconclusive or negative, record them; they are part of the deliverable.
