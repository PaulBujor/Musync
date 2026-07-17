# Musync — Agent Guide

## Ground Truth

**`README.md`** is the authoritative reference for project intent, setup, and design decisions.

## Setup — Exact Commands

```bash
# Create solution (do NOT hand-edit .sln or .csproj)
dotnet new sln -n Musync

# Create projects
dotnet new console -n Musync -o src/Musync --framework net10.0
dotnet new xunit -n Musync.Tests -o tests/Musync.Tests --framework net10.0

# Wire up solution
dotnet sln add src/Musync/Musync.csproj
dotnet sln add tests/Musync.Tests/Musync.Tests.csproj
dotnet add tests/Musync.Tests reference src/Musync/Musync.csproj

# Add packages — use `dotnet add package`, never edit .csproj directly
dotnet add src/Musync package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/Musync package System.CommandLine
```

## Run Commands

| Mode | Command |
|------|---------|
| Direct | `dotnet run --project src/Musync -- <command>` (e.g. `spotify queue-albums`) |
| Docker (Postgres only) | `docker compose up` — `compose.yaml` starts only Postgres; it does **not** build the app |
| Docker (app + Postgres) | `docker compose -f compose.yaml -f compose.app.yaml up` (`compose.app.yaml` builds/runs the Musync image) |
| Tests | `dotnet test` (mock provider, no credentials needed) |
| Lint | `dotnet format --verify-no-changes` (CI gate; run `dotnet format` to fix) |

## CLI Command Tree (`Program.cs` is the source of truth)

`Program.cs` is both the composition root and the dispatcher: it builds the host, registers DI, parses `args`, then calls the `RunQueueAlbumsAsync` / `RunImportAsync` helpers.

- `spotify queue-albums` — sync saved albums into the Spotify queue playlist. Spotify only; `tidal queue-albums` is rejected at runtime (Tidal is import-source only).
- `spotify import --source <provider>` — import from another provider (source ≠ target; target must be Spotify). Tidal import reads your collection tracks via the v2 `userCollectionTracks` endpoint.
- Global recursive options: `--dry-run` (no provider mutation), `--limit <n>`.
- Deprecated aliases `sync` and `import-tidal` still dispatch (to `spotify queue-albums` / `spotify import --source tidal`) but log a warning and return exit code `3`.
- Exit codes: `0` success, `1` error/misconfiguration, `2` cancelled, `3` deprecated alias succeeded.

## Architecture (non-obvious from filenames)

- **`Options/`** not `Configuration/`
- **`Domain/Interfaces/`** — interfaces live with domain models, not in a separate abstractions project
- **`Infrastructure/`** contains only concrete implementations (no interfaces)
- **`Jobs/`** (plural) — follows .NET conventions
- **`tests/Fakes/`** — mock provider lives in test project, not production code

## Key Patterns

- **Provider selection (music)**: Providers are **keyed DI services** — `GetKeyedService<IMusicProvider>("spotify"|"tidal")` and `GetKeyedService<ITrackMapper>(...)`, resolved by name from the command, never injected directly. Tidal wiring is registered only when `Tidal:ApiBaseUrl` is set; targeting an unconfigured provider fails with exit code `1`. Mock provider lives in `tests/Fakes/` and is registered directly in test DI.
- **Jobs**: An orchestrator (`QueueAlbumsOrchestrator` / `ImportOrchestrator`) runs step classes in sequence, threading a `*RunContext` record that carries the resolved provider(s), playlist id, `dryRun`, and `limit`. Steps live in per-flow folders (`Jobs/Sync/`, `Jobs/Import/`); the orchestrator's call order — not a name prefix — defines execution order, and state flows step→step by return value. Orchestrators and step classes are DI-registered and DI-scoped; the provider reaches them via the context, not DI.
- **Provider selection (database)**: `Database:Provider` config (`"Sqlite"` or `"Postgres"`). Default: SQLite. Docker overrides to PostgreSQL via environment variable.
- **Auth**: PKCE OAuth with browser-based flow on first run; refresh token persisted (encrypted at rest) to the `RefreshTokens` table via `ISpotifyAuthenticator` / `SpotifyTokenHandler` (a `DelegatingHandler`)
- **Token management**: `SpotifyTokenHandler` is a `DelegatingHandler` — NOT mixed into `SpotifyMusicProvider`. Token refresh serialised with `SemaphoreSlim(1,1)`. New refresh tokens written immediately to DB in a dedicated `DbContext` transaction.
- **Pagination**: `IAsyncEnumerable<T>` on all paginated Spotify endpoints (lazy streaming, not full buffering)
- **Parallelism**: `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` (default: 3 concurrent) for album track-list fetches
- **Caching**: `IHybridCache` (in-process only, no distributed backend; `AddHybridCache()` with no extras). Caches only the liked-tracks `HashSet<string>` per run (`CacheKeys.LikedTracks`); track history and current queue contents are read fresh from the DB / provider each time.
- **Membership checks**: `HashSet<string>.Contains()` — never per-track SQLite or Spotify queries
- **Resilience**: Polly via `AddStandardResilienceHandler()` with custom `Retry-After` header support
- **Logging**: `[LoggerMessage]` source generators (no string interpolation). `JobRunId` pushed as a structured scope property.
- **Options**: `services.AddOptions<T>().BindConfiguration("Section").ValidateDataAnnotations()`. Provider options are validated lazily (on first use in the run helpers), not at host build, so a command targeting one provider doesn't fail because another is unconfigured. No `IConfiguration` injection below `Program.cs`.
- **EF Core**: SQLite + WAL mode (default) or PostgreSQL. Provider selected via `Database:Provider` config. SQLite uses `EnsureCreated()`; PostgreSQL uses `MigrateAsync()`. In tests, use real SQLite in-memory (not `UseInMemoryDatabase`).
- **Cancellation**: Every `async` method accepts `CancellationToken`. `System.CommandLine` wires `Ctrl+C`/`SIGTERM` to the root token.

## Testing

- No real HTTP calls — `IMusicProvider` boundary enforces this
- `LocalMockMusicProvider` in `tests/Fakes/` seeded per test
- Unit tests target `JobOrchestrator` and step classes in isolation
