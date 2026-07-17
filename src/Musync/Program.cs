using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Persistence;
using Musync.Infrastructure.Spotify;
using Musync.Infrastructure.Tidal;
using Musync.Jobs;
using Musync.Options;

var builder = Host.CreateApplicationBuilder(args);

// Validated lazily (on first use), not at host build, so a command targeting one provider doesn't
// fail because another is unconfigured. The run helpers report validation failures as exit code 1.
builder.Services
    .AddOptions<SpotifyOptions>()
    .BindConfiguration("Spotify")
    .ValidateDataAnnotations();

builder.Services
    .AddOptions<TidalOptions>()
    .BindConfiguration("Tidal")
    .ValidateDataAnnotations();

builder.Services.AddHybridCache();

// Database provider selection (allowed values are enforced explicitly below)
builder.Services
    .AddOptions<DatabaseOptions>()
    .BindConfiguration("Database")
    .ValidateDataAnnotations();

var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";

// ValidateOnStart never fires (the host is never started), so check the provider here to fail
// with a clean message instead of an unhandled throw when the DbContext is first resolved.
if (!DatabaseOptions.Allowed.Contains(dbProvider))
{
    await Console.Error.WriteLineAsync(
        $"Invalid Database:Provider '{dbProvider}'. Valid values: {string.Join(", ", DatabaseOptions.Allowed)}.");
    return 1;
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    switch (dbProvider)
    {
        case "Postgres":
            options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")!);
            break;
        case "Sqlite":
            options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite")!);
            break;
        default:
            throw new InvalidOperationException($"Unsupported database provider: {dbProvider}");
    }
});

// Encrypt refresh tokens at rest (see AppDbContext). Keys live under the local user profile,
// separate from the database, so copying the DB alone cannot decrypt the tokens.
var keyRingPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Musync", "keys");
Directory.CreateDirectory(keyRingPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .SetApplicationName("Musync");

// Step classes (providers come via RunContext, not DI)
builder.Services.AddScoped<SyncStep1_SnapshotAndDiff>();
builder.Services.AddScoped<SyncStep2_AddNewTracks>();
builder.Services.AddScoped<SyncStep3_GenerateReport>();
builder.Services.AddScoped<ImportStep1_FetchAndMap>();
builder.Services.AddScoped<ImportStep2_AddToQueue>();
builder.Services.AddScoped<ImportStep3_GenerateReport>();
builder.Services.AddScoped<ReconcileQueueJob>();

// Spotify auth + HTTP
var spotifyMaxRetries = builder.Configuration.GetValue("Spotify:MaxRetries", 3);
builder.Services.AddScoped<ISpotifyAuthenticator, SpotifyAuthenticator>();
builder.Services.AddTransient<SpotifyTokenHandler>();
builder.Services
    .AddHttpClient("spotify-music", (sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<SpotifyOptions>>().Value;
        client.BaseAddress = new Uri(opts.ApiBaseUrl);
    })
    .AddHttpMessageHandler<SpotifyTokenHandler>()
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        options.Retry.MaxRetryAttempts = spotifyMaxRetries;
        options.Retry.DelayGenerator = args =>
        {
            if (args.Outcome.Result?.Headers.RetryAfter?.Delta is { } delta)
                return ValueTask.FromResult<TimeSpan?>(delta);
            return ValueTask.FromResult<TimeSpan?>(null);
        };
    });

// Playlist add/remove are non-idempotent — a retried lost-but-committed POST duplicates tracks.
// A separate client keeps timeouts and the circuit breaker but never retries writes.
builder.Services
    .AddHttpClient("spotify-music-write", (sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<SpotifyOptions>>().Value;
        client.BaseAddress = new Uri(opts.ApiBaseUrl);
    })
    .AddHttpMessageHandler<SpotifyTokenHandler>()
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
    });

builder.Services.AddKeyedSingleton<IMusicProvider>("spotify", (sp, _) =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return new SpotifyMusicProvider(
        factory.CreateClient("spotify-music"),
        factory.CreateClient("spotify-music-write"));
});

