namespace Triesoft.Core.Identity;

/// <summary>
/// Dua peran untuk fase ini. Superadmin (Mabes) vs Admin Daerah (Polda) baru punya perbedaan
/// fungsional nyata setelah ada distribusi kunci multi-lokasi -- untuk satu instalasi lokal,
/// keduanya butuh hak akses yang identik, jadi disatukan jadi <see cref="Admin"/>.
/// </summary>
public enum UserRole
{
    /// <summary>Kelola user &amp; kunci lokal, plus enkripsi/dekripsi.</summary>
    Admin,

    /// <summary>Hanya enkripsi/dekripsi.</summary>
    Operator,
}
