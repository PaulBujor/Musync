# Spotify Queue Manager — Requirements

## Overview

A CLI tool that runs on demand — `dotnet run` or `docker compose up` — syncs your Spotify library to a designated queue playlist, then exits. Albums you’ve saved funnel into the playlist; tracks you’ve liked or previously dismissed are removed and excluded going forward. After each run, a summary is logged to stdout. The tool is designed to be run as many times a day as needed.

-----

## Infrastructure

|Concern         |Decision                                                                                                                   |
|----------------|---------------------------------------------------------------------------------------------------------------------------|
|Runtime         |Docker Compose (local); or `dotnet run` directly                                                                           |
|Entry point     |`System.CommandLine` — runs `sync` by default, extensible with subcommands later                                           |
|Framework       |.NET 10                                                                                                                    |
|Configuration   |`IOptions<T>` pattern via `Microsoft.Extensions.Options`; values supplied as environment variables (`.env` file in Compose)|
|Database        |SQLite (single file, easy to back up, no extra container); WAL mode enabled                                                |
|ORM & Migrations|EF Core with `dotnet ef migrations` — schema versioned in source, applied on startup                                       |
|Cache           |`IHybridCache` (in-process only, no distributed backend); memory-only via default registration                             |
|IDs             |UUIDv7 via `Guid.CreateVersion7()` (.NET 9+) — time-ordered, globally unique, no auto-increment                            |
|Logging         |Serilog with console sink; structured output                                                                               |

-----

## Prerequisites — Manual Steps

> These steps cannot be automated by a coding agent and must be completed before first deployment.

### 1. Spotify Developer App

1. Go to [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) and create a new app.
1. Set the **Redirect URI** to `http://localhost:5000/callback` (used only during initial auth).
1. Note down **Client ID** and **Client Secret**.
1. Under app settings, enable the following scopes:
- `user-library-read` — read saved albums
- `playlist-modify-public` or `playlist-modify-private` — write to the queue playlist
- `user-library-read` — check liked tracks

### 2. Docker Networking Note for Auth

On first run the tool opens `http://localhost:5000/callback` in a browser to complete OAuth. When running via `dotnet run` this works automatically. When running via `docker compose up`, the container must publish port 5000 to the host (`ports: ["5000:5000"]`) so the browser redirect reaches the listener inside the container. This is the only time port 5000 is used — it is not a persistent service.

### 3. Create the Queue Playlist

Manually create a Spotify playlist named whatever you like (e.g. *“Queue”*). Copy its **Playlist ID** from the share link — it is the string after `playlist/` in the URL. Paste it into your `.env`.

-----

## Configuration

Configuration follows the standard .NET layered model: `appsettings.json` defines the shape and safe defaults, and is checked into source control. Secrets and environment-specific values are supplied via environment variables at runtime (`.env` file in Docker Compose), using .NET’s `__` double-underscore convention to map into the options hierarchy.

### `appsettings.json`

```json
{
  "Provider": "Spotify",
  "Spotify": {
    "ClientId": "",
    "ClientSecret": "",
    "QueuePlaylistId": "",
    "RequestDelayMs": 100,
    "MaxRetries": 3
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "/dev/null"  // unused — console sink only; retained for config shape
        }
      }
    ]
  }
}
```

### Environment Variable Overrides (`.env`)

Environment variables shadow `appsettings.json` values using `Section__Key` notation. Only secrets and values that differ per environment need to be set.

```env
Provider=Spotify   # Spotify | Mock

Spotify__ClientId=
Spotify__ClientSecret=
Spotify__QueuePlaylistId=
```

### Strongly-Typed Options Classes

Each section binds to a dedicated options record, registered via `services.Configure<T>()`:

```
SpotifyOptions       → "Spotify"
```

The `Provider` key binds to a `MusicProviderType` enum, not a free string:

```csharp
public enum MusicProviderType { Spotify, Mock }
```

Registration is driven by this value in `Program.cs`:

```csharp
var providerType = builder.Configuration.GetValue<MusicProviderType>("Provider");

switch (providerType)
{
    case MusicProviderType.Spotify:
        builder.Services
            .AddHttpClient<IMusicProvider, SpotifyMusicProvider>()
            .AddHttpMessageHandler<SpotifyTokenHandler>()
            .AddStandardResilienceHandler(...);
        break;

    case MusicProviderType.Mock:
        builder.Services.AddSingleton<IMusicProvider, LocalMockMusicProvider>();
        break;

    default:
        throw new InvalidOperationException($"Unknown music provider: {providerType}");
}
```

