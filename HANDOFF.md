# HANDOFF: Konteks Proyek TRIESOFT 4

Dokumen ini untuk sesi baru (manusia atau Claude) yang melanjutkan proyek dari komputer lain. Baca ini bersama `README.md`. README menjelaskan cara memakai aplikasi, dokumen ini menjelaskan **kenapa** kode dibuat seperti ini dan **apa yang belum selesai**.

## 1. Latar belakang

TRIESOFT adalah aplikasi enkripsi file untuk berita rahasia Polri, di bawah Bidsandi Baintelkam Polri. TRIESOFT 3 (versi lama):
- Kunci simetris dengan block cipher lama. Ada 4 pilihan algoritma dengan sampul Alpha, Beta, C, D.
- Bidsandi Mabes membangkitkan kunci baru **tiap bulan** dan mengirimnya ke sandi daerah/Polda.
- Fitur: login (user hanya didaftarkan dan diverifikasi oleh superadmin/Mabes), enkripsi, dekripsi, atur kunci, hapus kunci, setting.

Dugaan kelemahan TRIESOFT 3 yang mendasari desain baru (belum diverifikasi ke kodenya): kunci bulanan dipakai langsung untuk file (satu kunci bocor = semua file bulan itu bocor), tidak ada autentikasi integritas, distribusi kunci manual, RBAC datar, tidak ada audit trail.

Bahasa kerja dengan pemilik proyek: **Bahasa Indonesia**. Pesan error dan teks UI juga Bahasa Indonesia.

## 2. Status fase

| # | Fase | Status |
|---|---|---|
| 1 | Core crypto engine (envelope encryption, AES-256-GCM) | Selesai |
| 2 | Key management (penyimpanan dan siklus hidup kunci) | Selesai |
| 3 | UI + login/auth + RBAC | Selesai |
| 4 | Audit log tamper-evident | Selesai |
| 5 | Distribusi kunci Mabes ke Polda | **Belum** |
| 6 | Algoritma tambahan (Profile B/C/D) | **Belum** |

Tes: 58 di `Triesoft.Core.Tests`, 16 di `Triesoft.App.Tests`, semua hijau di macOS. Build 0 warning (kecuali 1 warning API usang di test screenshot).

## 3. Keputusan penting dan alasannya

