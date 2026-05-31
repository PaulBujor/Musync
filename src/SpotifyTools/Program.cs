using System.CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Infrastructure.Spotify;
using SpotifyTools.Jobs;
using SpotifyTools.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<SpotifyOptions>()
    .BindConfiguration("Spotify")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHybridCache();
builder.Services.AddDbContext<SpotifyDbContext>(options =>
    options.UseSqlite("Data Source=spotifyqueue.db;Cache=Shared;"));

builder.Services.AddScoped<SyncStep1_SnapshotAndDiff>();
builder.Services.AddScoped<SyncStep2_AddNewTracks>();
builder.Services.AddScoped<SyncStep3_GenerateReport>();
builder.Services.AddScoped<JobOrchestrator>();

builder.Services.AddScoped<ISpotifyAuthenticator, SpotifyAuthenticator>();
builder.Services.AddTransient<SpotifyTokenHandler>();
builder.Services
    .AddHttpClient<IMusicProvider, SpotifyMusicProvider>(client =>
    {
        client.BaseAddress = new Uri("https://api.spotify.com/v1/");
    })
    .AddHttpMessageHandler<SpotifyTokenHandler>()
    .AddStandardResilienceHandler(options =>
    {
        var spotifyOptions = new SpotifyOptions();
        builder.Configuration.GetSection("Spotify").Bind(spotifyOptions);
        options.Retry.MaxRetryAttempts = spotifyOptions.MaxRetries;
        options.Retry.DelayGenerator = args =>
        {
            if (args.Outcome.Result?.Headers.RetryAfter?.Delta is { } delta)
                return ValueTask.FromResult<TimeSpan?>(delta);
            return ValueTask.FromResult<TimeSpan?>(null);
        };
    });

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SpotifyDbContext>();
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
else
{
    await parseResult.InvokeAsync(cancellationToken: cts.Token);
    return parseResult.Errors.Count == 0 ? 0 : 1;
}

return 0;