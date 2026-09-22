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

    /// <summary>Panjang maksimum satu komponen nama (satu nama file atau satu nama folder).</summary>
    public const int MaxEntryNameChars = 255;

    /// <summary>Batas path lengkap di dalam bundle (bundle versi 2 mengenal subfolder, dipisah "/").</summary>
    public const int MaxPathChars = 1024;

    /// <summary>Kedalaman maksimum folder di dalam bundle (jumlah komponen path, termasuk nama file).</summary>
    public const int MaxDepth = 32;

    public const int MaxEntries = 100_000;

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

    /// <summary>
    /// Mengembalikan alasan penolakan, atau null kalau path entri bundle aman: komponen dipisah "/" (hanya "/", bukan "\"),
    /// tiap komponen lolos <see cref="ValidateEntryName"/>, tidak ada komponen kosong (jadi tidak ada "/" di awal, di akhir,
    /// atau ganda), ".." ditolak oleh aturan komponen, dan panjang serta kedalaman dibatasi.
    /// </summary>
    public static string? ValidateEntryPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "nama kosong";
        if (path.Length > MaxPathChars) return "path terlalu panjang";

        var segments = path.Split('/');
        if (segments.Length > MaxDepth) return "folder terlalu dalam";
        foreach (var segment in segments)
        {
            if (ValidateEntryName(segment) is { } problem)
                return segments.Length == 1 ? problem : $"komponen '{segment}': {problem}";
        }
        return null;
    }

    /// <summary>
    /// Menentukan path tiap file di dalam bundle. File tunggal ditaruh di akar bundle dengan nama filenya; file dari sebuah folder
    /// dikelompokkan di bawah satu folder induk dengan struktur relatifnya. Bentrok nama di akar (dua folder bernama sama dari
    /// tempat berbeda, atau file dan folder bernama sama) diselesaikan dengan akhiran " (2)", " (3)", ... tanpa
    /// menggabungkan folder yang berbeda. Hasilnya berurutan sama dengan <paramref name="items"/>.
    /// </summary>
    public static IReadOnlyList<string> PlanEntryPaths(IReadOnlyList<BundlePlanItem> items)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupRoots = new Dictionary<string, string>();
        var result = new List<string>(items.Count);

        foreach (var item in items)
        {
            if (item.GroupId is { } groupId)
            {
                if (!groupRoots.TryGetValue(groupId, out var root))
                {
                    var wanted = Sanitize(item.GroupName ?? "folder");
                    root = wanted;
                    for (var i = 2; !used.Add(root); i++)
                        root = $"{wanted} ({i})";
                    groupRoots[groupId] = root;
                }
                result.Add(root + "/" + (item.RelativePath ?? Sanitize(item.FileName)));
            }
            else
            {
                var wanted = Sanitize(item.FileName);
                var stem = Path.GetFileNameWithoutExtension(wanted);
                var ext = Path.GetExtension(wanted);
                var name = wanted;
                for (var i = 2; !used.Add(name); i++)
                    name = $"{stem} ({i}){ext}";
                result.Add(name);
            }
        }
        return result;
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

/// <summary>Satu file di daftar, untuk menentukan namanya di dalam bundle. Lihat <see cref="BundleNames.PlanEntryPaths"/>.</summary>
/// <param name="GroupId">Penanda folder asal kalau file ini ditambahkan lewat folder; null kalau dipilih sebagai file tunggal.</param>
/// <param name="GroupName">Nama folder induk (komponen pertama path di bundle).</param>
/// <param name="RelativePath">Path relatif terhadap folder induk, dipisah "/", tiap komponen sudah valid.</param>
/// <param name="FileName">Nama file asli, dipakai untuk file tunggal.</param>
public sealed record BundlePlanItem(string? GroupId, string? GroupName, string? RelativePath, string FileName);