**Platform**
- **Desktop, bukan web.** Sistem sandi idealnya bisa jalan offline (air-gapped), kunci harus bisa dihapus pasti dari memori (tidak bisa di JS/browser), dan akses smart card/HSM lebih matang di API native. Web menambah permukaan serangan.
- **.NET 10 (C#).** Awalnya rencana .NET 8, tapi SDK yang terpasang lewat brew adalah 10 (LTS juga).
- **Avalonia, bukan WPF.** Awalnya disepakati WPF. Diganti karena WPF hanya bisa dibangun di Windows, sedangkan pengembangan dilakukan di Mac. Avalonia memakai pola XAML/MVVM yang sangat mirip, tetap berjalan di Windows, dan memungkinkan render layar ke PNG untuk verifikasi visual.

**Kriptografi**
- **AES-256-GCM saja** untuk sekarang, memakai `System.Security.Cryptography.AesGcm` bawaan .NET tanpa dependensi luar. Tujuannya trusted computing base sekecil mungkin supaya mudah diaudit dan disertifikasi. `ChaCha20Poly1305` bawaan .NET **tidak didukung di Windows** (PlatformNotSupportedException), jadi menambahkannya butuh BouncyCastle. Itu sebabnya Profile B ditunda.
- **Envelope encryption.** DEK acak baru per file mengenkripsi isi dan nama file. DEK dibungkus (AES-GCM) oleh KEK (kunci bulanan). Nama file asli ikut dienkripsi.
- **Isi file dienkripsi per chunk** (default 1 MiB) supaya file besar tidak dimuat penuh ke memori, dan supaya pemotongan, penambahan, dan pengurutan ulang chunk terdeteksi.
- **Slot algoritma dicadangkan** di `AlgorithmProfile`: `0x01` AES-256-GCM (dipakai), `0x02` ChaCha20-Poly1305, `0x03` algoritma nasional BSSN, `0x04` post-quantum. Format file sudah siap, implementasinya belum.
- **PQC (ML-KEM/ML-DSA) belum perlu** untuk enkripsi file simetris. Baru relevan saat distribusi kunci memakai pertukaran kunci asimetris.

**Format file `.ts4`** (semua integer little-endian, kecuali counter nonce yang big-endian)
- Header: magic `TS4\0`, versi format (1), profile algoritma, KeyId (UTF-8, diawali panjang 2 byte), ChunkSize (4 byte), ContentBaseNonce (4 byte), WrapNonce (12), WrappedDek (32) + WrapTag (16), NameNonce (12), nama file terenkripsi (diawali panjang 2 byte) + NameTag (16).
- Tiap chunk: `[panjang 4 byte][flag isFinal 1 byte][ciphertext][tag 16 byte]`.
- Nonce chunk = 4 byte ContentBaseNonce + counter 8 byte (big-endian).
- AAD tiap chunk = seluruh byte header + counter + isFinal + panjang. Ini mencegah header dan chunk dari file berbeda dicampur, dan mencegah downgrade algoritma.
- Dekripsi menolak: tag salah, file terpotong (tidak ada chunk final), data tambahan setelah chunk final, ChunkSize tidak wajar (batas 64 MiB, mencegah header palsu memicu alokasi raksasa).
- KeyId tersimpan **plaintext** di header (sengaja), supaya `EnvelopeCipher.PeekKeyId` bisa mencari kunci yang tepat sebelum dekripsi.

**Key management**
- Tiga lapis: `MonthlyKeyManager` (kebijakan) > `IKeyStore`/`FileKeyStore` (penyimpanan) > `IKeyProtector` (perlindungan di disk).
- Status kunci: Active, Expired, Revoked, Purged. Hanya satu Active pada satu waktu. Impor kunci baru otomatis meng-Expire yang lama (kunci lama tetap bisa mendekripsi arsip). **Revoke dan Purge menghapus material kunci permanen.** Metadata tetap ada sebagai jejak.
- `DpapiKeyProtector` memakai scope **LocalMachine** (bukan CurrentUser), karena beberapa operator berbagi satu mesin Polda lewat login aplikasi, dan pemisahan akses ditegakkan oleh RBAC aplikasi. Ini melindungi dari disk/file yang dicuri, bukan dari user lain yang sudah login di mesin yang sama.
- Di non-Windows, `DevInsecurePassthroughProtector` (tanpa perlindungan) dipakai dengan peringatan. Kelas ini **sengaja ada di `Triesoft.App` dan `Triesoft.Cli`, tidak pernah di `Triesoft.Core`**, supaya library produksi tidak punya jalan pintas tidak aman.
- `MonthlyKeyManager.GetKeyForDecryption` menolak kunci Revoked dan Purged dengan pesan yang jelas.

**Auth dan RBAC**
- **2 peran saja: Admin dan Operator.** Riset awal menyarankan 4 (Superadmin, AdminDaerah, Operator, Auditor). Disederhanakan karena Superadmin (Mabes) dan AdminDaerah (Polda) baru berbeda secara fungsional setelah ada distribusi kunci multi-lokasi, dan Auditor baru berguna setelah ada audit log. Menambah peran nanti mudah (enum `UserRole`).
- Registrasi user: dibuat berstatus `PendingVerification`, lalu diverifikasi Admin (menyamai TRIESOFT 3). Akun Admin pertama dibuat lewat **First-Run Setup** (langsung aktif).
- Password: PBKDF2-HMAC-SHA256, 210.000 iterasi, salt 16 byte (bawaan .NET, tanpa dependensi).
- Login: 5 kali salah maka akun terkunci 15 menit. Status akun (Pending/Disabled) baru diungkap **setelah password terbukti benar**, supaya penebak acak tidak bisa mempelajari status akun.

**Audit log**
- File JSON Lines append-only (`audit.log`). Hash tiap entri = SHA-256 dari (hash sebelumnya, timestamp, aktor, aksi, detail). `VerifyChain()` mendeteksi entri yang diubah, disisipkan, atau dihapus.
- Yang dicatat: login sukses/gagal, daftar/verifikasi/nonaktifkan user, impor/cabut/hapus kunci, enkripsi/dekripsi (termasuk yang **gagal**, karena dekripsi gagal adalah sinyal investigasi penting).
- Log di-cache hash terakhirnya di memori, jadi `FileAuditLog` harus **singleton** per proses. Akses multi-proses ke file yang sama belum didukung (sama seperti `FileKeyStore` dan `FileUserStore`).
- Aksi audit dipasang di lapisan ViewModel (`Triesoft.App`), bukan di `Triesoft.Core`, karena di sanalah identitas pelaku (`SessionContext`) diketahui.

## 4. Jebakan teknis yang sudah ditemui

- Csproj template Avalonia **tidak mengaktifkan `ImplicitUsings`**. Tanpa itu banyak tipe dasar "tidak ditemukan".
- `CalendarDatePicker.SelectedDate` bertipe **`DateTime?`**, bukan `DateTimeOffset?`. Salah tipe membuat form impor kunci gagal diam-diam dengan `InvalidCastException` di UI. Ketahuan lewat screenshot.
- `TextBox.Watermark` sudah usang di Avalonia 12, pakai `PlaceholderText`.
- `System.Progress<T>` memanggil callback secara asinkron, jadi tidak cocok untuk assertion langsung di tes. Tes memakai `SyncProgress<T>` buatan sendiri.
- `Avalonia.Headless.XUnit` menarik **xUnit v3** dan bentrok dengan xUnit v2 di proyek ini. Solusinya memakai `Avalonia.Headless` saja dan mem-bootstrap manual (`HeadlessSetup`). Agar benar-benar merender bitmap, perlu `.UseSkia()` dan `UseHeadlessDrawing = false`. Tanpa itu `CaptureRenderedFrame()` mengembalikan null, dan tes harus meng-assert `NotNull` supaya tidak lolos diam-diam.
- Di Mac, `Path.GetTempPath()` bukan `/tmp` (biasanya `/var/folders/.../T/`). Screenshot ada di `triesoft4-screenshots` di folder itu.
- Lokasi data ditentukan `AppPaths`: default `%ProgramData%\Triesoft4` (machine-wide). Bisa dioverride dengan env var **`TRIESOFT4_DATA_DIR`**, dipakai untuk dev karena `CommonApplicationData` di macOS butuh sudo dan di Windows bisa butuh hak admin.
- Aturan CA2014: jangan `stackalloc` di dalam loop (bisa stack overflow untuk file besar). Sudah diperbaiki di `EnvelopeCipher`.

## 5. Celah verifikasi yang masih terbuka

1. **`DpapiKeyProtectorTests` belum pernah benar-benar berjalan.** Tes itu keluar lebih awal di non-Windows. Jalankan `dotnet test` di Windows sekali. Ini celah utama fase key management.
2. **Aplikasi belum pernah dijalankan sebagai jendela sungguhan.** Semua verifikasi visual lewat render headless. Coba `dotnet run --project src/Triesoft.App` dan nilai rasa pakainya (alur, ukuran font, kecepatan).
3. **Uji dengan file besar** (ratusan MB) di aplikasi belum dilakukan. Uji round-trip terbesar di tes adalah 5 MB.
4. Tidak ada uji integrasi antar-mesin.

## 6. Pertanyaan terbuka (butuh keputusan pemilik proyek)

- **BSSN Perka No. 11 Tahun 2024** (Penyelenggaraan Algoritma Kriptografi Indonesia dan Penilaian Kesesuaian Keamanan Modul Kriptografi): apakah TRIESOFT 4 termasuk yang **wajib** memakai "Algoritma Kriptografi Indonesia" dan sertifikasi modul (SNI ISO/IEC 19790:2015, Common Criteria Indonesia)? Jawabannya menentukan apakah AES-256-GCM boleh jadi algoritma utama atau harus di belakang algoritma nasional (Profile C). **Harus dikonfirmasi ke BSSN/Bidsandi Mabes sebelum dipakai resmi.**
- Bagaimana kunci bulanan sebenarnya dikirim Mabes ke Polda hari ini, dan apakah perlu tetap manual (offline) atau boleh digital.
- Apakah perlu smart card/HSM (PKCS#11) untuk penyimpanan kunci dan login. Kalau ya, cukup tambah implementasi `IKeyProtector` baru.

## 7. Langkah berikutnya yang disarankan

1. Di Windows: `dotnet test`, jalankan aplikasi, catat masalah tampilan atau alur.
2. **Distribusi kunci Mabes ke Polda**: paket kunci terenkripsi per-Polda (KEK dibungkus dengan public key Polda: RSA-OAEP-4096 atau ECDH P-384, semuanya bawaan .NET). Pertimbangkan hybrid dengan ML-KEM. Perlu memutuskan dulu bagaimana public key Polda didaftarkan (masalah bootstrap). Baru di fase ini peran Superadmin vs AdminDaerah punya makna fungsional.
3. Tambahkan peran **Auditor** (hanya bisa melihat audit log) kalau dibutuhkan pemisahan tugas.
4. Profile B (ChaCha20-Poly1305 via BouncyCastle) dan persiapan Profile C (algoritma nasional) setelah ada jawaban dari BSSN.
5. Untuk pemakaian resmi: audit keamanan pihak ketiga, code signing, installer.

## 8. Cara kerja yang dipakai di proyek ini

- Tiap fase besar: rancang dulu (plan mode) dan minta persetujuan, lalu implementasi, tes, dan verifikasi, baru commit.
- Verifikasi visual UI: jalankan `dotnet test tests/Triesoft.App.Tests --filter "FullyQualifiedName~ScreenshotTests"`, lalu buka PNG di folder `triesoft4-screenshots`.
- Pesan commit diakhiri baris `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`. Jangan commit atau push tanpa diminta pemilik proyek.
- Riwayat repo: dikembangkan di Mac dalam repo git yang berakar di home directory (remote lain, `blumtod`), lalu riwayat khusus folder proyek diekstrak dengan `git subtree split` dan di-push ke `github.com/deevsky69/triesoft4` (branch `main`). Dari clone baru di Windows, `git push` biasa sudah cukup.

## 9. Struktur proyek

```
src/Triesoft.Core/    Crypto/ KeyManagement/ Identity/ Audit/
src/Triesoft.App/     ViewModels/ Views/ Theme/ Services/ Converters/
src/Triesoft.Cli/     Alat uji baris perintah (bukan produk akhir)
tests/Triesoft.Core.Tests/
tests/Triesoft.App.Tests/   (termasuk VisualTests/ScreenshotTests)
```
