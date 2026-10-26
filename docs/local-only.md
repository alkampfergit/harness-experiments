# Local-only processing and offline model execution

Policy source: AGENTS.md (rule 7), constitution principle I. Code: `src/Polifemo.Platform/Privacy/`.
Check it with `polifemo privacy check [--worktree <dir>]`.

## Endpoints

Configuration is server-side, from the environment only. Every URL is validated when it is loaded and again on each
request (`LocalOnlyHttp`, the only way product code opens a connection; a test scans for raw networking).

| Variable | Default | Purpose |
|---|---|---|
| `ELASTICSEARCH_URL` | `http://localhost:9200` | store |
| `POLIFEMO_EMBEDDING_URL` | `http://127.0.0.1:8081` | local embedding sidecar |
| `POLIFEMO_RERANKING_URL` | `http://127.0.0.1:8082` | local reranking sidecar |
| `POLIFEMO_ALLOWED_SERVICE_HOSTS` | `elasticsearch` | single-label Compose service names, comma separated |
| `POLIFEMO_MODEL_DIR` | `.index/models` | pre-acquired model artifacts |

Allowed: loopback and the allowlisted service names. Everything else is rejected with `LOCAL_ONLY_EXTERNAL_ENDPOINT`
naming the kind, the source (configuration or caller override) and the host; paths, queries and credentials are never
echoed. URLs with credentials are rejected (`LOCAL_ONLY_CREDENTIALS_IN_URL`); passwords come from
`ELASTICSEARCH_PASSWORD` or a git-ignored `.devcontainer/.env`. A caller override is validated like the configured value,
so it can pick another local service but never an external one. The HTTP transport ignores the system proxy and does not
follow redirects.

Hosted embeddings, hosted reranking and external query transmission are denied independently. Setting
`POLIFEMO_ALLOW_HOSTED_EMBEDDINGS`, `POLIFEMO_ALLOW_HOSTED_RERANKING` or `POLIFEMO_ALLOW_EXTERNAL_QUERIES` is an error
(`LOCAL_ONLY_HOSTED_DENIED`), not an opt-in. Only a separate, authorized change to AGENTS.md can permit them.

## Models: acquisition is a separate step

Runs never download a model. `ModelArtifacts.Require` fails with `MODEL_ARTIFACT_MISSING` and the directory it looked in.

1. **Review** the model and add an entry to a review file (JSON list of `model`, `revision`, `license`,
   `requiresRemoteCode`, `remoteCodeRevision`, `reviewedBy`, `reviewedOn`). The revision is a 40-character commit SHA.
   The license must be recorded. A model that needs remote code is refused until that code is pinned to a commit SHA
   and reviewed.
2. **Acquire** the pinned revision once, outside any run, into `<POLIFEMO_MODEL_DIR>/<org_name>/<revision>/`. Only public
   weights are fetched; no sample content is sent.
3. **Verify** offline: `polifemo privacy verify-model --model <org/name> --revision <sha> --reviews <file>`.

Model-serving processes start with `OfflineEnvironment.Variables` (`HF_HUB_OFFLINE=1`, `TRANSFORMERS_OFFLINE=1`,
`HF_DATASETS_OFFLINE=1`, telemetry off). `privacy check` reports any that the current environment lacks.

## TokenSave and the network

`TokenSaveUploads.EnsureDisabled` requires `report_savings` to be `false` in the worktree's `.tokensave/config.json`;
`postcreate.sh` also runs `tokensave disable-upload-counter`. That is one layer: it does not stop every possible request.
For an offline run, also cut egress at the network, for example `docker network disconnect bridge <container>` while
keeping the Compose network, or an egress deny rule on the host. Runs that need only the local store, the local
sidecars and pre-acquired models then work unchanged.

## Credentials

Never in tracked files: use environment variables or git-ignored `.env` files. Results and diagnostics carry no
credentials.
