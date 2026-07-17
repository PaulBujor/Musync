using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Persistence;

namespace Musync.Infrastructure.Auth;

public abstract class PkceAuthenticatorBase(
    AppDbContext db,
    ILogger logger,
    HttpClient httpClient) : IAuthenticator
{
    protected abstract string AuthUrl { get; }
    protected abstract string TokenUrl { get; }
    protected abstract string ProviderName { get; }
    protected abstract string Scope { get; }
    protected abstract string ClientId { get; }
    protected abstract string RedirectUri { get; }

    public async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        var existingToken = await db.RefreshTokens
            .Where(x => x.Provider == ProviderName)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (existingToken is not null)
            return;

        logger.LogInformation("No {Provider} refresh token found. Starting browser-based OAuth flow...", ProviderName);

        var (codeVerifier, codeChallenge) = GeneratePkcePair();
        var state = Guid.NewGuid().ToString("N");

        using var listener = new HttpListener();
        listener.Prefixes.Add(RedirectUri.TrimEnd('/') + "/");
        listener.Start();

        var authUrl = $"{AuthUrl}?client_id={ClientId}" +
                      $"&response_type=code" +
                      $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                      $"&scope={Uri.EscapeDataString(Scope)}" +
                      $"&state={state}" +
                      $"&code_challenge_method=S256" +
                      $"&code_challenge={codeChallenge}";

        logger.LogInformation("Opening browser for {Provider} authorization...", ProviderName);
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
            throw new InvalidOperationException($"{ProviderName} authorization failed: {error}");
        }

        if (returnedState != state)
        {
            await WriteResponse(context, "State mismatch. Authentication failed.");
            throw new InvalidOperationException("OAuth state parameter mismatch");
        }

        await WriteResponse(context, "Authentication successful! You can close this window.");
        await ExchangeCodeForTokensAsync(code, codeVerifier, ct);
    }

    private async Task ExchangeCodeForTokensAsync(string code, string codeVerifier, CancellationToken ct)
    {
        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", RedirectUri),
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("code_verifier", codeVerifier)
        ]);

        var response = await httpClient.PostAsync(TokenUrl, content, ct);
        response.EnsureSuccessStatusCode();

        using var doc =
            await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var refreshToken = doc.RootElement.GetProperty("refresh_token").GetString()!;

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            Token = refreshToken,
            Provider = ProviderName,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("{Provider} refresh token persisted successfully", ProviderName);
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