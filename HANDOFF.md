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
| 5 | Distribusi kunci Mabes ke Polda | Selesai (lihat bagian "Distribusi kunci") |
| 6 | Algoritma tambahan (Profile B/C/D) | **Belum** |

Tes: 174 di `Triesoft.Core.Tests`, 78 di `Triesoft.App.Tests`, semua hijau di Windows (termasuk `DpapiKeyProtectorTests`, yang sebelumnya hanya dilewati di macOS). Build 0 error. Peringatan yang tersisa hanya di proyek tes screenshot: 1 API usang (`Bitmap.Save`) dan 2 xUnit1031 yang disengaja (`Task.Run(...).GetAwaiter().GetResult()` sebagai jalan keluar deadlock thread UI headless).

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

**Distribusi kunci (`Triesoft.Core/KeyDistribution`)**
- Asumsi yang dipakai karena pertanyaan terbuka di bagian 6 belum dijawab: distribusi digital (paket file), pendaftaran kunci publik lewat file `.ts4pub` + **verifikasi sidik jari manual di jalur terpisah**, peran tetap Admin/Operator (tidak ada Superadmin/AdminDaerah). Semua ini mudah diubah.
- Kriptografi, semuanya bawaan .NET: ECDH P-384 efemeral, lalu HKDF-SHA384, lalu AES-256-GCM untuk membungkus kunci bulanan. Seluruh paket ditandatangani ECDSA P-384 (SHA-384) oleh Mabes. Header paket (KeyId, masa berlaku, sidik jari penerbit dan penerima, kunci efemeral) jadi AAD dan info HKDF, jadi mengubah metadata membuat pembukaan gagal.
- Urutan pemeriksaan saat impor sengaja: penerbit dikenal, lalu tanda tangan valid, lalu paket untuk mesin ini, lalu dekripsi. Paket palsu ditolak sebelum kunci privat penerima dipakai. Kunci publik selain P-384 ditolak.
- `FileDistributionStore` menyimpan kunci privat penerima (`recipient.key`) dan penerbit (`issuer.key`) terproteksi `IKeyProtector`. Kunci privat dibuat di mesin itu dan tidak pernah diekspor. Mesin menjadi "penerbit" (Mabes) hanya setelah tombol "Aktifkan sebagai Penerbit". Penerbit tepercaya (pinned) dan daftar penerima juga disimpan di sini.
- `KeyDistributionManager`: `IssueMonthlyKey` (Mabes membangkitkan kunci acak, satu paket per Polda, opsional menyimpan salinan lokal, kunci di memori di-zeroize) dan `ImportPackage` (Polda memverifikasi lalu memanggil `MonthlyKeyManager.ImportMonthlyKey`).
- Layar **Distribusi Kunci** (khusus Admin) dan aksi audit baru: `IssuerIdentityCreated`, `IssuerTrusted`, `RecipientRegistered`, `RecipientRemoved`, `KeyPackageIssued`, `KeyPackageImported`, `KeyPackageImportFailed`.
- **Rotasi dan pencabutan kunci identitas** (`FileDistributionStore`): `RotateRecipientKey` (Polda), `RotateIssuerKey` (Mabes), `RevokeTrustedIssuer` (Polda), `RevokeRecipient` (Mabes). Semuanya wajib beralasan, dicatat di audit (`RecipientKeyRotated`, `IssuerKeyRotated`, `IssuerRevoked`, `RecipientRevoked`), dan di UI didahului dialog konfirmasi. Rotasi menulis kunci baru ke `*.key.new`, menimpa file kunci privat lama dengan byte acak, lalu menukarnya (upaya terbaik; salinan lama di SSD/OneDrive bisa tersisa).
- **`revoked.json` bersifat final:** sidik jari yang tercatat (dicabut, atau kunci lama hasil rotasi) ditolak oleh `AddRecipient` dan `SetTrustedIssuer`, dan tidak ada fitur "batalkan pencabutan". Salah cabut dipulihkan dengan rotasi kunci pihak itu dan mendaftar ulang. Mengganti penerbit tepercaya lewat impor kunci lain **tidak** otomatis mencabut yang lama (supaya salah impor bisa dikoreksi).
- **Batasan yang disengaja:** paket yang sudah terbit tidak bisa ditarik. Kalau kunci privat Polda bocor, cabut juga kunci bulanan terkait di Kelola Kunci (kalau penyerang punya paketnya, ia bisa membuka KEK-nya).
- **Belum ada:** hybrid ML-KEM. Dialog file (ekspor/impor, pilih folder) belum diuji manual, hanya lewat tes ViewModel.
- **Jebakan `AuditAction`:** `audit.log` menyimpan aksi sebagai **angka**, sedangkan hash rantai memakai **nama** enum. Nilai numerik enum sekarang ditulis eksplisit dan dikunci oleh `AuditActionStabilityTests`. Jangan menyisipkan atau mengurutkan ulang anggota; tambahkan di akhir. Commit `a49a51b` sempat menyisipkan anggota baru di tengah dan menggeser `FileEncrypted` dan sesudahnya, sehingga entri lama terbaca sebagai aksi lain dan verifikasi rantai akan gagal. Sudah diperbaiki; log asli developer diverifikasi utuh setelahnya. Kalau ada mesin lain yang sempat memakai fitur distribusi kunci dengan `a49a51b`, entri distribusinya di log mesin itu (angka 8-14) akan terbaca salah.

