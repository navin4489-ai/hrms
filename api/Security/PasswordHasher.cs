using System.Security.Cryptography;

namespace Wintech.Hrms.Api.Security;

/// <summary>
/// PBKDF2 (SHA-256, 100k iterations) password hashing — BRD section 11: passwords are never stored
/// or logged in any recoverable form.
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "PBKDF2";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash)) return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations < 10_000) return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>SHA-256 hash used for opaque refresh/reset tokens (only hashes are persisted).</summary>
    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    public static string CreateSecureToken(int bytes = 48) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
