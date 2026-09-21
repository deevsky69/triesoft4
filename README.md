# TRIESOFT 4 (Tribrata Encryption Software)

Aplikasi desktop untuk **mengenkripsi dan mendekripsi file** (PDF, Word, PPT, gambar, dan lainnya) yang dipakai Bidsandi Baintelkam Polri. Ini generasi ke-4 setelah TRIESOFT 3.

> Status: masih dalam pengembangan. Belum boleh dipakai untuk berita rahasia sungguhan sebelum ada audit keamanan dan pengesahan resmi dari BSSN/Bidsandi.

## Apa yang bisa dilakukan aplikasi ini?

- **Enkripsi file (satu atau banyak sekaligus)**: tambahkan file atau satu folder ke daftar, klik *Enkripsi Semua*. Tiap file menjadi file `.ts4` sendiri.
- **Dekripsi file (satu atau banyak sekaligus)**: tambahkan file `.ts4` atau satu folder, klik *Dekripsi Semua*. Tiap file kembali dengan nama aslinya.
- **Kelola kunci** (khusus Admin): impor kunci bulanan, cabut kunci yang dicurigai bocor, hapus kunci lama.
- **Distribusi kunci** (khusus Admin): Mabes menerbitkan kunci bulanan sebagai paket terenkripsi per Polda (file `.ts4kp`), Polda mengimpornya. Lihat "Distribusi kunci Mabes ke Polda" di bawah.
- **Kelola user** (khusus Admin): daftarkan user baru, verifikasi, nonaktifkan.
- **Audit log** (khusus Admin): catatan semua aktivitas penting. Catatannya dirantai dengan hash, jadi kalau ada yang diubah atau dihapus diam-diam, tombol "Verifikasi Integritas" akan mendeteksinya.

### Ada dua peran

| Peran | Boleh apa |
|---|---|
| **Admin** | Enkripsi/dekripsi, kelola kunci, kelola user, lihat audit log |
| **Operator** | Enkripsi dan dekripsi saja |

## Kenapa lebih aman dari TRIESOFT 3?

- Memakai **AES-256-GCM**, algoritma modern yang sekaligus menjaga kerahasiaan **dan** mendeteksi kalau file diubah orang.
- Setiap file punya **kunci acak sendiri**. Kunci bulanan hanya dipakai untuk "membungkus" kunci file itu. Jadi kalau satu file bocor, file lain di bulan yang sama tetap aman.
- **Nama file asli ikut dienkripsi**, tidak hanya isinya.
- File yang dipotong, ditambah, atau diubah 1 byte pun akan **ditolak** saat dekripsi.
- Kunci yang tersimpan di disk dilindungi **Windows DPAPI**.
- Login dilindungi hash password PBKDF2 dan akun terkunci 15 menit setelah 5 kali salah password.

## Yang perlu disiapkan

- **Windows 10/11** (target utama). Untuk mencoba saja, macOS dan Linux juga bisa (lihat catatan di bawah).
- **.NET 10 SDK**. Unduh di https://dotnet.microsoft.com/download
- **Git**. Unduh di https://git-scm.com

Cek .NET sudah terpasang dengan membuka terminal (PowerShell) lalu ketik:

```
dotnet --version
```

Kalau muncul angka versi `10.x.x`, berarti siap.

## Cara mengunduh dan menguji

```
git clone https://github.com/deevsky69/triesoft4.git
cd triesoft4
dotnet test
```

Kalau semua tes berwarna hijau (`Passed`), berarti kodenya sehat di komputer kamu. Di Windows, tes `DpapiKeyProtectorTests` ikut berjalan sungguhan (di macOS/Linux tes itu dilewati).

## Cara menjalankan aplikasi

**1. (Disarankan) tentukan folder penyimpanan data.**
Secara bawaan aplikasi menyimpan data di `C:\ProgramData\Triesoft4`, dan folder itu bisa butuh hak Administrator. Supaya mudah, arahkan ke folder yang bebas ditulis. Di PowerShell:

```
$env:TRIESOFT4_DATA_DIR = "C:\Triesoft4Data"
```

Pengaturan ini hanya berlaku di jendela PowerShell yang sama. Jalankan ulang setiap membuka jendela baru.

**2. Jalankan:**

