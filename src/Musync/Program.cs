using System.CommandLine;
using System.CommandLine.Parsing;
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

// Spotify options
builder.Services
    .AddOptions<SpotifyOptions>()
    .BindConfiguration("Spotify")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Tidal options (validated on use, not on start)
builder.Services
    .AddOptions<TidalOptions>()
    .BindConfiguration("Tidal")
    .ValidateDataAnnotations();

builder.Services.AddHybridCache();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")!));

// Step classes (providers come via RunContext, not DI)
builder.Services.AddScoped<SyncStep1_SnapshotAndDiff>();
builder.Services.AddScoped<SyncStep2_AddNewTracks>();
builder.Services.AddScoped<SyncStep3_GenerateReport>();
builder.Services.AddScoped<ImportStep1_FetchAndMap>();
builder.Services.AddScoped<ImportStep2_AddToQueue>();
builder.Services.AddScoped<ImportStep3_GenerateReport>();

// Spotify auth + HTTP
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
        options.Retry.DelayGenerator = args =>
        {
            if (args.Outcome.Result?.Headers.RetryAfter?.Delta is { } delta)
                return ValueTask.FromResult<TimeSpan?>(delta);
            return ValueTask.FromResult<TimeSpan?>(null);
        };
    });
builder.Services.AddKeyedSingleton<IMusicProvider>("spotify", (sp, _) =>
    new SpotifyMusicProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("spotify-music")));

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
        })
        .AddHttpMessageHandler<TidalTokenHandler>()
        .AddStandardResilienceHandler();
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
        options.Retry.MaxRetryAttempts = 3;
    });
builder.Services.AddKeyedSingleton<ITrackMapper>("spotify", (sp, _) =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("track-mapper");
    var logger = sp.GetRequiredService<ILogger<SpotifySearchMapper>>();
    return new SpotifySearchMapper(httpClient, logger);
});
builder.Services.AddKeyedSingleton<ITrackMapper>("tidal", (_, _) => new TidalSearchMapper());

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// ── Command tree ──────────────────────────────────────────────
var rootCommand = new RootCommand("Musync — multi-provider music queue manager");

var dryRunOption = new Option<bool>("--dry-run", "Preview changes without mutating providers") { Recursive = true };
var limitOption = new Option<int?>("--limit", "Maximum number of items to process") { Recursive = true };
rootCommand.Add(dryRunOption);
rootCommand.Add(limitOption);

var spotifyCmd = new Command("spotify", "Spotify operations");
var tidalCmd = new Command("tidal", "Tidal operations");
rootCommand.Add(spotifyCmd);
rootCommand.Add(tidalCmd);

var spotifyQueueAlbums = new Command("queue-albums", "Sync saved albums to the queue playlist");
spotifyCmd.Add(spotifyQueueAlbums);

var tidalQueueAlbums = new Command("queue-albums", "Sync saved albums to the queue playlist");
tidalCmd.Add(tidalQueueAlbums);

var spotifySourceOption = new Option<string>("--source", "Source provider to import from")
    .AcceptOnlyFromAmong("spotify", "tidal");
var spotifyImport = new Command("import", "Import tracks from another provider");
spotifyImport.Add(spotifySourceOption);
spotifyCmd.Add(spotifyImport);

var tidalSourceOption = new Option<string>("--source", "Source provider to import from")
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
    await using var scope = services.CreateAsyncScope();
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

    string playlistId;
    int maxParallelism;

    if (providerKey == "spotify")
    {
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<SpotifyOptions>>().Value;
        playlistId = opts.QueuePlaylistId;
        maxParallelism = opts.MaxConcurrentRequests;
    }
    else if (providerKey == "tidal")
    {
        var opts = scope.ServiceProvider.GetRequiredService<IOptions<TidalOptions>>().Value;
        playlistId = opts.QueuePlaylistId;
        maxParallelism = opts.MaxConcurrentRequests;
    }
    else
    {
        await Console.Error.WriteLineAsync($"Unknown provider: {providerKey}");
        return 1;
    }

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

    await using var scope = services.CreateAsyncScope();

    var targetProvider = scope.ServiceProvider.GetKeyedService<IMusicProvider>(targetProviderKey);
    if (targetProvider is null)
    {
        await Console.Error.WriteLineAsync(
            $"Provider '{targetProviderKey}' is not configured. Check appsettings.json.");
        return 1;
    }

    var sourceProvider = scope.ServiceProvider.GetKeyedService<IMusicProvider>(sourceProviderKey);
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

    string playlistId;
    if (targetProviderKey == "spotify")
        playlistId = scope.ServiceProvider.GetRequiredService<IOptions<SpotifyOptions>>().Value.QueuePlaylistId;
    else
        playlistId = scope.ServiceProvider.GetRequiredService<IOptions<TidalOptions>>().Value.QueuePlaylistId;

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
