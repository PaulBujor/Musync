# SpotifyTools

A CLI tool that syncs your saved Spotify albums into a queue playlist. Run it on demand — albums you save funnel into the queue; tracks you like or manually remove stay out going forward.

## Features

- **One-shot sync** — runs, logs a summary, exits. No scheduler, no daemon.
- **Liked-track exclusion** — tracks you liked on Spotify are removed from the queue and excluded from future runs.
- **Manual-removal tracking** — remove a track from the playlist manually and it won't be re-added.
- **Idempotent** — re-running is safe. Already-processed albums are skipped; already-seen tracks are never duplicated.
- **Docker or bare-metal** — works with `dotnet run` or `docker compose up`.

## Prerequisites

1. A [Spotify Developer App](https://developer.spotify.com/dashboard) with Redirect URI `http://localhost:5000/callback` and scopes `user-library-read`, `playlist-modify-public` (or `playlist-modify-private`), `playlist-read-private`, `playlist-read-collaborative`.
2. A Spotify playlist to use as the queue (grab its ID from the share link — it's the string after `playlist/`).

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
dotnet run --project src/SpotifyTools

# Docker
docker compose up
```

On first run, your browser opens for Spotify OAuth. After authorising, the tool proceeds with the sync.

## Authentication

First run uses PKCE OAuth: a browser window opens, you authorise the app, and the refresh token is stored in the SQLite database. Subsequent runs use the stored token silently. If Spotify rotates the refresh token, it's persisted immediately to the database.

## How It Works

Each sync run executes four steps:

1. **Snapshot & diff** — fetches the current playlist and your liked tracks. Liked tracks in the queue are removed; tracks removed manually from the queue since the last run are marked as such in history.
2. **Add new tracks** — iterates saved albums, skips already-processed ones, fetches track lists for new albums. Tracks that are liked or have been seen before are skipped; the rest are added to the queue.
3. *(transparent)* — Spotify token refresh happens automatically in the HTTP pipeline.
4. **Report** — logs a summary to stdout.

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

| Key | Default | Description |
|-----|---------|-------------|
| `Provider` | `Spotify` | `Spotify` or `Mock` |
| `Spotify__ClientId` | — | Spotify app client ID |
| `Spotify__ClientSecret` | — | Spotify app client secret |
| `Spotify__QueuePlaylistId` | — | Target playlist ID |
| `Spotify__MaxConcurrentRequests` | `3` | Parallel album track fetches |

Set `Provider=Mock` to test without Spotify credentials (uses in-memory seed data — only available via the test project).

## Project Structure

```
SpotifyTools.sln
├── src/SpotifyTools/
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Options/              # Strongly-typed options records
│   ├── Domain/               # Models + interfaces
│   │   └── Interfaces/
│   ├── Infrastructure/
│   │   ├── Spotify/          # API client, auth, token handler
│   │   └── Persistence/      # EF Core DbContext, migrations, repos
│   └── Jobs/                 # Orchestrator + sync step classes
└── tests/SpotifyTools.Tests/
    ├── Fakes/                # LocalMockMusicProvider
    └── Jobs/                 # Unit tests
```

## Testing

```bash
dotnet test
```

Tests use a real SQLite in-memory database (not `UseInMemoryDatabase`) and `LocalMockMusicProvider` — no HTTP calls, no credentials required.

## Tech Stack

- .NET 10 / C#
- System.CommandLine (v2)
- Entity Framework Core + SQLite (WAL mode)
- Microsoft.Extensions.Http.Resilience (Polly)
- IHybridCache (in-process)
- xunit
