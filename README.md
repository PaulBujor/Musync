# Musync

A CLI tool that syncs your saved Spotify albums into a queue playlist, and can import playlists from other music services.

Run it on demand — albums you save funnel into the queue; tracks you like or manually remove stay out going forward.

## Features

- **One-shot sync** — runs, logs a summary, exits. No scheduler, no daemon.
- **Liked-track exclusion** — tracks you liked on Spotify are removed from the queue and excluded from future runs.
- **Manual-removal tracking** — remove a track from the playlist manually and it won't be re-added.
- **Idempotent** — re-running is safe. Already-processed albums are skipped; already-seen tracks are never duplicated.
- **Cross-platform import** — import playlists from other providers (e.g. Tidal) into your Spotify queue.
- **Docker or bare-metal** — works with `dotnet run` or `docker compose up`.

## Prerequisites

1. A [Spotify Developer App](https://developer.spotify.com/dashboard) with scopes `user-library-read`, `playlist-modify-public` (or `playlist-modify-private`), `playlist-read-private`, `playlist-read-collaborative`.
2. A Spotify playlist to use as the queue (grab its ID from the share link — it's the string after `playlist/`).
3. (Optional) A [Tidal Developer App](https://developer.tidal.com/) if importing Tidal playlists.

Redirect URIs are configured per-provider in `appsettings.json` (default: `http://127.0.0.1:5000/callback`).

## Quick Start

### 1. Configure

```bash
cp .env.example .env
```

Fill in your credentials:

```env
Spotify__ClientId=your-client-id
Spotify__ClientSecret=your-client-secret
Spotify__QueuePlaylistId=your-playlist-id
```

### 2. Run

```bash
# Direct (pass the command after --)
dotnet run --project src/Musync -- spotify queue-albums

# Docker
docker compose up
```

On first run, your browser opens for OAuth. After authorising, the tool proceeds with the sync.

## Commands

The CLI is organised by provider. Each provider supports `queue-albums` (sync saved albums to its queue playlist) and `import` (pull tracks from another provider).

Global options (available on any command):

| Option | Description |
|--------|-------------|
| `--dry-run` | Preview changes without mutating any provider |
| `--limit <n>` | Cap the number of items processed |

### `spotify queue-albums`

Syncs your saved Spotify albums into the configured queue playlist.

```bash
dotnet run --project src/Musync -- spotify queue-albums
```

Runs three steps: snapshot & diff, add new tracks, report. (Token refresh happens transparently in the HTTP pipeline.)

> Tidal is an import **source** only — `tidal queue-albums` and imports *into* Tidal are not supported.

### `<target> import --source <source>`

Imports tracks from a source provider into the target provider's queue playlist. Source and target must differ, and the target must be Spotify.

```bash
# Import your Tidal collection into the Spotify queue
dotnet run --project src/Musync -- spotify import --source tidal
```

This command:
1. Authenticates with the source provider (PKCE OAuth, browser opens on first run).
2. Fetches tracks from the source.
3. Searches for each track in the target's catalog (by ISRC, then by name+artist).
4. Adds matched tracks to the target's queue playlist.
5. Generates a report of imported, skipped, and unmatched tracks.

### `spotify reconcile-queue`

Removes duplicate tracks from the Spotify queue playlist (keeping one copy of each) and backfills
the track-history ledger so future syncs stay consistent. Use `--dry-run` to preview.

```bash
dotnet run --project src/Musync -- spotify reconcile-queue --dry-run
dotnet run --project src/Musync -- spotify reconcile-queue
```

### Deprecated aliases

`sync` (→ `spotify queue-albums`) and `import-tidal` (→ `spotify import --source tidal`) still work but log a deprecation warning and exit with code `3`.

### Exit codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | Error or misconfiguration |
| `2` | Cancelled (Ctrl+C / SIGTERM) |
| `3` | A deprecated alias succeeded |

## Authentication

First run uses PKCE OAuth: a browser window opens, you authorise the app, and the refresh token is stored in the database. Subsequent runs use the stored token silently. If a provider rotates the refresh token, it's persisted immediately to the database.

## How It Works

### `queue-albums` command

Each run executes three steps (token refresh happens transparently in the HTTP pipeline):

1. **Snapshot & diff** — fetches the current playlist and your liked tracks. Liked tracks in the queue are removed; tracks removed manually from the queue since the last run are marked as such in history.
2. **Add new tracks** — iterates saved albums, skips already-processed ones, fetches track lists for new albums. Tracks that are liked or have been seen before are skipped; the rest are added to the queue.
3. **Report** — logs a summary to stdout.

### `import` command

Runs three steps:

1. **Fetch & map** — authenticates with the source provider, fetches its tracks, and searches each in the target provider's catalog via the target's `ITrackMapper` (e.g. `SpotifySearchMapper`).
2. **Add to queue** — adds matched tracks to the target's queue playlist using the same dedup logic as `queue-albums`.
3. **Generate report** — summarises imported, skipped, and unmatched tracks.

### Example Output

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

## Configuration

Settings come from `appsettings.json` (checked in, safe defaults) overridden by environment variables (secrets at runtime via `.env` or `docker compose`).

### Database (`Database__*`)

| Key | Default | Description |
|-----|---------|-------------|
| `Provider` | `Sqlite` | Database provider: `Sqlite` or `Postgres` |

Connection strings are provider-specific:
- `ConnectionStrings__Sqlite` — SQLite file path (e.g. `Data Source=/path/to/musync.db`)
- `ConnectionStrings__Postgres` — PostgreSQL connection string

For portable use, set `ConnectionStrings__Sqlite` to a path inside your Google Drive folder.

### Spotify (`Spotify__*`)

| Key | Default | Description |
|-----|---------|-------------|
| `ClientId` | — | Spotify app client ID |
| `ClientSecret` | — | Spotify app client secret |
| `QueuePlaylistId` | — | Target playlist ID |
| `RedirectUri` | `http://127.0.0.1:5000/callback` | OAuth redirect URI |
| `ApiBaseUrl` | `https://api.spotify.com/v1/` | Spotify API base URL |
| `AuthUrl` | `https://accounts.spotify.com/authorize` | OAuth authorisation endpoint |
| `TokenUrl` | `https://accounts.spotify.com/api/token` | OAuth token endpoint |
| `Scopes` | `user-library-read playlist-modify-public ...` | OAuth scopes |
| `MaxRetries` | `3` | HTTP retry count for reads (playlist writes are never retried) |
| `MaxConcurrentRequests` | `3` | Parallel album track fetches |

### Tidal (`Tidal__*`)

Tidal is supported as an **import source only** (`spotify import --source tidal`). It cannot be a
queue-albums or import *target*.

| Key | Default | Description |
|-----|---------|-------------|
| `ClientId` | — | Tidal app client ID |
| `ClientSecret` | — | Tidal app client secret |
| `RedirectUri` | `http://127.0.0.1:5000/callback` | OAuth redirect URI |
| `ApiBaseUrl` | `https://openapi.tidal.com/v2` | Tidal v2 API base URL. Leave empty to disable Tidal entirely |
| `AuthUrl` | `https://login.tidal.com/authorize` | OAuth authorisation endpoint |
| `TokenUrl` | `https://auth.tidal.com/v1/oauth2/token` | OAuth token endpoint |
| `Scopes` | `collection.read` | OAuth scopes |
| `MaxRetries` | `3` | HTTP retry count |

## Project Structure

```
Musync.slnx
├── src/Musync/
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Options/              # Strongly-typed options records (ProviderOptionsBase, SpotifyOptions, TidalOptions)
│   ├── Domain/               # Models + interfaces
│   │   ├── Album.cs, Track.cs, JobRun.cs, RefreshToken.cs
│   │   └── Interfaces/       # IMusicProvider, IAuthenticator, ITrackMapper, etc.
│   ├── Infrastructure/
│   │   ├── Auth/             # Shared PKCE authenticator & token handler bases
│   │   ├── Spotify/          # Spotify client, auth, token handler, models, SpotifySearchMapper
│   │   ├── Tidal/            # Tidal client, auth, token handler, models (import source only)
│   │   └── Persistence/      # EF Core AppDbContext
│   ├── Jobs/                 # QueueAlbumsOrchestrator, ImportOrchestrator + numbered step classes
│   └── Migrations/           # EF Core migrations
└── tests/Musync.Tests/
    ├── Fakes/                # LocalMockMusicProvider, LocalMockTrackMapper
    └── Jobs/                 # Unit tests (queue-albums + import orchestrators)
```

## Testing

```bash
dotnet test
```

Tests use a real SQLite in-memory database and `LocalMockMusicProvider` — no HTTP calls, no credentials required.

## Tech Stack

- .NET 10 / C#
- System.CommandLine (v2)
- Entity Framework Core + SQLite / PostgreSQL
- Npgsql.EntityFrameworkCore.PostgreSQL
- Microsoft.Extensions.Http.Resilience (Polly)
- IHybridCache (in-process)
- xunit
