namespace Triesoft.Core.Crypto;

/// <summary>
/// Membaca plaintext dari sebuah <see cref="Stream"/> yang panjangnya tidak diketahui di muka,
/// dan membaginya menjadi chunk berukuran tetap sambil menentukan chunk mana yang terakhir —
/// termasuk kasus tepi saat panjang file adalah kelipatan persis dari ukuran chunk (memakai
/// teknik lookahead 1 byte, pola umum pada konstruksi streaming AEAD).
/// </summary>
internal sealed class ChunkedPlaintextReader(Stream input, int chunkSize)
{
    private byte? _pending;
    private bool _done;

    /// <summary>
    /// Mengisi <paramref name="buffer"/> dengan chunk berikutnya. Mengembalikan <c>false</c>
    /// jika chunk terakhir sudah pernah dikembalikan pada panggilan sebelumnya.
    /// </summary>
    public bool TryReadNextChunk(byte[] buffer, out int length, out bool isFinal)
    {
        if (_done)
        {
            length = 0;
            isFinal = false;
            return false;
        }

        int offset = 0;
        if (_pending is { } pending)
        {
            buffer[0] = pending;
            offset = 1;
            _pending = null;
        }

        int filled = offset;
        while (filled < chunkSize)
        {
            int read = input.Read(buffer, filled, chunkSize - filled);
            if (read == 0) break;
            filled += read;
        }

        if (filled < chunkSize)
        {
            // Stream berakhir sebelum chunk ini penuh -> pasti chunk terakhir.
            length = filled;
            isFinal = true;
            _done = true;
            return true;
        }

        // Chunk penuh persis chunkSize -- intip 1 byte lagi untuk tahu apakah masih ada data
        // berikutnya (menangani kasus panjang file kelipatan persis dari chunkSize).
        Span<byte> peek = stackalloc byte[1];
        int peeked = input.Read(peek);
        if (peeked == 0)
        {
            length = filled;
            isFinal = true;
            _done = true;
        }
        else
        {
            _pending = peek[0];
            length = filled;
            isFinal = false;
        }

        return true;
    }
}