// Tidal auth + HTTP (skip if ApiBaseUrl not configured)
var tidalConfig = new TidalOptions();
builder.Configuration.GetSection("Tidal").Bind(tidalConfig);

if (!string.IsNullOrEmpty(tidalConfig.ApiBaseUrl))
{
    builder.Services.AddScoped<ITidalAuthenticator, TidalAuthenticator>();
    builder.Services.AddTransient<TidalTokenHandler>();
    builder.Services
        .AddHttpClient("tidal-music", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<TidalOptions>>().Value;
            client.BaseAddress = new Uri(opts.ApiBaseUrl);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
        })
        .AddHttpMessageHandler<TidalTokenHandler>()
        .AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            options.Retry.MaxRetryAttempts = tidalConfig.MaxRetries;
        });
    builder.Services.AddKeyedSingleton<IMusicProvider>("tidal", (sp, _) =>
        new TidalMusicProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("tidal-music")));
}

// Track mapper — keyed by target provider
builder.Services
    .AddHttpClient("track-mapper", (sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<SpotifyOptions>>().Value;
        client.BaseAddress = new Uri(opts.ApiBaseUrl);
    })
    .AddHttpMessageHandler<SpotifyTokenHandler>()
        .AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            options.Retry.MaxRetryAttempts = spotifyMaxRetries;
        });
