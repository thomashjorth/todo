using System.Security.Cryptography;
using System.Text;

namespace Todo.Tunnel;

/// <summary>
/// The one password the app has, stored as PBKDF2-SHA256 in a string that carries its own iteration
/// count: <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;</c>. Carrying the count is what lets
/// the tests' host verify with one iteration while the app's own hash takes 600,000, and what lets the
/// default rise later without invalidating a stored hash.
/// </summary>
public static class PasswordHash
{
    /// <summary>OWASP's figure for PBKDF2-HMAC-SHA256 as of 2023.</summary>
    public const int DefaultIterations = 600_000;

    private const string Scheme = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Create(string password, int iterations = DefaultIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, iterations);

        return $"{Scheme}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool IsWellFormed(string stored) => TryParse(stored, out _, out _, out _);

    public static bool Verify(string password, string stored)
    {
        if (!TryParse(stored, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        // Constant time, so how long a refusal takes says nothing about how much of the hash matched.
        return CryptographicOperations.FixedTimeEquals(Derive(password, salt, iterations), expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashBytes);

    private static bool TryParse(string stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];

        var parts = stored.Split('$');

        if (parts.Length != 4
            || parts[0] != Scheme
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, null, out iterations)
            || iterations < 1)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length == SaltBytes && hash.Length == HashBytes;
    }
}
