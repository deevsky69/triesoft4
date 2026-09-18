using Triesoft.Core.Audit;
using Xunit;

namespace Triesoft.Core.Tests.Audit;

public class FileAuditLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("triesoft4-auditlog-test-").FullName;

    private FileAuditLog NewLog() => new(_dir);

    [Fact]
    public void Record_ThenReadAll_ReturnsEntriesInOrder()
    {
        var log = NewLog();
        log.Record("admin1", AuditAction.LoginSucceeded, "");
        log.Record("admin1", AuditAction.KeyImported, "keyId=2026-09-TEST");

        var entries = log.ReadAll();

        Assert.Equal(2, entries.Count);
        Assert.Equal(AuditAction.LoginSucceeded, entries[0].Action);
        Assert.Equal(AuditAction.KeyImported, entries[1].Action);
    }

    [Fact]
    public void Record_ChainsHashesTogether()
    {
        var log = NewLog();
        log.Record("admin1", AuditAction.LoginSucceeded, "");
        log.Record("admin1", AuditAction.KeyImported, "keyId=2026-09-TEST");
        log.Record("admin1", AuditAction.KeyRevoked, "keyId=2026-09-TEST");

        var entries = log.ReadAll();

        Assert.Equal(new string('0', 64), entries[0].PreviousHash);
        Assert.Equal(entries[0].Hash, entries[1].PreviousHash);
        Assert.Equal(entries[1].Hash, entries[2].PreviousHash);
    }

    [Fact]
    public void VerifyChain_UntamperedLog_ReportsIntact()
    {
        var log = NewLog();
        for (var i = 0; i < 5; i++)
            log.Record("admin1", AuditAction.FileEncrypted, $"file=dokumen-{i}.pdf");

        var result = log.VerifyChain();

        Assert.True(result.IsIntact);
        Assert.Null(result.FailureIndex);
    }

    [Fact]
    public void VerifyChain_EmptyLog_ReportsIntact()
    {
        var log = NewLog();
        var result = log.VerifyChain();
        Assert.True(result.IsIntact);
    }

    [Fact]
    public void VerifyChain_TamperedEntry_DetectsBreak()
    {
        var log = NewLog();
        log.Record("admin1", AuditAction.LoginSucceeded, "");
        log.Record("admin1", AuditAction.KeyImported, "keyId=2026-09-TEST");
        log.Record("op1", AuditAction.FileEncrypted, "file=laporan.pdf");

        // Rusak baris kedua langsung di disk (di luar API FileAuditLog) -- simulasi file diedit manual.
        var path = Path.Combine(_dir, "audit.log");
        var lines = File.ReadAllLines(path);
        lines[1] = lines[1].Replace("keyId=2026-09-TEST", "keyId=DIUBAH-OLEH-PENYERANG");
        File.WriteAllLines(path, lines);

        // Instance baru supaya tidak memakai cache in-memory dari instance yang menulis di atas.
        var freshLog = NewLog();
        var result = freshLog.VerifyChain();

        Assert.False(result.IsIntact);
        Assert.Equal(1, result.FailureIndex);
    }

    [Fact]
    public void VerifyChain_DeletedEntryInMiddle_DetectsBreak()
    {
        var log = NewLog();
        log.Record("admin1", AuditAction.LoginSucceeded, "");
        log.Record("admin1", AuditAction.KeyImported, "keyId=2026-09-TEST");
        log.Record("op1", AuditAction.FileEncrypted, "file=laporan.pdf");

        var path = Path.Combine(_dir, "audit.log");
        var lines = File.ReadAllLines(path);
        File.WriteAllLines(path, [lines[0], lines[2]]); // hapus baris tengah

        var freshLog = NewLog();
        var result = freshLog.VerifyChain();

        Assert.False(result.IsIntact);
        Assert.Equal(1, result.FailureIndex);
    }

    [Fact]
    public void Record_PersistsAcrossNewInstance_ChainContinuesCorrectly()
    {
        var log1 = NewLog();
        log1.Record("admin1", AuditAction.LoginSucceeded, "");

        var log2 = NewLog(); // simulasi restart aplikasi, menunjuk direktori yang sama
        log2.Record("admin1", AuditAction.KeyImported, "keyId=2026-09-TEST");

        var entries = log2.ReadAll();
        Assert.Equal(2, entries.Count);
        Assert.Equal(entries[0].Hash, entries[1].PreviousHash);

        Assert.True(log2.VerifyChain().IsIntact);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
