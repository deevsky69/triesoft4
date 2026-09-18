namespace Triesoft.Core.Audit;

/// <summary>
/// Log tamper-evident untuk aksi sensitif (login, kelola user, kelola kunci, enkripsi/dekripsi) --
/// tiap entri diikat lewat hash rantai ke entri sebelumnya, sehingga penghapusan/penyisipan/
/// perubahan entri lama selalu terdeteksi lewat <see cref="VerifyChain"/>.
/// </summary>
public interface IAuditLog
{
    void Record(string actor, AuditAction action, string details);

    IReadOnlyList<StoredAuditEntry> ReadAll();

    ChainVerificationResult VerifyChain();
}
