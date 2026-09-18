using Triesoft.Core.Crypto;

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

        Contoh:
          triesoft-cli encrypt laporan.pdf --key-id 2026-09-TEST --key 00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff
        """);
}
