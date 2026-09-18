namespace Triesoft.Core.Crypto;

internal static class Ts4Constants
{
    /// <summary>"TS4\0" — 4 byte pertama setiap file .ts4.</summary>
    public static readonly byte[] Magic = [0x54, 0x53, 0x34, 0x00];

    public const byte FormatVersion = 1;

    public const int WrapNonceSize = 12;
    public const int NameNonceSize = 12;
    public const int GcmTagSize = 16;
    public const int DekSize = 32;
    public const int BaseNonceSize = 4;
    public const int ChunkCounterSize = 8;

    public const int DefaultChunkSize = 1024 * 1024; // 1 MiB

    /// <summary>
    /// Batas atas wajar untuk ChunkSize yang dibaca dari header file eksternal —
    /// mencegah header yang dipalsukan memicu alokasi buffer raksasa (DoS).
    /// </summary>
    public const uint MaxAllowedChunkSize = 64 * 1024 * 1024; // 64 MiB
}