```
dotnet run --project src/Triesoft.App
```

## Pemakaian pertama kali

1. **Setup awal.** Karena belum ada akun, aplikasi meminta kamu membuat **akun Admin pertama**. Isi username, nama lengkap, dan password, lalu klik *Buat Akun Admin*.
2. **Masuk** dengan akun itu.
3. **Impor kunci bulanan.** Buka menu **Kelola Kunci**, isi:
   - *Key ID*: nama kunci, misalnya `2026-09-POLDA-JATIM`
   - *Kunci*: 64 karakter heksadesimal (angka 0-9 dan huruf a-f) dari Bidsandi Mabes
   - *Berlaku dari/sampai*: masa berlaku kunci

   Lalu klik **Impor**. Kunci baru otomatis jadi kunci aktif, dan kunci bulan lalu berubah jadi *Expired* (tidak dipakai untuk enkripsi baru, tapi masih bisa membuka arsip lama).
4. **Daftarkan user lain** di menu **Kelola User**. User baru berstatus *PendingVerification*. Klik **Verifikasi** supaya ia bisa masuk.
5. **Enkripsi file** di menu **Enkripsi File**: *Tambah File...* (boleh pilih banyak) atau *Tambah Folder...* (semua file di folder itu, tanpa subfolder), lalu *Enkripsi Semua*. Hasilnya `namafile.ext.ts4` di folder masing-masing file.
6. **Dekripsi file** di menu **Dekripsi File**: tambahkan file `.ts4` dengan cara yang sama lalu *Dekripsi Semua*. Aplikasi otomatis mencari kunci yang cocok untuk tiap file.

Selain lewat tombol, file dan folder bisa **diseret dari File Explorer** ke kartu Enkripsi atau Dekripsi (kartu berubah biru saat siap menerima). Di Dekripsi, item yang bukan `.ts4` dilewati dan dilaporkan.

**Cara kerja daftar file:** tiap baris punya status (Menunggu, Diproses, Berhasil, Gagal, Dibatalkan) dan alasan kalau gagal. Satu file gagal tidak menghentikan yang lain. Tombol *Batal* berhenti setelah file yang sedang berjalan selesai. Menekan tombol proses lagi hanya mengulang file yang belum berhasil. Kalau dua file menghasilkan nama yang sama di satu folder saat dekripsi, yang kedua diberi nama `nama (2).ext` supaya tidak saling menimpa. Tiap file dicatat sendiri di audit log, termasuk yang gagal.

### Distribusi kunci Mabes ke Polda

Alternatif dari mengetik kunci hex. Kunci tidak pernah tampil sebagai teks, dan paket hanya bisa dibuka Polda tujuannya. Semua di menu **Distribusi Kunci** (Admin).

**Persiapan sekali di awal (membangun kepercayaan):**
1. **Mabes:** klik *Aktifkan sebagai Penerbit*, lalu *Ekspor Kunci Publik Penerbit* (file `.ts4pub`). Kirim ke tiap Polda.
2. **Polda:** klik *Ekspor Kunci Publik Penerima*, kirim file ke Mabes.
3. **Verifikasi sidik jari lewat jalur terpisah** (telepon atau tatap muka): masing-masing pihak membacakan sidik jarinya, yang lain mencocokkan. Ini yang mencegah penyerang menyelipkan kunci palsu.
4. **Mabes:** *Impor Kunci Publik Polda*, cocokkan sidik jari di dialog, lalu daftarkan.
5. **Polda:** *Impor Kunci Publik Mabes*, cocokkan sidik jari di dialog, lalu percayai.

**Tiap bulan:**
1. **Mabes:** centang Polda tujuan, isi Key ID dan masa berlaku, klik *Terbitkan Paket*, pilih folder. Hasilnya satu file `.ts4kp` per Polda.
2. Kirim tiap file ke Polda-nya lewat saluran apa saja (email, USB). Isinya terenkripsi dan ditandatangani.
3. **Polda:** *Pilih Paket Kunci*. Tanda tangan dan tujuan diperiksa, lalu kunci masuk sebagai Active.

**Kalau kunci identitas bocor atau mesin hilang** (kartu *Rotasi dan Pencabutan Kunci Identitas*, alasan wajib diisi dan tercatat di audit log):

