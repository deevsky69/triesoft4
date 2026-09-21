namespace Triesoft.App.Services;

/// <summary>
/// Direktori penyimpanan data aplikasi. Default-nya mengikuti semantik "machine-wide" yang benar
/// untuk deployment produksi (sejalan dengan <c>DataProtectionScope.LocalMachine</c> di
/// <c>DpapiKeyProtector</c>). Bisa dioverride lewat env var <c>TRIESOFT4_DATA_DIR</c> -- dipakai
/// untuk development/testing di mesin ini, karena <c>CommonApplicationData</c> di macOS
/// (<c>/usr/share</c>) butuh sudo untuk ditulis.
/// </summary>
public static class AppPaths
{
    private const string EnvOverrideVariable = "TRIESOFT4_DATA_DIR";

    public static string DataRoot =>
        Environment.GetEnvironmentVariable(EnvOverrideVariable)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Triesoft4");

    public static string KeyStoreDirectory => Path.Combine(DataRoot, "Keys");
    public static string DistributionDirectory => Path.Combine(DataRoot, "Distribution");
    public static string UserStoreDirectory => Path.Combine(DataRoot, "Users");
    public static string AuditLogDirectory => Path.Combine(DataRoot, "Audit");
}