builder.Services.AddKeyedSingleton<ITrackMapper>("spotify", (sp, _) =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("track-mapper");
    var logger = sp.GetRequiredService<ILogger<SpotifySearchMapper>>();
    return new SpotifySearchMapper(httpClient, logger);
});
// Tidal is import-source only; there is no track mapper for importing *into* Tidal.

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var provider = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider;

    if (provider == "Sqlite")
    {
        // EnsureCreated skips schema changes on an existing file, so enforce the active-history
        // uniqueness invariant directly: one active row per track, backed by a unique index.
        db.Database.EnsureCreated();
        using var connection = db.Database.GetDbConnection();
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA journal_mode=WAL;

            DELETE FROM "TrackHistories"
            WHERE "RemovedAt" IS NULL
              AND "Id" NOT IN (
                  SELECT MIN("Id")
                  FROM "TrackHistories"
                  WHERE "RemovedAt" IS NULL
                  GROUP BY "Provider", "TrackId"
              );

            DROP INDEX IF EXISTS "IX_TrackHistories_Provider_TrackId";

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrackHistories_Provider_TrackId"
            ON "TrackHistories" ("Provider", "TrackId")
            WHERE "RemovedAt" IS NULL;
            """;
        cmd.ExecuteNonQuery();
    }
    else
    {
        await db.Database.MigrateAsync();
    }
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// ── Command tree ──────────────────────────────────────────────
var rootCommand = new RootCommand("Musync — multi-provider music queue manager");

var dryRunOption = new Option<bool>("--dry-run") { Recursive = true, Description = "Preview changes without mutating providers" };
var limitOption = new Option<int?>("--limit") { Recursive = true, Description = "Maximum number of items to process" };
rootCommand.Add(dryRunOption);
rootCommand.Add(limitOption);

var spotifyCmd = new Command("spotify", "Spotify operations");
var tidalCmd = new Command("tidal", "Tidal operations");
rootCommand.Add(spotifyCmd);
rootCommand.Add(tidalCmd);

var spotifyQueueAlbums = new Command("queue-albums", "Sync saved albums to the queue playlist");
spotifyCmd.Add(spotifyQueueAlbums);

var spotifyReconcile = new Command("reconcile-queue", "Remove duplicate tracks from the queue playlist");
spotifyCmd.Add(spotifyReconcile);

var tidalQueueAlbums = new Command("queue-albums", "Sync saved albums to the queue playlist");
tidalCmd.Add(tidalQueueAlbums);

var spotifySourceOption = new Option<string>("--source") { Description = "Source provider to import from" }
    .AcceptOnlyFromAmong("spotify", "tidal");
var spotifyImport = new Command("import", "Import tracks from another provider");
spotifyImport.Add(spotifySourceOption);
spotifyCmd.Add(spotifyImport);

var tidalSourceOption = new Option<string>("--source") { Description = "Source provider to import from" }
    .AcceptOnlyFromAmong("spotify", "tidal");
var tidalImport = new Command("import", "Import tracks from another provider");
tidalImport.Add(tidalSourceOption);
tidalCmd.Add(tidalImport);

// Deprecated aliases
var syncCommand = new Command("sync", "[Deprecated] Use 'spotify queue-albums' instead");
rootCommand.Add(syncCommand);

var importTidalCommand = new Command("import-tidal", "[Deprecated] Use 'spotify import --source tidal' instead");
rootCommand.Add(importTidalCommand);

// ── Parse & dispatch ──────────────────────────────────────────
var parseResult = rootCommand.Parse(args);

if (parseResult.Errors.Count > 0)
{
    foreach (var error in parseResult.Errors)
        await Console.Error.WriteLineAsync(error.Message);
    return 1;
}

var invokedCommand = parseResult.CommandResult.Command;
var dryRun = parseResult.GetValue(dryRunOption);
var limit = parseResult.GetValue(limitOption);

// queue-albums: parent command is the provider name
if (invokedCommand.Name == "queue-albums")
{
    var providerKey = parseResult.CommandResult.Parent is CommandResult parentCmd
        ? parentCmd.Command.Name
        : throw new InvalidOperationException("queue-albums must be nested under a provider command");
    return await RunQueueAlbumsAsync(providerKey, dryRun, limit, host.Services, cts.Token);
}

// reconcile-queue: parent command is the provider name
if (invokedCommand.Name == "reconcile-queue")
{
    var providerKey = parseResult.CommandResult.Parent is CommandResult reconcileParent
        ? reconcileParent.Command.Name
        : throw new InvalidOperationException("reconcile-queue must be nested under a provider command");
    return await RunReconcileAsync(providerKey, dryRun, host.Services, cts.Token);
}

// import: parent command is target provider, --source is the source provider
if (invokedCommand.Name == "import")
{
    var targetProviderKey = parseResult.CommandResult.Parent is CommandResult importParent
        ? importParent.Command.Name
        : throw new InvalidOperationException("import must be nested under a provider command");

    var sourceProviderKey = targetProviderKey == "spotify"
        ? parseResult.GetValue(spotifySourceOption)
        : parseResult.GetValue(tidalSourceOption);

    if (string.IsNullOrEmpty(sourceProviderKey))
    {
        await Console.Error.WriteLineAsync("--source is required. Valid values: spotify, tidal");
        return 1;
    }

    return await RunImportAsync(targetProviderKey, sourceProviderKey, dryRun, limit, host.Services, cts.Token);
}

// Deprecated: sync → spotify queue-albums
if (invokedCommand == syncCommand)
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Musync");
    Log.DeprecatedCommand(logger, "sync", "spotify queue-albums");
    var result = await RunQueueAlbumsAsync("spotify", dryRun, limit, host.Services, cts.Token);
    return result == 0 ? 3 : result;
}

// Deprecated: import-tidal → spotify import --source tidal
if (invokedCommand == importTidalCommand)
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Musync");
    Log.DeprecatedCommand(logger, "import-tidal", "spotify import --source tidal");
    var result = await RunImportAsync("spotify", "tidal", dryRun, limit, host.Services, cts.Token);
    return result == 0 ? 3 : result;
}

return 0;

// ── Helpers ───────────────────────────────────────────────────

static async Task<int> RunQueueAlbumsAsync(
    string providerKey,
    bool dryRun,
    int? limit,
    IServiceProvider services,
    CancellationToken ct)
{
    if (providerKey != "spotify")
    {
        await Console.Error.WriteLineAsync(
            "queue-albums is only supported for Spotify. Tidal is import-source only — use 'spotify import --source tidal'.");
        return 1;
    }

    await using var scope = services.CreateAsyncScope();

    // Validate options before the provider is built (its HTTP pipeline reads them too).
    var (opts, optsError) = TryResolveSpotifyOptions(scope.ServiceProvider);
    if (opts is null)
    {
        await Console.Error.WriteLineAsync(optsError);
        return 1;
    }
    if (string.IsNullOrEmpty(opts.QueuePlaylistId))
    {
        await Console.Error.WriteLineAsync("Spotify:QueuePlaylistId is required for sync.");
        return 1;
    }

    var provider = scope.ServiceProvider.GetKeyedService<IMusicProvider>(providerKey);
    if (provider is null)
    {
        await Console.Error.WriteLineAsync(
            $"Provider '{providerKey}' is not configured. Check appsettings.json.");
        return 1;
    }

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var step1 = scope.ServiceProvider.GetRequiredService<SyncStep1_SnapshotAndDiff>();
    var step2 = scope.ServiceProvider.GetRequiredService<SyncStep2_AddNewTracks>();
    var step3 = scope.ServiceProvider.GetRequiredService<SyncStep3_GenerateReport>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<QueueAlbumsOrchestrator>>();

    var playlistId = opts.QueuePlaylistId;
    var maxParallelism = opts.MaxConcurrentRequests;

    var ctx = new SyncRunContext(providerKey, provider, playlistId, maxParallelism, dryRun, limit);
    var orchestrator = new QueueAlbumsOrchestrator(db, step1, step2, step3, logger);

    try
    {
        await orchestrator.RunAsync(ctx, ct);
    }
    catch (OperationCanceledException)
    {
        return 2;
    }
    catch (Exception ex)
    {
        Log.JobFailed(logger, ex.Message, ex);
        return 1;
    }

    return 0;
}

static async Task<int> RunReconcileAsync(
    string providerKey,
    bool dryRun,
    IServiceProvider services,
    CancellationToken ct)
{
    if (providerKey != "spotify")
    {
        await Console.Error.WriteLineAsync($"reconcile-queue is not supported for provider: {providerKey}");
        return 1;
    }

    await using var scope = services.CreateAsyncScope();

    var (opts, optsError) = TryResolveSpotifyOptions(scope.ServiceProvider);
    if (opts is null)
    {
        await Console.Error.WriteLineAsync(optsError);
        return 1;
    }
    if (string.IsNullOrEmpty(opts.QueuePlaylistId))
    {
        await Console.Error.WriteLineAsync("Spotify:QueuePlaylistId is required for reconcile-queue.");
        return 1;
    }
    var playlistId = opts.QueuePlaylistId;

    var provider = scope.ServiceProvider.GetKeyedService<IMusicProvider>(providerKey);
    if (provider is null)
    {
        await Console.Error.WriteLineAsync(
            $"Provider '{providerKey}' is not configured. Check appsettings.json.");
        return 1;
    }

    var job = scope.ServiceProvider.GetRequiredService<ReconcileQueueJob>();
    var ctx = new ReconcileRunContext(providerKey, provider, playlistId, dryRun);

    try
    {
        await job.RunAsync(ctx, ct);
    }
    catch (OperationCanceledException)
    {
        return 2;
    }
    catch (Exception)
    {
        // RunAsync already logged the failure and marked the JobRun failed.
        return 1;
    }

    return 0;
}

static async Task<int> RunImportAsync(
    string targetProviderKey,
    string sourceProviderKey,
    bool dryRun,
    int? limit,
    IServiceProvider services,
    CancellationToken ct)
{
    var validProviders = new[] { "spotify", "tidal" };
    if (!validProviders.Contains(targetProviderKey))
    {
        await Console.Error.WriteLineAsync($"Unknown target provider: {targetProviderKey}");
        return 1;
    }
    if (!validProviders.Contains(sourceProviderKey))
    {
        await Console.Error.WriteLineAsync($"Unknown source provider: {sourceProviderKey}");
        return 1;
    }
    if (targetProviderKey == sourceProviderKey)
    {
        await Console.Error.WriteLineAsync("Source and target providers must be different.");
        return 1;
    }
    if (targetProviderKey == "tidal")
    {
        await Console.Error.WriteLineAsync(
            "Tidal cannot be an import target — it is import-source only. Use 'spotify import --source tidal'.");
        return 1;
    }

    await using var scope = services.CreateAsyncScope();

    // Target is always Spotify (guarded above); validate its options before any provider is built.
    var (spotOpts, spotOptsError) = TryResolveSpotifyOptions(scope.ServiceProvider);
    if (spotOpts is null)
    {
        await Console.Error.WriteLineAsync(spotOptsError);
        return 1;
    }
    if (string.IsNullOrEmpty(spotOpts.QueuePlaylistId))
    {
        await Console.Error.WriteLineAsync("Spotify:QueuePlaylistId is required for import.");
        return 1;
    }
    var playlistId = spotOpts.QueuePlaylistId;

    var (targetProvider, targetError) = TryResolveProvider(scope.ServiceProvider, targetProviderKey);
    if (targetError is not null)
    {
        await Console.Error.WriteLineAsync(targetError);
        return 1;
    }
    if (targetProvider is null)
    {
        await Console.Error.WriteLineAsync(
            $"Provider '{targetProviderKey}' is not configured. Check appsettings.json.");
        return 1;
    }

    var (sourceProvider, sourceError) = TryResolveProvider(scope.ServiceProvider, sourceProviderKey);
    if (sourceError is not null)
    {
        await Console.Error.WriteLineAsync(sourceError);
        return 1;
    }
    if (sourceProvider is null)
    {
        await Console.Error.WriteLineAsync(
            $"Provider '{sourceProviderKey}' is not configured. Check appsettings.json.");
        return 1;
    }

    var mapper = scope.ServiceProvider.GetKeyedService<ITrackMapper>(targetProviderKey);
    if (mapper is null)
    {
        await Console.Error.WriteLineAsync(
            $"Track mapper for '{targetProviderKey}' is not available.");
        return 1;
    }

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var step1 = scope.ServiceProvider.GetRequiredService<ImportStep1_FetchAndMap>();
    var step2 = scope.ServiceProvider.GetRequiredService<ImportStep2_AddToQueue>();
    var step3 = scope.ServiceProvider.GetRequiredService<ImportStep3_GenerateReport>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<ImportOrchestrator>>();

    var ctx = new ImportRunContext(sourceProviderKey, targetProviderKey, sourceProvider, targetProvider, mapper, playlistId, dryRun, limit);
    var orchestrator = new ImportOrchestrator(db, step1, step2, step3, logger);

    try
    {
        await orchestrator.RunAsync(ctx, ct);
    }
    catch (OperationCanceledException)
    {
        return 2;
    }
    catch (Exception ex)
    {
        Log.JobFailed(logger, ex.Message, ex);
        return 1;
    }

    return 0;
}

// Resolves and validates Spotify options, turning a data-annotation failure into a message.
static (SpotifyOptions? Options, string? Error) TryResolveSpotifyOptions(IServiceProvider services)
{
    try
    {
        return (services.GetRequiredService<IOptions<SpotifyOptions>>().Value, null);
    }
    catch (OptionsValidationException ex)
    {
        return (null, $"Invalid Spotify configuration: {string.Join("; ", ex.Failures)}");
    }
}

// Null provider with null error means the provider isn't registered; an error means its options
// failed validation while the HTTP pipeline read them.
static (IMusicProvider? Provider, string? Error) TryResolveProvider(IServiceProvider services, string key)
{
    try
    {
        return (services.GetKeyedService<IMusicProvider>(key), null);
    }
    catch (OptionsValidationException ex)
    {
        return (null, $"Invalid {key} configuration: {string.Join("; ", ex.Failures)}");
    }
}