**Enkripsi/dekripsi massal (`Triesoft.App/ViewModels/BatchFileViewModelBase`)**
- Permintaan pemilik proyek: banyak file sekaligus dengan tampilan daftar. Diartikan sebagai **batch per file** (tiap file tetap satu `.ts4` sendiri, format file tidak berubah), bukan satu arsip gabungan. Kalau yang dimaksud "bundle" adalah satu file `.ts4` berisi banyak file, itu butuh format arsip baru dan belum dikerjakan.
- `EncryptViewModel` dan `DecryptViewModel` menurunkan basis yang sama: daftar `BatchFileItem` (status per file), diproses berurutan, satu gagal tidak menghentikan yang lain, `Batal` berhenti setelah file berjalan selesai, menjalankan ulang hanya memproses yang belum berhasil. Tiap file diaudit sendiri (sukses dan gagal). Kunci dimuat per file.
- Perbaikan yang ikut masuk: (1) dekripsi memakai file sementara dan **menghapus plaintext parsial** kalau gagal di tengah (sebelumnya tertinggal saat tamper terdeteksi); (2) enkripsi menghapus `.ts4` setengah jadi, tapi hanya kalau memang dibuat oleh proses itu; (3) **nama file asli dari header dipersempit ke nama file saja**, jadi header berisi `..\x` atau path absolut tidak bisa menulis keluar dari folder; (4) dua hasil dengan nama sama dalam satu proses tidak saling menimpa (`nama (2).ext`). File yang sudah ada sebelum proses tetap ditimpa seperti perilaku lama.
- **Drag and drop** dari File Explorer ke kartu enkripsi/dekripsi (`Views/FileDropTarget`, API `DragEventArgs.DataTransfer.TryGetFiles()` milik Avalonia 12). File dan folder diterima; logika penyaringan ada di `BatchFileViewModelBase.AddDropped` (teruji): folder ditambahkan isinya (dengan subfolder kalau `IncludeSubfolders` aktif), dekripsi hanya menerima `.ts4`, item yang dilewati dilaporkan di pesan. Drop ditolak selama proses berjalan. Kartu disorot lewat class `drop-active`. **Event drop-nya sendiri belum pernah diuji dengan seret sungguhan** (headless tidak mensimulasikan drag dari OS); hanya logika VM dan gaya sorotan yang teruji.
- Belum ada: penanganan kunci aktif yang berganti di tengah batch (tiap file memakai kunci yang aktif saat file itu diproses).