Setting `Provider=Mock` in `.env` (or as a `dotnet run` argument) loads the local mock with no Spotify credentials required — no environment name magic, no conditional compilation.

> **Serilog note:** Serilog is configured from `appsettings.json` using `UseSerilog(context, config => config.ReadFrom.Configuration(context.Configuration))`. Only the console sink is used — the tool is short-lived, so log files are unnecessary. Docker captures console output via `docker compose logs`.

-----

## Data Model (SQLite + EF Core)

SQLite is opened in **WAL mode** (`PRAGMA journal_mode=WAL`) to allow concurrent reads without blocking writes. EF Core migrations manage all schema changes; migrations are applied automatically on startup via `dbContext.Database.MigrateAsync()`.

### `JobRun`

One row per run. Acts as the audit log and the source of data for the run summary.

|Column                |Type     |Notes                                              |
|----------------------|---------|---------------------------------------------------|
|`Id`                  |TEXT PK  |UUIDv7                                             |
|`StartedAt`           |DATETIME |UTC                                                |
|`FinishedAt`          |DATETIME?|Null if still running or crashed                   |
|`Status`              |TEXT     |`"succeeded"`, `"failed"`, `"partial"`             |
|`TracksAdded`         |INTEGER  |                                                   |
|`TracksRemovedLiked`  |INTEGER  |                                                   |
|`TracksRemovedManual` |INTEGER  |                                                   |
|`TracksSkipped`       |INTEGER  |                                                   |
|`NewAlbumsEncountered`|INTEGER  |                                                   |
|`QueueSizeAfter`      |INTEGER  |                                                   |
|`ErrorMessage`        |TEXT?    |Top-level exception message if status is `"failed"`|

### `TrackHistory`

Tracks every track that has ever entered the queue playlist. This is the source of truth for “do not re-add” logic.

|Column          |Type     |Notes                                                       |
|----------------|---------|------------------------------------------------------------|
|`Id`            |TEXT PK  |UUIDv7                                                      |
|`JobRunId`      |TEXT FK  |References `JobRun.Id` — the run that first added this track|
|`SpotifyTrackId`|TEXT     |Unique Spotify track URI; indexed                           |
|`TrackName`     |TEXT     |For human-readable reporting                                |
|`ArtistName`    |TEXT     |                                                            |
|`AlbumName`     |TEXT     |                                                            |
|`AddedAt`       |DATETIME |When it was first added to the queue                        |
|`RemovedAt`     |DATETIME?|Set when removed (liked or manual removal detected)         |
|`RemovalReason` |TEXT?    |`"liked"` or `"manual"`                                     |

### `ProcessedAlbums`

Tracks which Spotify albums have already had their full track list fetched and processed. Prevents re-hitting the Spotify API for every album on every run — only newly saved albums require track-list calls.

|Column            |Type    |Notes                                                    |
|------------------|--------|---------------------------------------------------------|
|`Id`              |TEXT PK |UUIDv7                                                   |
|`SpotifyAlbumId`  |TEXT    |Unique Spotify album URI; indexed                        |
|`AlbumName`       |TEXT    |                                                         |
|`ArtistName`      |TEXT    |                                                         |
|`FirstProcessedAt`|DATETIME|When the album was first seen and fully processed        |
|`LastSeenAt`      |DATETIME|Updated each run the album is still in the user’s library|

### `AppSettings`

General-purpose key-value store for runtime-mutable values. Currently used only for refresh token writeback.

|Column     |Type    |Notes                         |
|-----------|--------|------------------------------|
|`Key`      |TEXT PK |e.g. `"spotify:refresh_token"`|
|`Value`    |TEXT    |                              |
|`UpdatedAt`|DATETIME|                              |

-----

## Caching Strategy

Because the service is a short-lived CLI process, no distributed cache is needed. The service uses `IHybridCache` registered with only the default in-memory backend — no Valkey or Redis sidecar. This gives the cleaner `GetOrCreateAsync` API and built-in stampede protection, while behaving identically to `IMemoryCache` at runtime.

