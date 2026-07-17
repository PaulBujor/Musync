using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Tests.Infrastructure;

public sealed class RefreshTokenEncryptionTests
{
    private const string Plaintext = "super-secret-refresh-token";

    private static AppDbContext BuildDb()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        var db = services.BuildServiceProvider().GetRequiredService<AppDbContext>();
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Token_StoredAsCiphertext_ButReadBackAsPlaintext()
    {
        var db = BuildDb();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            Token = Plaintext,
            Provider = "spotify",
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Raw column value must not be the plaintext.
        var connection = db.Database.GetDbConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Token FROM RefreshTokens";
        var stored = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.NotEqual(Plaintext, stored);

        // Reading through EF transparently decrypts it.
        db.ChangeTracker.Clear();
        var roundTripped = await db.RefreshTokens.SingleAsync();
        Assert.Equal(Plaintext, roundTripped.Token);
    }

    [Fact]
    public async Task LegacyPlaintextToken_IsReadableAsIs()
    {
        var db = BuildDb();
        db.Database.EnsureCreated();

        // Simulate a row written before encryption existed: raw plaintext in the column.
        var connection = db.Database.GetDbConnection();
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO RefreshTokens (Id, Token, Provider, UpdatedAt) VALUES ($id, $token, $provider, $updated)";
            AddParam(insert, "$id", Guid.CreateVersion7().ToString());
            AddParam(insert, "$token", Plaintext);
            AddParam(insert, "$provider", "spotify");
            AddParam(insert, "$updated", DateTime.UtcNow.ToString("o"));
            await insert.ExecuteNonQueryAsync();
        }

        var token = await db.RefreshTokens.SingleAsync();
        Assert.Equal(Plaintext, token.Token);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
