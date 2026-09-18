namespace Triesoft.Core.Audit;

/// <summary>
/// Satu entri audit log yang sudah tersimpan, termasuk hash rantainya sendiri
/// (<see cref="Hash"/>) dan hash entri sebelumnya (<see cref="PreviousHash"/>) --
/// lihat <see cref="FileAuditLog"/> untuk bagaimana rantai ini dibangun &amp; diverifikasi.
/// </summary>
public sealed record StoredAuditEntry(
    DateTimeOffset Timestamp,
    string Actor,
    AuditAction Action,
    string Details,
    string PreviousHash,
    string Hash);
