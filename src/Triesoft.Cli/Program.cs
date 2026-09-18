using System.Globalization;
using Triesoft.Core.Crypto;
using Triesoft.Core.KeyManagement;

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "encrypt" => RunEncrypt(rest),
        "decrypt" => RunDecrypt(rest),
        "key" => RunKey(rest),
        _ => Unknown(),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Gagal: {ex.Message}");
    return 1;
}

int Unknown()
{
    PrintUsage();
    return 1;
}

int RunEncrypt(string[] cliArgs)
{
    var opts = ParseOptions(cliArgs);
    var inputPath = opts.Positional
        ?? throw new ArgumentException("Path file input wajib diisi.");
    var keyId = opts.Options.GetValueOrDefault("key-id")
        ?? throw new ArgumentException("--key-id wajib diisi.");
    var keyHex = opts.Options.GetValueOrDefault("key")
        ?? throw new ArgumentException("--key (hex 64 karakter / 32 byte) wajib diisi.");
    var outputPath = opts.Options.GetValueOrDefault("out") ?? inputPath + ".ts4";

    var keyBytes = Convert.FromHexString(keyHex);
    using var kek = MonthlyKey.FromBytes(keyId, keyBytes);

    using var input = File.OpenRead(inputPath);
    using var output = File.Create(outputPath);
    EnvelopeCipher.Encrypt(input, output, kek, Path.GetFileName(inputPath));

    Console.WriteLine($"OK: {inputPath} -> {outputPath} (KeyId: {keyId})");
    return 0;
}

int RunDecrypt(string[] cliArgs)
{
    var opts = ParseOptions(cliArgs);
    var inputPath = opts.Positional
        ?? throw new ArgumentException("Path file .ts4 wajib diisi.");
    var keyId = opts.Options.GetValueOrDefault("key-id")
        ?? throw new ArgumentException("--key-id wajib diisi.");
    var keyHex = opts.Options.GetValueOrDefault("key")
        ?? throw new ArgumentException("--key (hex 64 karakter / 32 byte) wajib diisi.");

    var keyBytes = Convert.FromHexString(keyHex);
    using var kek = MonthlyKey.FromBytes(keyId, keyBytes);

    using var input = File.OpenRead(inputPath);

    // Dekripsi ke file sementara dulu -- jangan tulis ke tujuan akhir sebelum
    // seluruh file terverifikasi (mencegah hasil parsial tersisa kalau gagal di tengah).
    var tempPath = Path.GetTempFileName();
    DecryptResult result;
    try
    {
        using (var tempOutput = File.Create(tempPath))
        {
            result = EnvelopeCipher.Decrypt(input, tempOutput, kek);
        }

        var outputPath = opts.Options.GetValueOrDefault("out") ?? result.OriginalFileName;
        File.Move(tempPath, outputPath, overwrite: true);
        Console.WriteLine($"OK: {inputPath} -> {outputPath} (nama asli: {result.OriginalFileName}, KeyId: {result.KeyId})");
    }
    finally
    {
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }

    return 0;
}

int RunKey(string[] cliArgs)
{
    if (cliArgs.Length < 1)
    {
        PrintUsage();
        return 1;
    }

    var subcommand = cliArgs[0].ToLowerInvariant();
    var subRest = cliArgs.Skip(1).ToArray();

    return subcommand switch
    {
        "import" => RunKeyImport(subRest),
        "list" => RunKeyList(subRest),
        "revoke" => RunKeyRevoke(subRest),
        "purge" => RunKeyPurge(subRest),
        _ => Unknown(),
    };
}

int RunKeyImport(string[] cliArgs)
{
    var opts = ParseOptions(cliArgs);
    var manager = GetKeyManager(opts);

    var keyId = RequireOption(opts, "key-id");
    var keyHex = RequireOption(opts, "key");
    var validFrom = ParseDate(RequireOption(opts, "valid-from"));
    var validUntil = ParseDate(RequireOption(opts, "valid-until"));

    var keyBytes = Convert.FromHexString(keyHex);
    manager.ImportMonthlyKey(keyId, keyBytes, validFrom, validUntil);

    Console.WriteLine($"OK: kunci '{keyId}' diimpor sebagai Active (berlaku {validFrom:yyyy-MM-dd} s/d {validUntil:yyyy-MM-dd}).");
    return 0;
}

