namespace Triesoft.Core.Crypto;

internal static class StreamHelpers
{
    /// <summary>
    /// Mengisi <paramref name="buffer"/> penuh dari <paramref name="stream"/>.
    /// Mengembalikan <c>false</c> HANYA jika stream sudah di EOF sebelum satu byte pun terbaca
    /// (EOF yang wajar/diharapkan). Melempar <see cref="EndOfStreamException"/> jika stream
    /// berakhir di tengah pengisian buffer (data terpotong/rusak).
    /// </summary>
    public static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer[totalRead..]);
            if (read == 0)
            {
                if (totalRead == 0) return false;
                throw new EndOfStreamException("Data terpotong: stream berakhir di tengah pembacaan.");
            }

            totalRead += read;
        }

        return true;
    }
}
