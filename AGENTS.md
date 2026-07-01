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
| Direct | `dotnet run --project src/Musync` |
| Docker | `docker compose up` (uses `compose.yaml`) |
| Tests | `dotnet test` (mock provider, no credentials needed) |

## Architecture (non-obvious from filenames)

- **`Options/`** not `Configuration/`
- **`Domain/Interfaces/`** — interfaces live with domain models, not in a separate abstractions project
- **`Infrastructure/`** contains only concrete implementations (no interfaces)
- **`Jobs/`** (plural) — follows .NET conventions
- **`tests/Fakes/`** — mock provider lives in test project, not production code

## Key Patterns

- **Provider selection**: Always Spotify in production. Mock provider lives in `tests/Fakes/` and is registered directly in test DI.
- **Auth**: PKCE OAuth with browser-based flow on first run; refresh token persisted to `AppSettings` table via `ISpotifyAuthenticator` / `SpotifyTokenHandler` (a `DelegatingHandler`)
- **Token management**: `SpotifyTokenHandler` is a `DelegatingHandler` — NOT mixed into `SpotifyMusicProvider`. Token refresh serialised with `SemaphoreSlim(1,1)`. New refresh tokens written immediately to DB in a dedicated `DbContext` transaction.
- **Pagination**: `IAsyncEnumerable<T>` on all paginated Spotify endpoints (lazy streaming, not full buffering)
- **Parallelism**: `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` (default: 3 concurrent) for album track-list fetches
- **Caching**: `IHybridCache` (in-process only, no distributed backend; `AddHybridCache()` with no extras). Used for per-run `HashSet<string>` lookups (liked tracks, track history, queue contents).
- **Membership checks**: `HashSet<string>.Contains()` — never per-track SQLite or Spotify queries
- **Resilience**: Polly via `AddStandardResilienceHandler()` with custom `Retry-After` header support
- **Logging**: `[LoggerMessage]` source generators (no string interpolation). `JobRunId` pushed as a structured scope property.
- **Options**: `services.Configure<T>().BindConfiguration("Section").ValidateDataAnnotations().ValidateOnStart()`. No `IConfiguration` injection below `Program.cs`.
- **EF Core**: SQLite + WAL mode. Migrations applied on startup via `dbContext.Database.MigrateAsync()`. In tests, use real SQLite in-memory (not `UseInMemoryDatabase`).
- **Cancellation**: Every `async` method accepts `CancellationToken`. `System.CommandLine` wires `Ctrl+C`/`SIGTERM` to the root token.

## Testing

- No real HTTP calls — `IMusicProvider` boundary enforces this
- `LocalMockMusicProvider` in `tests/Fakes/` seeded per test
- Unit tests target `JobOrchestrator` and step classes in isolation
