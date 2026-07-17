# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Ground truth

- **`README.md`** — project intent, setup, configuration reference, and design rationale.
- **`AGENTS.md`** — architecture conventions and key patterns. Read it before writing code; the notes below extend it rather than repeat it.

## Commands

| Task | Command |
|------|---------|
| Build | `dotnet build` (CI uses `-c Release`) |
| Run tests | `dotnet test` (mock provider, no credentials needed) |
| Single test | `dotnet test --filter "FullyQualifiedName~<ClassOrMethodName>"` |
| Run app | `dotnet run --project src/Musync -- <command>` |
| Lint (must pass CI) | `dotnet format --verify-no-changes` — run `dotnet format` to fix |

CI (`.github/workflows/`) runs restore → build → test and `dotnet format --verify-no-changes` on push/PR to `main`. A formatting violation fails the build.

**Never hand-edit `.slnx`, `.csproj`, or add packages by editing files** — use `dotnet sln add` and `dotnet add package` (see `AGENTS.md`).

## Command tree (source of truth: `Program.cs`, not README)

The CLI has been restructured beyond what `README.md` documents. Current structure:

- `spotify queue-albums` / `tidal queue-albums` — sync saved albums into that provider's queue playlist.
- `spotify import --source <spotify|tidal>` / `tidal import --source ...` — import from another provider (source and target must differ).
- Global recursive options: `--dry-run` (preview, no provider mutation), `--limit <n>`.
- Deprecated aliases still wired: `sync` → `spotify queue-albums`, `import-tidal` → `spotify import --source tidal`. These log a deprecation warning and return exit code `3` on success.

Exit codes: `0` success, `1` error/misconfiguration, `2` cancelled (Ctrl+C / SIGTERM), `3` a deprecated alias succeeded.

## Architecture notes (beyond AGENTS.md)

- **`Program.cs` is the composition root and the dispatcher.** It builds the host, selects the DB provider, registers all DI, then parses `args` and calls `RunQueueAlbumsAsync` / `RunImportAsync` helpers. Providers and track mappers are resolved by **keyed services** (`GetKeyedService<IMusicProvider>("spotify"|"tidal")`), never injected directly.
- **Provider wiring is conditional.** Tidal HTTP client, authenticator, and keyed `IMusicProvider` are only registered when `Tidal:ApiBaseUrl` is configured. A command targeting an unconfigured provider fails gracefully with exit code `1`.
- **Jobs are orchestrator + numbered step classes.** `QueueAlbumsOrchestrator` / `ImportOrchestrator` run `Step1/2/3` in sequence, threading a `*RunContext` record (holds the resolved provider(s), playlist id, `dryRun`, `limit`). Step classes are DI-scoped; the provider is passed via the context, not resolved by the step.
- **Two providers, per-provider `queue-albums` and cross-provider `import`.** When adding a provider: create `Infrastructure/<Provider>/` (client, authenticator, token handler, models), register a keyed `IMusicProvider` and (if it can be an import target) a keyed `ITrackMapper` in `Program.cs`, and add the command nodes.
