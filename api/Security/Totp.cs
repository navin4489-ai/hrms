using System.Security.Cryptography;
using System.Text;

namespace Wintech.Hrms.Api.Security;

/// <summary>
/// RFC 6238 TOTP without external dependencies — FR-EMP-003 (MFA, mandatory for privileged roles).
/// Compatible with Google Authenticator / Authy / 1Password (SHA-1, 6 digits, 30 s step).
/// </summary>
public static class Totp
{
    private const int Digits = 6;
    private const int StepSeconds = 30;
    private const int AllowedClockDriftSteps = 1;

    public static string GenerateSecret(int byteLength = 20)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base32Encode(bytes);
    }

    public static string ComputeCode(string base32Secret, DateTime? utcNow = null)
    {
        var key = Base32Decode(base32Secret);
        var counter = (long)((utcNow ?? DateTime.UtcNow).ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds / StepSeconds;
        return Compute(key, counter);
    }

    public static bool Verify(string base32Secret, string? code)
    {
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code)) return false;
        var normalized = code.Trim().Replace(" ", string.Empty);
        if (normalized.Length != Digits || !normalized.All(char.IsDigit)) return false;

        var key = Base32Decode(base32Secret);
        var counter = (long)((DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds) / StepSeconds;

        for (var i = -AllowedClockDriftSteps; i <= AllowedClockDriftSteps; i++)
        {
            if (string.Equals(Compute(key, counter + i), normalized, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    public static string BuildOtpAuthUri(string secret, string accountName, string issuer = "Wintech HRMS")
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        return $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";
    }

    private static string Compute(byte[] key, long counter)
    {
        var counterBytes = new byte[8];
        BitConverter.TryWriteBytes(counterBytes.AsSpan(), counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) |
                     (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % (int)Math.Pow(10, Digits)).ToString().PadLeft(Digits, '0');
    }

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                sb.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0) sb.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return sb.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = input.Trim().TrimEnd('=').Replace(" ", string.Empty).ToUpperInvariant();
        var bytes = new List<byte>(clean.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var c in clean)
        {
            var index = alphabet.IndexOf(c);
            if (index < 0) throw new FormatException("Invalid base32 character.");
            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft < 8) continue;
            bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
            bitsLeft -= 8;
        }

        return bytes.ToArray();
    }
}
