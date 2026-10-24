# Automata do-work

This configuration adapts the schedule from `azdo-cli` for this repository:

- GitHub issue discovery is opt-in by the `automated` label.
- The do-work integration base is `develop`; production trunk is `master`.
- At most two model runs are allowed per five-minute tick.
- Only `alkampfergit` and `copilot` are authorized message authors; Automata posts as `alkampferoutlook`.
- Cron files are staged for later, but the scheduler is intentionally not auto-started: this repository requires local-only LLM processing, while Automata 0.13.0 exposes only cloud Claude/Codex executors. Do not run `start-do-work-cron.sh` until a compliant local executor is available and verified.

`AGENTS.md` remains authoritative. The private `samples/` corpora must never be accessed or sent to a model or external service. Runtime logs are kept in the container and are not committed. Automata may check out `develop` during preflight; the installed cron wrapper restores the setup branch after each clean tick so this configuration remains available until merged.
