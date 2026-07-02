using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

// Tidal options (validated on use, not on start — sync doesn't need them)
builder.Services
    .AddOptions<TidalOptions>()
    .BindConfiguration("Tidal")
    .ValidateDataAnnotations();

builder.Services.AddHybridCache();
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Valkey") ?? "localhost:6379";
});
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")!));

// Sync job steps
builder.Services.AddScoped<SyncStep1_SnapshotAndDiff>();
builder.Services.AddScoped<SyncStep2_AddNewTracks>();
builder.Services.AddScoped<SyncStep3_GenerateReport>();
builder.Services.AddScoped<JobOrchestrator>();

// Tidal import job steps
builder.Services.AddScoped<ImportTidalStep1_FetchAndMap>();
builder.Services.AddScoped<ImportTidalStep2_AddToQueue>();
builder.Services.AddScoped<ImportTidalStep3_GenerateReport>();
builder.Services.AddScoped<ImportTidalOrchestrator>();

var spotifyConfig = new SpotifyOptions();
builder.Configuration.GetSection("Spotify").Bind(spotifyConfig);
var tidalConfig = new TidalOptions();
builder.Configuration.GetSection("Tidal").Bind(tidalConfig);

// Spotify auth + HTTP (default IMusicProvider)
builder.Services.AddScoped<ISpotifyAuthenticator, SpotifyAuthenticator>();
builder.Services.AddTransient<SpotifyTokenHandler>();
builder.Services
    .AddHttpClient<IMusicProvider, SpotifyMusicProvider>(client =>
    {
        client.BaseAddress = new Uri(spotifyConfig.ApiBaseUrl);
    })
    .AddHttpMessageHandler<SpotifyTokenHandler>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = spotifyConfig.MaxRetries;
        options.Retry.DelayGenerator = args =>
        {
            if (args.Outcome.Result?.Headers.RetryAfter?.Delta is { } delta)
                return ValueTask.FromResult<TimeSpan?>(delta);
            return ValueTask.FromResult<TimeSpan?>(null);
        };
    });

// Tidal auth + HTTP (keyed "tidal")
builder.Services.AddScoped<ITidalAuthenticator, TidalAuthenticator>();
builder.Services.AddTransient<TidalTokenHandler>();
builder.Services.AddHttpClient("tidal-music", client =>
{
    client.BaseAddress = new Uri(tidalConfig.ApiBaseUrl);
})
.AddHttpMessageHandler<TidalTokenHandler>()
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = tidalConfig.MaxRetries;
});
builder.Services.AddKeyedSingleton<IMusicProvider>("tidal", (sp, _) =>
    new TidalMusicProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("tidal-music")));

// Track mapper (SearchSpotify for ISRC matching)
builder.Services
    .AddHttpClient<ITrackMapper, SpotifySearchMapper>(client =>
    {
        client.BaseAddress = new Uri(spotifyConfig.ApiBaseUrl);
    })
    .AddHttpMessageHandler<SpotifyTokenHandler>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
    });

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

var rootCommand = new RootCommand("Spotify Queue Manager");
var syncCommand = new Command("sync", "Sync saved albums to the queue playlist");
rootCommand.Add(syncCommand);

var importTidalCommand = new Command("import-tidal", "Import Tidal favorites to the queue playlist");
rootCommand.Add(importTidalCommand);

var parseResult = rootCommand.Parse(args);
var invokedCommand = parseResult.CommandResult.Command;

if (invokedCommand == syncCommand || invokedCommand == rootCommand)
{
    await using var scope = host.Services.CreateAsyncScope();
    var orchestrator = scope.ServiceProvider.GetRequiredService<JobOrchestrator>();
    try
    {
        await orchestrator.RunAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        return 2;
    }
    catch
    {
        return 1;
    }
}
else if (invokedCommand == importTidalCommand)
{
    // Validate Tidal options before running
    var tidalOpts = host.Services.GetRequiredService<IOptions<TidalOptions>>().Value;
    if (string.IsNullOrEmpty(tidalOpts.ClientId))
    {
        await Console.Error.WriteLineAsync("Tidal ClientId is not configured. Set Tidal__ClientId in your .env file.");
        return 1;
    }

    await using var scope = host.Services.CreateAsyncScope();
    var orchestrator = scope.ServiceProvider.GetRequiredService<ImportTidalOrchestrator>();
    try
    {
        await orchestrator.RunAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        return 2;
    }
    catch
    {
        return 1;
    }
}
else
{
    await parseResult.InvokeAsync(cancellationToken: cts.Token);
    return parseResult.Errors.Count == 0 ? 0 : 1;
}

return 0;
