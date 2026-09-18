using System.Security.Cryptography;

namespace Triesoft.Core.Crypto;

internal static class CryptoRandom
{
    public static byte[] GetBytes(int count)
    {
        var buffer = new byte[count];
        RandomNumberGenerator.Fill(buffer);
        return buffer;
    }
}
