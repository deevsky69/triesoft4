using Triesoft.Core.KeyManagement;
using Xunit;

namespace Triesoft.Core.Tests.KeyManagement;

/// <summary>
/// DPAPI hanya tersedia di Windows. Test ini keluar lebih awal (lolos tanpa menguji apa pun)
/// di OS lain -- termasuk mesin dev macOS yang dipakai untuk menulis kode ini. Ini gap yang
/// diketahui: jalankan sekali di mesin/CI Windows sebelum fase ini dianggap tuntas untuk produksi.
/// </summary>
public class DpapiKeyProtectorTests
{
    [Fact]
    public void Protect_ThenUnprotect_RoundTrips_AndOutputDiffersFromPlaintext()
    {
        if (!OperatingSystem.IsWindows()) return;

        var protector = new DpapiKeyProtector();
        var plaintext = new byte[32];
        Random.Shared.NextBytes(plaintext);

        var protectedBytes = protector.Protect(plaintext);
        var unprotected = protector.Unprotect(protectedBytes);

        Assert.Equal(plaintext, unprotected);
        Assert.NotEqual(plaintext, protectedBytes);
    }
}