Registration:

```csharp
builder.Services.AddHybridCache(); // L1 in-memory only — no distributed backend added
```

If a distributed backend is ever needed later, adding `AddStackExchangeRedisCache()` is sufficient — no call sites change.

|What                       |Cache Key             |Scope  |Notes                                                                 |
|---------------------------|----------------------|-------|----------------------------------------------------------------------|
|Track history existence set|`track-history:exists`|Per run|Loaded once as a `HashSet<string>` before the album loop; O(1) lookups|
|Liked tracks set           |`liked-tracks`        |Per run|Fetched once; used for both removal detection and skip decisions      |
|Queue playlist contents    |`queue-playlist`      |Per run|Snapshot taken before diffing; not refreshed mid-run                  |

Usage pattern throughout the codebase:

```csharp
// Clean factory-style API — no TryGetValue/Set ceremony
var likedTrackIds = await _cache.GetOrCreateAsync(
    "liked-tracks",
    async ct => new HashSet<string>(await _musicProvider.GetLikedTrackIdsAsync(ct)),
    cancellationToken: ct
);
```

-----

## Job Logic

Each run executes the following steps **in order**:

### Step 1 — Snapshot & Diff (Liked + Manual Removal)

Combining liked-track removal and manual-removal detection into a single step avoids a race condition where tracks removed in the liked pass are misidentified as manually removed.

1. Fetch the current contents of the queue playlist → `currentPlaylistTracks`.
1. Fetch the user’s liked tracks → `likedTracks` (cached in-memory for this run).
1. Load all `TrackHistory` records with no `RemovedAt` → `activeHistory`.
1. Identify **liked removals**: `currentPlaylistTracks ∩ likedTracks`.
1. Remove liked tracks from the Spotify playlist (batched).
1. Mark them in `TrackHistory` with `RemovalReason = "liked"`.
1. Identify **manual removals**: tracks in `activeHistory` that are absent from `currentPlaylistTracks` **and** not in the liked-removal set from step 4.
1. Mark them in `TrackHistory` with `RemovalReason = "manual"`.

By computing the liked-removal set before querying for manual removals, the two passes never conflict.

### Step 2 — Add New Tracks from Saved Albums

1. Fetch all saved albums. For each album, skip fetching its track list if it appears in `ProcessedAlbums` (see Data Model) — only new or unprocessed albums require a track-list API call.
1. For each track in unprocessed albums, skip it if:
- It is in `likedTracks` (cached from Step 1), **or**
- It appears in `TrackHistory` (cached in-memory existence set).
1. Add remaining tracks to the queue playlist in batches.
1. Record each newly added track in `TrackHistory`.
1. Record all fetched albums in `ProcessedAlbums`.

### Step 3 — Persist Updated Spotify Refresh Token

Spotify may rotate the refresh token on each access token renewal. Token persistence is **immediate and isolated**: the moment `SpotifyTokenHandler` receives a new refresh token from `/api/token`, it writes it to the `AppSettings` table in a dedicated short-lived `DbContext` transaction — independent of the main job unit of work. This ensures the token is safe even if the process exits mid-run. The token is never stored in `.env` — it lives exclusively in the database after first auth.

### Step 4 — Generate and Send Report *(optional but included)*

See report spec below.

-----

## Idempotency

If the job is triggered more than once on the same day (e.g. due to a container restart or a future manual trigger), it is safe to re-run. Steps 1 and 2 are naturally idempotent — removing already-removed tracks or marking already-marked history rows is a no-op. Step 3 skips any track already present in `TrackHistory`, so no duplicates will be added. Each run always creates a new `JobRun` row.

-----

## Spotify API Considerations

Spotify imposes rate limits (HTTP 429) and endpoint-level size limits. The service will handle these as follows:

