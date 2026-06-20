using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Options;

namespace SpotifyTools.Infrastructure.Tidal;

public sealed class TidalAuthenticator(
    IOptions<TidalOptions> options,
    SpotifyDbContext db,
    ILogger<TidalAuthenticator> logger,
    HttpClient httpClient)
    : ITidalAuthenticator
{
    private const string TokenUrl = "https://auth.tidal.com/v1/oauth2/token";
    private const string AuthUrl = "https://login.tidal.com/authorize";

    private readonly TidalOptions _options = options.Value;
    private string RedirectUri => _options.RedirectUri;

    public async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        var existingToken = await db.RefreshTokens
            .Where(x => x.Provider == "tidal")
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (existingToken is not null)
            return;

        logger.LogInformation("No Tidal refresh token found. Starting browser-based OAuth flow...");

        var (codeVerifier, codeChallenge) = GeneratePkcePair();
        var state = Guid.NewGuid().ToString("N");

        using var listener = new HttpListener();
        listener.Prefixes.Add(_options.RedirectUri.TrimEnd('/') + "/");
        listener.Start();

        var authUrl = $"{AuthUrl}?client_id={_options.ClientId}" +
                       $"&response_type=code" +
                       $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                       $"&scope={Uri.EscapeDataString("user.read user_collection.read")}" +
                       $"&state={state}" +
                       $"&code_challenge_method=S256" +
                       $"&code_challenge={codeChallenge}";

        logger.LogInformation("Opening browser for Tidal authorization...");
        Process.Start(new ProcessStartInfo
        {
            FileName = authUrl,
            UseShellExecute = true
        });

        var context = await listener.GetContextAsync();
        var code = context.Request.QueryString["code"];
        var returnedState = context.Request.QueryString["state"];

        if (string.IsNullOrEmpty(code))
        {
            var error = context.Request.QueryString["error"] ?? "unknown";
            await WriteResponse(context, $"Authorization failed: {error}");
            throw new InvalidOperationException($"Tidal authorization failed: {error}");
        }

        if (returnedState != state)
        {
            await WriteResponse(context, "State mismatch. Authentication failed.");
            throw new InvalidOperationException("OAuth state parameter mismatch");
        }

        await WriteResponse(context, "Authentication successful! You can close this window.");
        await ExchangeCodeForTokens(code, codeVerifier, ct);
    }

    private async Task ExchangeCodeForTokens(string code, string codeVerifier, CancellationToken ct)
    {
        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", RedirectUri),
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("code_verifier", codeVerifier)
        ]);

        var response = await httpClient.PostAsync(TokenUrl, content, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var refreshToken = doc.RootElement.GetProperty("refresh_token").GetString()!;

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            Token = refreshToken,
            Provider = "tidal",
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Tidal refresh token persisted successfully");
    }

    private static (string verifier, string challenge) GeneratePkcePair()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var verifier = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Convert.ToBase64String(challengeBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return (verifier, challenge);
    }

    private static async Task WriteResponse(HttpListenerContext context, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"<html><body><h1>{message}</h1></body></html>");
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.OutputStream.Close();
    }
}
