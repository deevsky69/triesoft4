using System.Buffers.Binary;
using System.Text;

namespace Triesoft.Core.Crypto;

/// <summary>Serialisasi/deserialisasi header file .ts4.</summary>
internal static class Ts4FileFormat
{
    public static byte[] SerializeHeader(Ts4Header header)
    {
        var keyIdBytes = Encoding.UTF8.GetBytes(header.KeyId);
        if (keyIdBytes.Length > ushort.MaxValue)
            throw new ArgumentException("KeyId terlalu panjang.", nameof(header));
        if (header.EncryptedFileName.Length > ushort.MaxValue)
            throw new ArgumentException("Nama file terenkripsi terlalu panjang.", nameof(header));

        using var ms = new MemoryStream();
        ms.Write(Ts4Constants.Magic);
        ms.WriteByte(Ts4Constants.FormatVersion);
        ms.WriteByte((byte)header.AlgorithmProfile);

        WriteUInt16Prefixed(ms, keyIdBytes);

        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u32, header.ChunkSize);
        ms.Write(u32);

        ms.Write(header.ContentBaseNonce);
        ms.Write(header.WrapNonce);
        ms.Write(header.WrappedDek);
        ms.Write(header.WrapTag);
        ms.Write(header.NameNonce);
        WriteUInt16Prefixed(ms, header.EncryptedFileName);
        ms.Write(header.NameTag);

        return ms.ToArray();
    }

    /// <summary>
    /// Membaca &amp; memvalidasi header dari awal <paramref name="input"/>. Mengembalikan header
    /// yang sudah di-parse sekaligus byte mentahnya (dipakai lagi sebagai associated data untuk
    /// setiap chunk, mengikat seluruh metadata ke autentikasi isi file).
    /// </summary>
    public static (Ts4Header Header, byte[] HeaderBytes) ReadHeader(Stream input)
    {
        using var mirror = new MemoryStream();

        Span<byte> magic = stackalloc byte[4];
        ReadExactlyInto(input, magic, mirror);
        if (!magic.SequenceEqual(Ts4Constants.Magic))
            throw new InvalidDataException("File bukan format TRIESOFT4 (.ts4) yang valid — magic bytes tidak cocok.");

        byte version = ReadByteInto(input, mirror);
        if (version != Ts4Constants.FormatVersion)
            throw new InvalidDataException($"Versi format .ts4 tidak didukung: {version}.");

        byte profileByte = ReadByteInto(input, mirror);
        var profile = (AlgorithmProfile)profileByte;
        if (profile != AlgorithmProfile.AesGcm256)
            throw new NotSupportedException($"Algorithm profile 0x{profileByte:X2} belum didukung pada versi ini.");

        var keyIdBytes = ReadUInt16PrefixedInto(input, mirror);
        var keyId = Encoding.UTF8.GetString(keyIdBytes);

        Span<byte> u32 = stackalloc byte[4];
        ReadExactlyInto(input, u32, mirror);
        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(u32);
        if (chunkSize == 0 || chunkSize > Ts4Constants.MaxAllowedChunkSize)
            throw new InvalidDataException("Ukuran chunk pada header tidak valid.");

        var baseNonce = ReadFixedInto(input, mirror, Ts4Constants.BaseNonceSize);
        var wrapNonce = ReadFixedInto(input, mirror, Ts4Constants.WrapNonceSize);
        var wrappedDek = ReadFixedInto(input, mirror, Ts4Constants.DekSize);
        var wrapTag = ReadFixedInto(input, mirror, Ts4Constants.GcmTagSize);
        var nameNonce = ReadFixedInto(input, mirror, Ts4Constants.NameNonceSize);
        var encryptedFileName = ReadUInt16PrefixedInto(input, mirror);
        var nameTag = ReadFixedInto(input, mirror, Ts4Constants.GcmTagSize);

        var header = new Ts4Header
        {
            AlgorithmProfile = profile,
            KeyId = keyId,
            ChunkSize = chunkSize,
            ContentBaseNonce = baseNonce,
            WrapNonce = wrapNonce,
            WrappedDek = wrappedDek,
            WrapTag = wrapTag,
            NameNonce = nameNonce,
            EncryptedFileName = encryptedFileName,
            NameTag = nameTag,
        };

        return (header, mirror.ToArray());
    }

    private static void WriteUInt16Prefixed(Stream output, byte[] data)
    {
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(len, (ushort)data.Length);
        output.Write(len);
        output.Write(data);
    }

    private static byte ReadByteInto(Stream input, Stream mirror)
    {
        Span<byte> one = stackalloc byte[1];
        ReadExactlyInto(input, one, mirror);
        return one[0];
    }

    private static byte[] ReadFixedInto(Stream input, Stream mirror, int size)
    {
        var buffer = new byte[size];
        ReadExactlyInto(input, buffer, mirror);
        return buffer;
    }

    private static byte[] ReadUInt16PrefixedInto(Stream input, Stream mirror)
    {
        Span<byte> lenBuf = stackalloc byte[2];
        ReadExactlyInto(input, lenBuf, mirror);
        var len = BinaryPrimitives.ReadUInt16LittleEndian(lenBuf);
        var data = new byte[len];
        ReadExactlyInto(input, data, mirror);
        return data;
    }

    private static void ReadExactlyInto(Stream input, Span<byte> buffer, Stream mirror)
    {
        bool ok;
        try
        {
            ok = StreamHelpers.TryReadExactly(input, buffer);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("File .ts4 terpotong: header tidak lengkap.");
        }

        if (!ok)
            throw new InvalidDataException("File .ts4 terpotong: header tidak lengkap.");

        mirror.Write(buffer);
    }
}