| Situasi | Yang dilakukan | Akibat |
|---|---|---|
| Kunci privat **Polda** bocor atau laptop hilang | Polda: *Rotasi Kunci Penerima*. Mabes: *Cabut* Polda itu, lalu daftarkan kunci publik baru (sidik jari dicocokkan lagi) | Kunci privat lama dihancurkan. Paket untuk kunci lama tidak bisa diimpor lagi |
| Kunci privat **Mabes** bocor | Mabes: *Rotasi Kunci Penerbit*, kirim kunci publik baru. Tiap Polda: *Cabut Penerbit Tepercaya*, lalu percayai kunci baru (sidik jari dicocokkan) | Paket dari kunci lama ditolak. Kunci bulanan yang sudah diimpor tidak terpengaruh |
| Pergantian berkala | Rotasi tanpa pencabutan | Sama seperti di atas |

Kunci publik yang sudah dicabut **tidak bisa didaftarkan atau dipercaya lagi** dan pencabutan tidak bisa dibatalkan. Kalau salah cabut, lakukan rotasi kunci pihak itu dan daftar ulang.

> Paket yang sudah terbit tidak bisa ditarik kembali. Kalau kunci privat Polda **bocor** (bukan sekadar rotasi berkala), cabut juga kunci bulanan terkait di **Kelola Kunci**.

### Arti status kunci

| Status | Artinya |
|---|---|
| **Active** (hijau) | Kunci bulan ini. Dipakai untuk enkripsi baru |
| **Expired** (abu-abu) | Kunci lama. Hanya untuk membuka arsip lama |
| **Revoked** (merah) | Dicabut karena dicurigai bocor. Materi kunci **dihapus permanen** |
| **Purged** (abu-abu) | Kunci lama yang sudah dihapus permanen dengan cara normal |

> Hati-hati: kunci yang sudah **dicabut** atau **dihapus permanen** tidak bisa dipulihkan. File yang dienkripsi dengan kunci itu **tidak akan bisa dibuka lagi**.

## Catatan penting soal keamanan

- **Di Windows**, kunci disimpan terlindungi memakai DPAPI. Ini pengaturan yang benar untuk penggunaan sesungguhnya.
- **Di macOS/Linux**, DPAPI tidak ada, sehingga aplikasi memakai penyimpanan **tanpa perlindungan** dan menampilkan peringatan. Itu hanya untuk mencoba dan mengembangkan. **Jangan** dipakai untuk data rahasia.
- Jangan simpan kunci bulanan di email, chat, atau catatan biasa.
- Aplikasi ini belum diuji oleh pihak ketiga. Untuk pemakaian resmi, ikuti aturan BSSN (Peraturan BSSN No. 11 Tahun 2024) soal sertifikasi modul kriptografi.

## Untuk pengembang

### Struktur folder

```
src/
  Triesoft.Core/    Logika inti: enkripsi, kunci, user, audit log
  Triesoft.App/     Aplikasi desktop (Avalonia UI)
  Triesoft.Cli/     Alat baris perintah untuk uji manual (bukan produk akhir)
tests/
  Triesoft.Core.Tests/   Tes untuk Triesoft.Core
  Triesoft.App.Tests/    Tes untuk tampilan dan alur di aplikasi
```

### Tes tampilan otomatis

`Triesoft.App.Tests` punya tes yang merender tiap layar menjadi gambar PNG tanpa membuka jendela. Gambarnya disimpan di folder `triesoft4-screenshots` di folder sementara sistem, dan bisa dibuka untuk memeriksa tampilan.

### Contoh pemakaian CLI

```
dotnet run --project src/Triesoft.Cli -- encrypt laporan.pdf --key-id 2026-09-TEST --key <64-karakter-hex>
dotnet run --project src/Triesoft.Cli -- decrypt laporan.pdf.ts4 --key-id 2026-09-TEST --key <64-karakter-hex>
```

## Yang belum ada

- Hybrid tahan komputer kuantum (ML-KEM) untuk distribusi kunci.
- Algoritma tambahan (ChaCha20-Poly1305, algoritma nasional BSSN, dan algoritma tahan komputer kuantum).
- Peran Auditor terpisah dan login dua langkah (kartu pintar/OTP).
