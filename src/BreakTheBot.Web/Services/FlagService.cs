using System.Security.Cryptography;
using System.Text;

namespace BreakTheBot.Web.Services;

/// <summary>
/// Creates per-user, per-level flags: FLAG{L{level}_{12 hex chars}}.
/// The flag is an HMAC of "userId:levelId" using the server secret, so it is never
/// stored in the database or in source code.
/// </summary>
public class FlagService
{
    private readonly byte[] _key;

    public FlagService(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Flags:Secret is missing. Set it with dotnet user-secrets.");
        _key = Encoding.UTF8.GetBytes(secret);
    }

    public string GetFlag(string userId, int levelId)
    {
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{userId}:{levelId}"));
        var hex = Convert.ToHexString(hash).ToLowerInvariant()[..12];
        return $"FLAG{{L{levelId}_{hex}}}";
    }

    public bool IsCorrect(string userId, int levelId, string? submitted)
    {
        if (string.IsNullOrWhiteSpace(submitted)) return false;
        var expected = Encoding.UTF8.GetBytes(GetFlag(userId, levelId));
        var actual = Encoding.UTF8.GetBytes(submitted.Trim());
        // Constant-time comparison so timing cannot leak how close a guess was.
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}