using System.Security.Cryptography;

namespace Triesoft.Core.Identity;

/// <summary>
/// PBKDF2-HMAC-SHA256 native (<see cref="Rfc2898DeriveBytes"/>) -- tanpa dependency tambahan,
/// konsisten dengan prinsip trusted-computing-base minimal di fase-fase sebelumnya. 210.000
/// iterasi mengikuti rekomendasi minimum OWASP (2023) untuk PBKDF2-HMAC-SHA256.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static (byte[] Hash, byte[] Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return (hash, salt);
    }

    public static bool Verify(string password, byte[] hash, byte[] salt)
    {
        var computed = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return CryptographicOperations.FixedTimeEquals(computed, hash);
    }
}
