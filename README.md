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
# Direct
dotnet run --project src/Musync

# Docker
docker compose up
```

On first run, your browser opens for Spotify OAuth. After authorising, the tool proceeds with the sync.

## Commands

### `sync` (default)

Syncs your saved Spotify albums into the configured queue playlist.

```bash
dotnet run --project src/Musync sync
```

Runs four steps: snapshot & diff, add new tracks, (transparent token refresh), report.

### `import-tidal`

Imports a Tidal playlist into your Spotify queue playlist.

```bash
dotnet run --project src/Musync import-tidal <playlist-url>
```

Example:
```bash
dotnet run --project src/Musync import-tidal https://tidal.com/browse/playlist/abc123
```

This command:
1. Authenticates with Tidal (PKCE OAuth, browser opens on first run).
2. Fetches all tracks from the Tidal playlist.
3. Searches for each track in Spotify's catalog (by ISRC, then by name+artist).
4. Adds matched tracks to the queue playlist.
5. Generates a report of imported, skipped, and unmatched tracks.

## Authentication

First run uses PKCE OAuth: a browser window opens, you authorise the app, and the refresh token is stored in the database. Subsequent runs use the stored token silently. If a provider rotates the refresh token, it's persisted immediately to the database.

## How It Works

### Sync command

Each sync run executes four steps:

1. **Snapshot & diff** — fetches the current playlist and your liked tracks. Liked tracks in the queue are removed; tracks removed manually from the queue since the last run are marked as such in history.
2. **Add new tracks** — iterates saved albums, skips already-processed ones, fetches track lists for new albums. Tracks that are liked or have been seen before are skipped; the rest are added to the queue.
3. *(transparent)* — Spotify token refresh happens automatically in the HTTP pipeline.
4. **Report** — logs a summary to stdout.

### Import-tidal command

Runs three steps:

1. **Fetch & map** — authenticates with Tidal, fetches the playlist, searches each track in Spotify's catalog via `SpotifySearchMapper`.
2. **Add to queue** — adds matched tracks to the queue playlist using the same dedup logic as sync.
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

### Spotify (`Spotify__*`)

| Key | Default | Description |
|-----|---------|-------------|
| `ClientId` | — | Spotify app client ID |
| `ClientSecret` | — | Spotify app client secret |
| `QueuePlaylistId` | — | Target playlist ID |
| `AuthUrl` | `https://accounts.spotify.com/authorize` | OAuth authorisation endpoint |
| `TokenUrl` | `https://accounts.spotify.com/api/token` | OAuth token endpoint |
| `Scopes` | `user-library-read playlist-modify-public ...` | OAuth scopes |
| `RequestDelayMs` | `100` | Delay between API requests |
| `MaxRetries` | `3` | HTTP retry count |
| `MaxConcurrentRequests` | `3` | Parallel album track fetches |

### Tidal (`Tidal__*`)

| Key | Default | Description |
|-----|---------|-------------|
| `ClientId` | — | Tidal app client ID |
| `ClientSecret` | — | Tidal app client secret |
| `AuthUrl` | `https://login.tidal.com/authorize` | OAuth authorisation endpoint |
| `TokenUrl` | `https://auth.tidal.com/v1/oauth2/token` | OAuth token endpoint |
| `Scopes` | `user.read user_collection.read` | OAuth scopes |
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
│   │   ├── Mapping/          # SpotifySearchMapper (ISRC + by-name fallback)
│   │   ├── Spotify/          # Spotify API client, auth, token handler, models
│   │   ├── Tidal/            # Tidal API client, auth, token handler, models
│   │   └── Persistence/      # EF Core AppDbContext, migrations, repos
│   ├── Jobs/                 # Orchestrators + step classes (sync, import-tidal)
│   └── Migrations/           # EF Core migrations
└── tests/Musync.Tests/
    ├── Fakes/                # LocalMockMusicProvider, LocalMockTrackMapper
    └── Jobs/                 # Unit tests (sync + import-tidal)
```

## Testing

```bash
dotnet test
```

Tests use a real SQLite in-memory database (separate from the production PostgreSQL setup) and `LocalMockMusicProvider` — no HTTP calls, no credentials required.

## Tech Stack

- .NET 10 / C#
- System.CommandLine (v2)
- Entity Framework Core + PostgreSQL
- Npgsql.EntityFrameworkCore.PostgreSQL
- Microsoft.Extensions.Http.Resilience (Polly)
- IHybridCache (in-process)
- xunit
