using System.Text;

namespace Triesoft.Core.Bundle;

/// <summary>
/// Aturan nama untuk bundle: nama entri di dalam bundle dan nama folder hasil ekstraksi. Aturannya sengaja ketat dan
/// sama di semua OS, karena nama entri datang dari file terdekripsi dan dipakai untuk menulis ke disk:
/// hanya nama file datar (tanpa pemisah path, tanpa ".."), tanpa karakter terlarang Windows, dan bukan nama perangkat
/// Windows (CON, NUL, COM1, ...) yang bisa membuka perangkat, bukan file.
/// </summary>
public static class BundleNames
{
    /// <summary>Ekstensi nama asli yang menandai isi .ts4 sebagai bundle. Nama asli ini terenkripsi dan terautentikasi di header.</summary>
    public const string Extension = ".ts4bundle";

    public const int MaxEntryNameChars = 255;

    private const string ForbiddenChars = "/\\:*?\"<>|";

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsBundleFileName(string fileName) => fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Mengembalikan alasan penolakan, atau null kalau nama entri aman dipakai.</summary>
    public static string? ValidateEntryName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "nama kosong";
        if (name.Length > MaxEntryNameChars) return "nama terlalu panjang";
        if (name is "." or "..") return "nama tidak boleh titik";
        if (name.Any(c => c < ' ' || ForbiddenChars.Contains(c))) return "mengandung karakter terlarang atau pemisah path";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "tidak boleh diakhiri titik atau spasi";
        if (IsReservedDeviceName(name)) return "nama perangkat sistem yang dicadangkan";
        return null;
    }

    /// <summary>Mengubah nama file apa pun menjadi nama entri yang valid (karakter terlarang jadi "_").</summary>
    public static string Sanitize(string fileName)
    {
        var sb = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
            sb.Append(c < ' ' || ForbiddenChars.Contains(c) ? '_' : c);

        var name = sb.ToString().TrimEnd('.', ' ');
        if (name.Length > MaxEntryNameChars) name = name[..MaxEntryNameChars].TrimEnd('.', ' ');
        if (name.Length == 0) name = "file";
        if (IsReservedDeviceName(name)) name = "_" + name;
        return name;
    }

    /// <summary>Nama entri yang unik (tidak peka huruf besar/kecil): "a.txt", "a (2).txt", "a (3).txt", ...</summary>
    public static IReadOnlyList<string> MakeUnique(IEnumerable<string> names)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var name in names)
        {
            var candidate = name;
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            for (var i = 2; !used.Add(candidate); i++)
                candidate = $"{stem} ({i}){ext}";
            result.Add(candidate);
        }
        return result;
    }

    /// <summary>Nama folder hasil ekstraksi dari nama asli bundle ("laporan.ts4bundle" -> "laporan").</summary>
    public static string FolderNameFor(string bundleFileName)
    {
        var name = IsBundleFileName(bundleFileName) ? bundleFileName[..^Extension.Length] : bundleFileName;
        var sanitized = Sanitize(name);
        return sanitized == "file" && name.Trim().Length == 0 ? "bundle" : sanitized;
    }

    private static bool IsReservedDeviceName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd(' ');
        return ReservedDeviceNames.Contains(stem);
    }
}