**Bundle: banyak file menjadi SATU `.ts4` (`Triesoft.Core/Bundle`, mode di `EncryptViewModel`)**
- **Format `.ts4` tidak berubah.** Bundle adalah aliran byte tersendiri yang dienkripsi lewat `EnvelopeCipher` seperti file biasa: `"TS4B" 0x01`, lalu per entri `0x01 [u16 panjangNama][nama UTF-8][u64 ukuran][isi]`, ditutup `0x00 [u32 jumlahEntri]` (detail di `BundleFormat`). Yang menandai isi sebagai bundle adalah **nama asli** `<nama>.ts4bundle` di header (terenkripsi dan terautentikasi). Versi lama yang mendekripsi bundle akan menghasilkan satu file `*.ts4bundle` berisi aliran mentah.
- **Enkripsi streaming:** `BundleReadStream` membangkitkan aliran langsung dari file sumber (tanpa arsip sementara di disk, tanpa memuat ke memori) dan diberikan sebagai input `EnvelopeCipher.Encrypt`. Uji sekali pakai: dua file 350 MB total, enkripsi 3,8 dtk, ekstraksi 1 dtk, hash cocok, memori puncak naik ~22 MB. `CanSeek` sengaja true hanya supaya progres bisa dihitung (`Length`/`Position`); Seek sebenarnya melempar exception.
- **Dekripsi:** selalu ke file sementara dulu (seperti file biasa). Kalau nama asli berakhiran `.ts4bundle` **dan** isinya diawali `TS4B`, baru diekstrak (`BundleExtractor`) ke folder staging lalu dipindah ke folder bernama bundle (`nama`, `nama (2)`, ... tidak pernah menggabung ke folder yang ada). Jadi ekstraksi hanya terjadi **setelah seluruh file lolos autentikasi**. Gagal di tengah membuang staging seluruhnya. Kalau bernama `.ts4bundle` tapi bukan bundle, dikembalikan sebagai file biasa (data tidak hilang).
- **Bundle atomik:** satu file tidak terbaca berarti tidak ada bundle sama sekali (pemeriksaan awal, dan `IOException` kalau file menyusut saat dibaca). Bundle ditulis ke `*.partial` lalu dipindah; nama tidak pernah menimpa (`nama (2).ts4`). Pembatalan berlaku sampai tengah bundle.
- **Keamanan ekstraksi** (`BundleNames.ValidateEntryName`, aturan sama di semua OS): hanya nama file datar, tanpa pemisah path, `..`, `:` (ADS), karakter terlarang Windows, akhiran titik/spasi, dan nama perangkat Windows (CON, NUL, COM1, ...). Duplikat (tidak peka huruf besar/kecil), entri lebih dari 100.000, ukuran lebih besar dari sisa data, jumlah tidak cocok, data tambahan, dan UTF-8 tidak valid ditolak. File dibuat dengan `CreateNew` (tidak pernah menimpa).
- Enkripsi file tunggal menolak input bernama `*.ts4bundle` (nama itu dicadangkan). Audit bundle memuat `bundle=`, `files=`, `keyId=`, dan daftar nama (maks 50).
- **Subfolder di dalam bundle = format versi 2.** Nama entri boleh berupa path dengan pemisah `/` (`Laporan/sub/data.xlsx`); tiap komponen tetap lewat aturan `ValidateEntryName`, plus `ValidateEntryPath` (tanpa komponen kosong, `/` di awal/akhir/ganda, `\`, `..`, maks 32 tingkat dan 1.024 karakter). **Penulis memilih versi terendah yang cukup:** bundle yang isinya datar tetap versi 1 (bisa dibuka build lama); yang bersubfolder versi 2 (build lama menolak dengan "versi tidak didukung"). Pembaca menerima keduanya, dan versi 1 tetap hanya menerima nama datar. Folder kosong tidak dimuat (yang tersimpan hanya file).
- **Perencanaan nama:** `BundleNames.PlanEntryPaths`. File tunggal di akar bundle; file dari folder dikelompokkan di bawah satu folder induk (`GroupId` per penambahan folder). Dua folder bernama sama dari tempat berbeda menjadi `Data` dan `Data (2)` (tidak digabung); file lepas bernama sama dengan sebuah folder diberi `(2)`. `BundleReadStream` dan `BundleExtractor` sama-sama menolak nama yang sekaligus file dan folder (`a` dan `a/b.txt`), duplikat tanpa peduli huruf besar/kecil, dan memastikan hasil akhirnya tetap di bawah folder tujuan.
- **Penelusuran folder** (`BatchFileViewModelBase.BuildFolderItems`): rekursif kalau `IncludeSubfolders` (bawaan aktif; berlaku juga untuk mode per-file dan untuk folder yang diseret), `IgnoreInaccessible = false` (folder yang tak bisa dibaca membatalkan seluruh penambahan dengan pesan, bukan dilewati diam-diam), file **tersembunyi, sistem, dan tautan/reparse point dilewati** (mencegah loop dan keluar dari folder yang dipilih). Folder tujuan bundle bawaan = induk folder yang ditambahkan (`BatchFileItem.DefaultOutputFolder`).
- Uji sekali pakai (tidak disimpan): pohon 5.000 file, 3 tingkat: penelusuran 0,1 dtk, enkripsi bundle 0,9 dtk, dekripsi+ekstraksi 6,4 dtk (didominasi pembuatan 5.000 file baru di Windows), semua isi cocok.
- **Belum ada:** folder kosong di dalam bundle, opsi menyertakan file tersembunyi, dan progres per file di dalam bundle. Cabang "file menyusut saat dibaca" hanya teruji di OS yang mengizinkannya; di Windows `FileShare.Read` sudah mencegahnya.
- Jebakan tes: memanggil `ExecuteAsync(...).GetAwaiter().GetResult()` dari thread UI headless membuat **deadlock**. Jalankan lewat `Task.Run(...)` (lihat `MainShell_EncryptBatch_Screenshot`).

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
2. **Distribusi kunci**: sudah diimplementasikan (lihat bagian 3). Rotasi dan pencabutan kunci identitas juga sudah ada. Sisa: uji manual antar-dua-mesin lewat dialog file, konfirmasi ke Bidsandi apakah alur digital + sidik jari manual diterima, pertimbangkan hybrid ML-KEM, dan peran Superadmin vs AdminDaerah (sekarang penerbit ditentukan oleh tombol "Aktifkan sebagai Penerbit", bukan peran).
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
