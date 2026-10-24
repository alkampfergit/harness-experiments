# Automata do-work

This configuration adapts the schedule from `azdo-cli` for this repository:

- GitHub issue discovery is opt-in by the `automated` label.
- The do-work integration base is `develop`; production trunk is `master`.
- At most two model runs are allowed per five-minute tick.
- Only `alkampfergit` and `copilot` are authorized message authors; Automata posts as `alkampferoutlook`.
- Cron runs inside the Dev Container. Authentication must be configured inside that container.

`AGENTS.md` remains authoritative. The private `samples/` corpora must never be accessed or sent to a model or external service. Runtime logs are kept in the container and are not committed.
