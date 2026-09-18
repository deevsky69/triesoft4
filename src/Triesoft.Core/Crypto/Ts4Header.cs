namespace Triesoft.Core.Crypto;

/// <summary>Model header file .ts4 (lihat <see cref="Ts4FileFormat"/> untuk serialisasinya).</summary>
internal sealed class Ts4Header
{
    public required AlgorithmProfile AlgorithmProfile { get; init; }
    public required string KeyId { get; init; }
    public required uint ChunkSize { get; init; }

    /// <summary>4 byte acak, basis nonce untuk setiap chunk isi file.</summary>
    public required byte[] ContentBaseNonce { get; init; }

    public required byte[] WrapNonce { get; init; }
    public required byte[] WrappedDek { get; init; }
    public required byte[] WrapTag { get; init; }

    public required byte[] NameNonce { get; init; }
    public required byte[] EncryptedFileName { get; init; }
    public required byte[] NameTag { get; init; }
}