int RunKeyList(string[] cliArgs)
{
    var opts = ParseOptions(cliArgs);
    var manager = GetKeyManager(opts);

    var keys = manager.ListKeys();
    if (keys.Count == 0)
    {
        Console.WriteLine("(belum ada kunci di store ini)");
        return 0;
    }

    foreach (var k in keys.OrderBy(k => k.ImportedAt))
    {
        var reason = k.StatusReason is { } r ? $" ({r})" : "";
        Console.WriteLine($"{k.KeyId,-24} {k.Status,-8} valid {k.ValidFrom:yyyy-MM-dd}..{k.ValidUntil:yyyy-MM-dd}{reason}");
    }
    return 0;
}

int RunKeyRevoke(string[] cliArgs)
{
    var opts = ParseOptions(cliArgs);
    var manager = GetKeyManager(opts);

    var keyId = RequireOption(opts, "key-id");
    var reason = RequireOption(opts, "reason");

    manager.RevokeKey(keyId, reason);
    Console.WriteLine($"OK: kunci '{keyId}' dicabut (revoked). Material sudah dihapus permanen.");
    return 0;
}

int RunKeyPurge(string[] cliArgs)
{
    var opts = ParseOptions(cliArgs);
    var manager = GetKeyManager(opts);

    var keyId = RequireOption(opts, "key-id");

    manager.PurgeExpiredKey(keyId);
    Console.WriteLine($"OK: kunci '{keyId}' di-purge (dihapus permanen).");
    return 0;
}

MonthlyKeyManager GetKeyManager((string? Positional, Dictionary<string, string> Options) opts)
{
    var storeDir = RequireOption(opts, "store");
    var store = new FileKeyStore(storeDir, CreateProtector());
    return new MonthlyKeyManager(store);
}

IKeyProtector CreateProtector()
{
    if (OperatingSystem.IsWindows())
        return new DpapiKeyProtector();

    Console.Error.WriteLine(
        "PERINGATAN: berjalan di non-Windows -- memakai protector passthrough yang TIDAK AMAN, " +
        "hanya untuk pengembangan lokal. Jangan pernah dipakai di lingkungan produksi.");
    return new DevInsecurePassthroughProtector();
}

string RequireOption((string? Positional, Dictionary<string, string> Options) opts, string name) =>
    opts.Options.GetValueOrDefault(name) ?? throw new ArgumentException($"--{name} wajib diisi.");

DateTimeOffset ParseDate(string value) =>
    DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

static (string? Positional, Dictionary<string, string> Options) ParseOptions(string[] cliArgs)
{
    string? positional = null;
    var options = new Dictionary<string, string>();

    for (var i = 0; i < cliArgs.Length; i++)
    {
        var arg = cliArgs[i];
        if (arg.StartsWith("--"))
        {
            var name = arg[2..];
            if (i + 1 >= cliArgs.Length)
                throw new ArgumentException($"Opsi --{name} butuh nilai.");
            options[name] = cliArgs[++i];
        }
        else
        {
            positional ??= arg;
        }
    }

    return (positional, options);
}

static void PrintUsage()
{
    Console.WriteLine("""
        Triesoft.Cli — dev harness untuk core crypto engine TRIESOFT 4 (BUKAN produk akhir).

        Penggunaan:
          triesoft-cli encrypt <file> --key-id <id> --key <64-hex-char> [--out <file.ts4>]
          triesoft-cli decrypt <file.ts4> --key-id <id> --key <64-hex-char> [--out <file>]

          triesoft-cli key import --store <dir> --key-id <id> --key <64-hex-char> --valid-from <ISO8601> --valid-until <ISO8601>
          triesoft-cli key list   --store <dir>
          triesoft-cli key revoke --store <dir> --key-id <id> --reason <teks>
          triesoft-cli key purge  --store <dir> --key-id <id>

        Catatan: perlindungan kunci di store memakai Windows DPAPI. Di luar Windows (mis. saat
        development di macOS/Linux), CLI ini otomatis jatuh ke protector passthrough TIDAK AMAN
        khusus pengembangan lokal, dengan peringatan di stderr setiap dipakai.
        """);
}

/// <summary>
/// PERINGATAN: protector palsu yang TIDAK melindungi apa pun -- byte kunci ditulis apa adanya.
/// Hanya ada di Triesoft.Cli (dev harness), TIDAK PERNAH di Triesoft.Core, supaya library
/// produksi tidak punya jalan pintas tidak aman. Dipakai semata supaya alur key management bisa
/// diuji end-to-end di mesin non-Windows (DPAPI tidak tersedia di luar Windows).
/// </summary>
sealed class DevInsecurePassthroughProtector : IKeyProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();
    public byte[] Unprotect(ReadOnlySpan<byte> protectedBytes) => protectedBytes.ToArray();
}
