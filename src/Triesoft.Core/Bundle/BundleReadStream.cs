using System.Buffers.Binary;
using System.Text;

namespace Triesoft.Core.Bundle;

/// <summary>Satu file yang masuk bundle: path sumber dan nama entri di dalam bundle.</summary>
public sealed record BundleSource(string Path, string EntryName);

/// <summary>
/// Stream baca-saja yang membangkitkan isi bundle (lihat <see cref="BundleFormat"/>) langsung dari file-file sumber,
/// satu per satu, tanpa arsip sementara di disk dan tanpa memuat semuanya ke memori. Dipakai sebagai input
/// <c>EnvelopeCipher.Encrypt</c>.
/// </summary>
/// <remarks>
/// <see cref="CanSeek"/> sengaja true supaya <c>EnvelopeCipher</c> bisa menghitung progres dari <see cref="Length"/> dan
/// <see cref="Position"/>; Seek/SetPosition sesungguhnya tidak didukung dan melempar exception.
/// Ukuran tiap file diambil dari handle saat file dibuka. Kalau file jadi lebih pendek saat dibaca, dilempar
/// <see cref="IOException"/> -- bundle tidak boleh diam-diam berisi file yang terpotong.
/// </remarks>
public sealed class BundleReadStream : Stream
{
    private readonly IReadOnlyList<BundleSource> _sources;
    private readonly CancellationToken _cancellation;
    private readonly long _length;

    private long _position;
    private int _next;
    private bool _preambleWritten;
    private bool _trailerWritten;
    private bool _disposed;

    private byte[] _pending = [];
    private int _pendingOffset;

    private FileStream? _file;
    private long _fileRemaining;
    private string _fileName = "";

    public BundleReadStream(IReadOnlyList<BundleSource> sources, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0) throw new ArgumentException("Bundle butuh minimal satu file.", nameof(sources));
        if (sources.Count > BundleFormat.MaxEntries) throw new ArgumentException("Terlalu banyak file dalam satu bundle.", nameof(sources));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long length = BundleFormat.Magic.Length + 1 + 1 + 4;
        foreach (var source in sources)
        {
            if (BundleNames.ValidateEntryName(source.EntryName) is { } problem)
                throw new ArgumentException($"Nama entri '{source.EntryName}' tidak valid: {problem}.", nameof(sources));
            if (!seen.Add(source.EntryName))
                throw new ArgumentException($"Nama entri '{source.EntryName}' dipakai dua kali.", nameof(sources));

            length += 1 + 2 + Encoding.UTF8.GetByteCount(source.EntryName) + 8 + new FileInfo(source.Path).Length;
        }

        _sources = sources;
        _cancellation = cancellation;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cancellation.ThrowIfCancellationRequested();

        var total = 0;
        while (total < buffer.Length)
        {
            if (_pendingOffset < _pending.Length)
            {
                var n = Math.Min(_pending.Length - _pendingOffset, buffer.Length - total);
                _pending.AsSpan(_pendingOffset, n).CopyTo(buffer[total..]);
                _pendingOffset += n;
                total += n;
                continue;
            }

            if (_file is not null)
            {
                if (_fileRemaining == 0)
                {
                    CloseFile();
                    continue;
                }

                var want = (int)Math.Min(_fileRemaining, buffer.Length - total);
                var read = _file.Read(buffer.Slice(total, want));
                if (read == 0)
                    throw new IOException($"File '{_fileName}' berubah saat dibaca (menjadi lebih pendek dari saat bundle dimulai).");
                _fileRemaining -= read;
                total += read;
                continue;
            }

            if (!Advance()) break;
        }

        _position += total;
        return total;
    }

    /// <summary>Menyiapkan potongan berikutnya (pembuka, entri berikutnya, atau penutup). False kalau semuanya sudah habis.</summary>
    private bool Advance()
    {
        if (!_preambleWritten)
        {
            _preambleWritten = true;
            SetPending([.. BundleFormat.Magic, BundleFormat.Version]);
            return true;
        }

        if (_next < _sources.Count)
        {
            OpenNext();
            return true;
        }

        if (!_trailerWritten)
        {
            _trailerWritten = true;
            var trailer = new byte[5];
            trailer[0] = BundleFormat.EndTag;
            BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(1), (uint)_sources.Count);
            SetPending(trailer);
            return true;
        }

        return false;
    }

    private void OpenNext()
    {
        var source = _sources[_next++];
        var file = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        _file = file;
        _fileRemaining = file.Length;
        _fileName = source.EntryName;

        var nameBytes = Encoding.UTF8.GetBytes(source.EntryName);
        var header = new byte[1 + 2 + nameBytes.Length + 8];
        header[0] = BundleFormat.EntryTag;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(1), (ushort)nameBytes.Length);
        nameBytes.CopyTo(header.AsSpan(3));
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(3 + nameBytes.Length), (ulong)_fileRemaining);
        SetPending(header);
    }

    private void SetPending(byte[] bytes)
    {
        _pending = bytes;
        _pendingOffset = 0;
    }

    private void CloseFile()
    {
        _file?.Dispose();
        _file = null;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseFile();
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
