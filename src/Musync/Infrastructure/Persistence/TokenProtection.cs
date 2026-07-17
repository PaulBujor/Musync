using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Musync.Infrastructure.Persistence;

/// <summary>
///     Encrypts refresh-token values for storage. Reads tolerate legacy plaintext so an
///     existing unencrypted token keeps working until it is next rotated and re-encrypted.
/// </summary>
internal static class TokenProtection
{
    public static string Protect(IDataProtector protector, string value)
    {
        return protector.Protect(value);
    }

    public static string Unprotect(IDataProtector protector, string value)
    {
        try
        {
            return protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            // Not our ciphertext (legacy plaintext, or written with a key we no longer have).
            // Hand the raw value back; the auth layer re-authenticates if it turns out invalid.
            return value;
        }
    }
}