|Concern                    |Approach                                                                          |
|---------------------------|----------------------------------------------------------------------------------|
|Playlist track fetch       |Paginate using `offset` + `limit=50` (API max)                                    |
|Saved albums fetch         |Paginate using `offset` + `limit=50`                                              |
|Liked tracks check         |Batch check using `/me/tracks/contains` (max 50 IDs per request)                  |
|Add tracks to playlist     |Batch using `/playlists/{id}/tracks` (max 100 URIs per request)                   |
|Remove tracks from playlist|Batch using same endpoint with DELETE (max 100 per request)                       |
|Rate limit (429)           |Honour `Retry-After` header; exponential backoff with configurable max retries    |
|General resilience         |[Polly](https://github.com/App-vNext/Polly) for retry and circuit-breaker policies|

-----

## Abstractions

The following interfaces decouple the core logic from any specific provider, making the service testable and replaceable:

```
IMusicProvider
  GetSavedAlbumsAsync()
  GetAlbumTracksAsync(albumId)
  GetPlaylistTracksAsync(playlistId)
  GetLikedTracksAsync()
  AddTracksToPlaylistAsync(playlistId, trackIds)
  RemoveTracksFromPlaylistAsync(playlistId, trackIds)

ITrackHistoryRepository
  GetAllHistoryAsync()
  AddTrackHistoryAsync(entries)
  MarkRemovedAsync(trackId, reason, removedAt)
  ExistsAsync(trackId)


IJobRunRepository
  CreateAsync(jobRun)
  UpdateAsync(jobRun)
  GetLatestAsync()

IAppSettingsRepository
  GetAsync(key)
  SetAsync(key, value)

ISpotifyAuthenticator
  // Checks AppSettings for a stored refresh token.
  // If absent, runs the browser-based PKCE flow and persists the result.
  // Called once by SpotifyTokenHandler before the first API request.
  EnsureAuthenticatedAsync(CancellationToken ct)
```

`SpotifyMusicProvider` implements `IMusicProvider`. Future providers (e.g. a mock, or Tidal) can be swapped in without touching job logic.

The job is driven by `System.CommandLine` — no `BackgroundService`, no scheduler. Future subcommands (e.g. `stats`, `reset`) can be added to the root command without restructuring.

-----

## Run Summary

At the end of each run, a structured summary is logged to stdout at `Information` level. All values are sourced from the `JobRun` row, updated incrementally throughout the run:

```
=== Spotify Queue Sync Complete ===
Duration:          00:01:23
Status:            Succeeded
Tracks added:      42
Tracks removed:    6  (4 liked, 2 manual)
Tracks skipped:    318
New albums seen:   3
Queue size:        104
===================================
```

If the run fails, the exception is logged at `Error` level with the full stack trace before the summary is emitted with `Status: Failed`.

-----

## Out of Scope (for now)

- Web UI or dashboard
- Additional CLI subcommands (architecture is ready; `System.CommandLine` root command is already structured for extension)
- Multi-user support
- Handling podcasts or local files in Spotify
- Email reporting (can be added later via MailKit + Fastmail if desired)
- Remote deployment / CI/CD (can be added later; Docker image is self-contained)

-----

## Technical Implementation Notes

These notes prescribe specific patterns and conventions the coding agent must follow. They are not optional style preferences — they define the expected shape of the codebase.

-----

### Project Structure

```
SpotifyQueueManager/
├── SpotifyQueueManager.sln
├── src/
│   └── SpotifyQueueManager/
│       ├── SpotifyQueueManager.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Options/                # Strongly-typed options records (SpotifyOptions, …)
│       ├── Domain/                 # Pure models — Track, Album, JobRun, etc. No EF deps.
│       │   └── Interfaces/         # Interfaces that belong to the domain (IMusicProvider, ITrackHistoryRepository, …)
│       ├── Infrastructure/
│       │   ├── Spotify/            # SpotifyMusicProvider, SpotifyTokenHandler, SpotifyAuthenticator
│       │   └── Persistence/        # EF Core DbContext, entity configs, migrations, repositories
│       └── Jobs/                   # JobOrchestrator and sync step classes
└── tests/
    └── SpotifyQueueManager.Tests/
        ├── SpotifyQueueManager.Tests.csproj
        ├── Fakes/                  # LocalMockMusicProvider and other test fakes
        └── Jobs/                   # Unit tests for JobOrchestrator and step classes
```

The solution and projects must be created using the `dotnet` CLI — never by generating `.csproj` or `.sln` files by hand:

```bash
# Create solution
dotnet new sln -n SpotifyQueueManager

# Create projects
dotnet new console -n SpotifyQueueManager -o src/SpotifyQueueManager --framework net10.0
dotnet new xunit -n SpotifyQueueManager.Tests -o tests/SpotifyQueueManager.Tests --framework net10.0

# Wire up solution
dotnet sln add src/SpotifyQueueManager/SpotifyQueueManager.csproj
dotnet sln add tests/SpotifyQueueManager.Tests/SpotifyQueueManager.Tests.csproj

# Add project reference from tests to src
dotnet add tests/SpotifyQueueManager.Tests/SpotifyQueueManager.Tests.csproj reference src/SpotifyQueueManager/SpotifyQueueManager.csproj

# Add NuGet packages (examples — full list to be determined during implementation)
dotnet add src/SpotifyQueueManager package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/SpotifyQueueManager package System.CommandLine
```

All package additions must go through `dotnet add package`, not by editing `.csproj` directly.

Key structural decisions:

- **`.csproj` files are explicit** — both projects must appear in the solution file and be referenced correctly.
- **`Options/` not `Configuration/`** — avoids confusion with `Microsoft.Extensions.Configuration` namespaces.
- **Interfaces live in `Domain/Interfaces/`** — they express what the domain needs from the outside world; putting them in a separate top-level `Abstractions/` folder is a common pattern in SDK packages but is unnecessarily distant from the models they relate to in an application.
- **`Mock/` moved to `tests/Fakes/`** — test fakes do not belong in the production project. `LocalMockMusicProvider` is registered only in tests; shipping it in the main binary is wrong.
- **`Caching/` removed** — `IHybridCache` is used directly; there is nothing meaningful to put in a dedicated caching folder beyond a static class of key constants, which can live in `Jobs/` alongside the code that uses it.
- **`Infrastructure/` contains no interfaces** — interfaces are in `Domain/Interfaces/`; `Infrastructure/` contains only concrete implementations.
- **`Jobs/` not `Job/`** — plural is the .NET convention (matching `Controllers/`, `Services/`, `Models/`).

-----

### Options Pattern

Every configuration section binds to a strongly-typed record using `services.Configure<T>()`. All options classes must use `ValidateDataAnnotations()` and `ValidateOnStart()` so missing or invalid configuration fails fast at startup rather than silently mid-run.

```csharp
// Registration
builder.Services
    .AddOptions<SpotifyOptions>()
    .BindConfiguration("Spotify")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Options record
public sealed record SpotifyOptions
{
    [Required] public string ClientId { get; init; } = "";
    [Required] public string ClientSecret { get; init; } = "";
    [Required] public string QueuePlaylistId { get; init; } = "";
    [Range(0, 5000)] public int RequestDelayMs { get; init; } = 100;
    [Range(1, 10)]   public int MaxRetries { get; init; } = 3;
}
```

Inject options into services as `IOptions<T>` (for singleton/transient values that don’t change at runtime). Do not inject raw `IConfiguration` anywhere below `Program.cs`.

-----

### Multiple `IMusicProvider` Implementations

Two implementations are required from the start:

#### `SpotifyMusicProvider`

The production implementation. Registered when `ASPNETCORE_ENVIRONMENT != "Development"` or via a feature flag.

#### `LocalMockMusicProvider`

A fully in-memory (or SQLite-backed) implementation of `IMusicProvider` that returns seeded data. This enables the job to be unit-tested without any network calls or Spotify credentials.

```csharp
public sealed class LocalMockMusicProvider : IMusicProvider
{
    private readonly List<Album> _savedAlbums;
    private readonly HashSet<string> _likedTrackIds;
    private readonly List<Track> _playlistTracks;

    // Constructor accepts seed data — injectable via test setup
}
```

Registration is driven by the `Provider` config key (see Configuration section). Setting `Provider=Mock` in `.env` or passing `--Provider Mock` as a CLI arg loads `LocalMockMusicProvider` with no credentials required. The switch is explicit and throws on unrecognised values — no silent fallback.

`LocalMockMusicProvider` lives in `tests/SpotifyQueueManager.Tests/Fakes/` — not in the production project. When `Provider=Mock` is needed outside of tests (e.g. a quick local run without credentials), the test project can be referenced conditionally, or a minimal hardcoded seed can be inlined.

-----

### Authentication Flow — Browser-Based PKCE

On the very first run (no refresh token in `AppSettings`), `ISpotifyAuthenticator` performs the OAuth 2.0 PKCE flow automatically:

1. Generate a cryptographically random `code_verifier` and derive the `code_challenge`.
1. Start an `HttpListener` on `http://localhost:5000/callback`.
1. Open the Spotify authorisation URL in the default browser via `Process.Start`.
1. Await the redirect asynchronously — the listener captures the `?code=` query parameter.
1. Exchange the code for an access token and refresh token via `/api/token`.
1. Persist the refresh token immediately to `AppSettings` and close the listener.

From the second run onwards, the stored refresh token is used directly — no browser interaction occurs. The listener is never started unless the token is missing.

> **PKCE over Authorization Code:** PKCE is the recommended flow for CLI tools. It does not require the client secret to be sent during the token exchange, only the `code_verifier`. The client secret remains in `.env` solely for any endpoints that require it (none currently).

-----

### HTTP Client & Auth — `DelegatingHandler`

Spotify token management must not leak into `SpotifyMusicProvider`. Use a `DelegatingHandler` registered on the typed `HttpClient`:

```csharp
public sealed class SpotifyTokenHandler : DelegatingHandler
{
    // On each request:
    //   1. Load access token from memory (cached for its lifetime, typically 1 hour).
    //   2. If no access token, use the stored refresh token to obtain one from /api/token.
    //   3. If no refresh token exists in AppSettings (first run), trigger browser auth (see ISpotifyAuthenticator).
    //   4. On 401 response, force-refresh the access token and retry once.
    //   5. Whenever a new refresh token is received, persist it immediately via IAppSettingsRepository.
    //   Token refresh serialised with SemaphoreSlim(1,1) to prevent concurrent refresh races.
}
```

Registration:

```csharp
builder.Services
    .AddHttpClient<IMusicProvider, SpotifyMusicProvider>()
    .AddHttpMessageHandler<SpotifyTokenHandler>();
```

`SpotifyMusicProvider` then only concerns itself with building requests and parsing responses — never with tokens.

-----

### Resilience — Polly via `Microsoft.Extensions.Http.Resilience`

Use the `AddStandardResilienceHandler()` extension (ships with .NET 8+ via `Microsoft.Extensions.Http.Resilience`) rather than hand-rolling Polly pipelines. Override the defaults to honour Spotify’s `Retry-After` header:

```csharp
builder.Services
    .AddHttpClient<IMusicProvider, SpotifyMusicProvider>()
    .AddHttpMessageHandler<SpotifyTokenHandler>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = spotifyOptions.MaxRetries;
        options.Retry.DelayGenerator = args =>
        {
            if (args.Outcome.Result?.Headers.RetryAfter?.Delta is { } delta)
                return ValueTask.FromResult<TimeSpan?>(delta);
            return ValueTask.FromResult<TimeSpan?>(null); // fall back to default backoff
        };
    });
```

-----

### Async & Concurrency

#### `IAsyncEnumerable<T>` for Pagination

All paginated Spotify endpoints must be exposed as `IAsyncEnumerable<T>` from `IMusicProvider`. This streams pages lazily rather than loading the entire library into memory before processing begins:

```csharp
// Interface
IAsyncEnumerable<Album> GetSavedAlbumsAsync(CancellationToken ct);

// Caller — processes each album as pages arrive
await foreach (var album in _musicProvider.GetSavedAlbumsAsync(ct))
{
    await ProcessAlbumAsync(album, context, ct);
}
```

#### Controlled Parallelism with `SemaphoreSlim`

Album track-list fetches (Step 2) can be parallelised across multiple albums, but must be throttled to avoid saturating the Spotify rate limit. Use `SemaphoreSlim` with a configurable degree of parallelism:

```csharp
var semaphore = new SemaphoreSlim(SpotifyOptions.MaxConcurrentRequests); // default: 3

var tasks = newAlbums.Select(async album =>
{
    await semaphore.WaitAsync(ct);
    try   { await FetchAndProcessAlbumAsync(album, ct); }
    finally { semaphore.Release(); }
});

await Task.WhenAll(tasks);
```

#### `CancellationToken` Propagation

Every `async` method in the codebase — from `JobOrchestrator.RunAsync` down to individual repository calls — must accept and forward a `CancellationToken`. `System.CommandLine` provides the root token, wired to `Ctrl+C` and Docker’s `SIGTERM`, so the entire call tree can be cancelled cleanly.

-----

### `HashSet<string>` for O(1) Membership Checks

The two hottest lookups during the album processing loop are “is this track liked?” and “has this track been seen before?”. Both must be loaded into `HashSet<string>` at the start of the run and consulted in-memory — never queried per-track against SQLite or the Spotify API:

```csharp
// Loaded via IHybridCache — deduplicated even if multiple steps request the same key
var likedTrackIds   = await _cache.GetOrCreateAsync("liked-tracks",   async ct => new HashSet<string>(...), ct);
var historyTrackIds = await _cache.GetOrCreateAsync("track-history:exists", async ct => new HashSet<string>(...), ct);

// O(1) per track during inner loop
if (likedTrackIds.Contains(track.Id) || historyTrackIds.Contains(track.Id))
    continue;
```

Both sets are populated via `IHybridCache` so that if multiple steps call the same key, the factory runs exactly once — stampede protection is built in.

-----

### CLI Entry Point — `System.CommandLine`

The tool uses `System.CommandLine` (Microsoft’s official CLI library) as its entry point. This replaces `BackgroundService` entirely — there is no scheduler or long-running loop. The process starts, runs, and exits.

The default (and currently only) command is `sync`. The structure is designed so additional subcommands can be added later without refactoring `Program.cs`:

```csharp
var rootCommand = new RootCommand("Spotify Queue Manager");

var syncCommand = new Command("sync", "Sync saved albums to the queue playlist");
syncCommand.SetHandler(async (context) =>
{
    var ct = context.GetCancellationToken(); // honours Ctrl+C / SIGTERM
    await using var scope = host.Services.CreateAsyncScope();
    var orchestrator = scope.ServiceProvider.GetRequiredService<JobOrchestrator>();
    await orchestrator.RunAsync(ct);
});

rootCommand.AddCommand(syncCommand);
rootCommand.SetHandler(() => syncCommand.InvokeAsync([])); // dotnet run → runs sync by default

return await rootCommand.InvokeAsync(args);
```

`System.CommandLine` wires up `CancellationToken` automatically — `Ctrl+C` or a Docker `SIGTERM` signal cancels the token, giving the orchestrator a clean shutdown path.

`JobOrchestrator` creates and persists the `JobRun` record, calls each step in order, handles top-level exceptions, logs the run summary, and updates the final `JobRun` status. It is a scoped service resolved fresh per invocation.

-----

### Structured Logging Conventions

Use `LoggerMessage.Define` source generators (or the `[LoggerMessage]` attribute) rather than `_logger.LogInformation("...")` string interpolation. This avoids boxing allocations and enables structured log analysis:

```csharp
public static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Adding {TrackCount} tracks to playlist {PlaylistId}")]
    public static partial void AddingTracks(ILogger logger, int trackCount, string playlistId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limited by Spotify. Retrying after {Delay}")]
    public static partial void RateLimited(ILogger logger, TimeSpan delay);
}
```

Every log entry for a job run must include the `JobRunId` as a structured property, pushed onto the logging scope at the start of `JobOrchestrator.RunAsync`:

```csharp
using (_logger.BeginScope(new { JobRunId = jobRun.Id }))
{
    // all logs within this block carry JobRunId automatically
}
```

-----

### Testing Strategy

Unit tests target `JobOrchestrator` and each step class in isolation, with `LocalMockMusicProvider` (in `tests/Fakes/`) seeded per test and a real SQLite in-memory connection string (preferred over `UseInMemoryDatabase`, which doesn’t enforce relational constraints).

No test should make a real HTTP call. The `IMusicProvider` boundary enforces this — if a test can compile without mocking it, the abstraction is in the wrong place.

```csharp
// Example test setup
var provider = new LocalMockMusicProvider(
    savedAlbums: [TestData.AlbumA, TestData.AlbumB],
    likedTracks: [TestData.TrackA1],
    playlistTracks: []
);

var orchestrator = BuildOrchestrator(provider, inMemoryDb);
await orchestrator.RunAsync(CancellationToken.None);

// Assert TrackA1 was not added, TrackA2 was added, etc.
```