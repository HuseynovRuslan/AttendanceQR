# QRLog — Production cutover planı (62.84.179.39 → 94.20.153.137)

Status: **PLAN v2 — heç nə icra olunmayıb.** Hər addım ayrıca təsdiqlə başlayır. DNS-ə bu sənəd
təsdiqlənib «GO» deyilənə qədər toxunulmur. v2: rəy üzrə 6 düzəliş (frontend açıq qalır, JWT sınağı
sübutlu, restore backend-siz, TLS sertifikatları köçürülür, app SHA sabit, TTL gözləməsi ləğv).

Faktlar (2026-08-21): prod DB 18 MB (dump 1 MB, restore 1 s); DNS Cloudflare **proxy-siz** (A qeydləri
birbaşa köhnə IP-yə, **TTL = 300 s** — 3 resolver + authoritative NS ilə təsdiqlənib, ixrac:
`dns-before-2026-08-21.txt`); **app SHA: prod-da `798601a` işləyir, `main` HEAD-də tətbiq kodu onunla
eynidir** (fərq yalnız ops/ və Caddyfile); **JWT rotasiyası real brauzerlə sübut edilib** (2026-08-21,
yeni hostdakı staging: kəsintidə köhnə JWT ilə növbəyə düşən skan → açar dəyişdi → 401, növbə qaldı →
yenidən login → skan bir dəfə yazıldı); trafik: son 14 gündə 21:00-dan sonra saatda 1–6 skan,
23:00-da 0–1; köhnə server yeni serverin 22 portuna çata bilmir (AZ-only) —
bütün fayllar köhnə → operator maşını → yeni yolu ilə keçir; Data Protection açarları konteyner
daxilindədir və hər deploy-da onsuz da yenilənir (heç nə onlardan asılı deyil); JWT müddətsizdir;
telefonların offline növbəsi 502/503-də skanı saxlayıb özü göndərir (keçid pəncərəsinin sığortası).

## 0. Xülasə

| | |
|---|---|
| Pəncərə | **Çərşənbə axşamı və ya çərşənbə, T0 = 21:30 Bakı** — son 14 günün saatlıq trafikinə görə 21:00–23:00 ən sakit zolaqdır (saatda 1–6 skan; 18:00-da 17–35), 23:15 backup və 00:30 gecə işinə qədər 1,5 saat ehtiyat qalır. Cümə/şənbə axşamı YOX |
| Yazı dondurulması (downtime) | **~25 dəqiqə** (T+0 → T+25). **Frontend bütün müddət açıq qalır** — yalnız API 503 qaytarır; QR açan işçi tətbiqi yükləyir, skan edir, yaşıl «yadda saxlanıldı» kartı görür, skan telefonda növbələnir və keçiddən sonra özü düşür (eyni mexanizm staging-də real brauzerlə sübut edilib) |
| Oxu kəsilməsi | Admin panel/hesabatlar ~25 dəq (API yoxdur); tətbiq və landing yüklənir |
| GO/NO-GO qapıları | 3 (T−1 saat, T+22, T+40) — hər birində abort meyarı var |
| Köhnə server | **7 gün toxunulmur**, maintenance + DB read-only + backend dayandırılmış vəziyyətdə «isti ehtiyat» |

## 1. Hazırlıq (T−2 gün … T−1 saat) — heç biri downtime yaratmır

| # | Addım | Kim | Müddət |
|---|---|---|---|
| H1 | DNS «əvvəl» vəziyyəti: Cloudflare panelindən **tam qeyd siyahısını ixrac et** və `dns-before-2026-08-21.txt` ilə tutuşdur (catch-all tenantları — məs. `pivezakuska` — siyahıya düşsün). TTL hər yerdə 300 s-dir, **24 saat gözləmə lazım deyil** (keş ≤ 5 dəq). İstəyə bağlı: T−1 saat TTL → 60 s. Proxy statusuna toxunma (DNS-only qalır) | operator (Cloudflare) | 10 dəq, cutover günü |
| H2 | Yeni sirlər (bölmə 2): Cloudflare-də iki **yeni R2 API tokeni** (foto bucket; backup bucket), AWS IAM-da **yeni Rekognition açarı**, OpenAI-də **yeni Assistant açarı**, BotFather-də Telegram tokeni (istəyə bağlı). Köhnələr **hələ ləğv edilmir** | operator | 30 dəq |
| H3 | Yeni hostda prod `.env`-i yığ: köhnə `.env`-dən qorunan dəyərlər + H2 yeniləri + yeni `POSTGRES_PASSWORD` + yeni `Jwt__SigningKey`. Yalnız `/opt/attendanceqr/.env`, `chmod 600`, Git-ə yox | mən | 20 dəq |
| H4 | **App SHA sabitlənir:** deploy commit-i `D` üçün `git diff --quiet 798601a D -- src frontend docker-compose.prod.yml` boş olmalıdır (prod-dakı tətbiqlə eyni), `D` `prod-cutover` teqi ilə işarələnir; **T−1 gündən cutover bitənə qədər `main`-ə tətbiq kodu merge olunmur.** Yeni hostda staging-i prod modelinə keçir (staging Caddy overlay-ı çıxar, `edge` = `attendanceqr_default` external). Prod stack-i **real `Caddyfile`** və köçürülmüş sertifikatlarla (H4b) **soyuq** qaldır: DB boş, frontend, landing (`ops/build-landing.sh`). **Backend yalnız bir dəfə, ilkin `/health` + miqrasiya yoxlaması üçün qalxır, sonra `docker stop` edilir və bütün worker/job-larla birlikdə T+11-ə qədər bağlı qalır** (restore məşqləri də backend-siz — ayrıca konteynerdə). Hələ heç bir istifadəçi buraya gəlmir. Paritet qapısı: `ops/new-server/app-parity-check.sh 798601a <D>` — src, frontend, hər iki Dockerfile, .dockerignore-lar, docker-compose.prod.yml, Caddyfile, root .NET build faylları | mən | 40 dəq |
| H4b | **TLS sertifikatları köçürülür** — `ops/new-server/caddy-data-transfer.sh`: köhnə host `tar`-ı stdout-a yazır, baytlar **iki SSH tuneli arasında operator prosesinin yaddaşındakı borudan** keçir, yeni host birbaşa volume-a açır — **operator diskinə heç nə yazılmır** (shred-ə ehtiyac yoxdur). Skript sonra yeni hostda hər canlı hostun sertifikatının müddətini `openssl -checkend` ilə yoxlayır (≥ 20 gün, əks halda exit 4) və ACME hesabının gəldiyini təsdiqləyir. **Məşq edilib (2026-08-21):** 51 fayl, 2 ACME hesabı, 11 canlı host, ən erkən bitmə 9 oktyabr (~49 gün); müvəqqəti Caddy ilə `curl` `-k`-sız real Let's Encrypt TLS keçdi; sınaq volume-u silindi. Köhnəlmiş `ecafe/katalog/sinaq/tlstest` qapıdan kənardır | mən | 10 dəq |
| H5 | Yeni hostda cron faylı (`backup`, `watchdog`, `prune`, `restore-test`, staging `autodeploy`) yazılır, **hamısı şərhdə** (deaktiv) | mən | 10 dəq |
| H6 | Restore məşqi — `cutover-checklist.sh` ilə: `snapshot` → `dump` (sha256 + ölçü qapısı) → **ayrıca, şəbəkəsiz, backend-siz Postgres konteynerinə** restore → `compare` (cədvəl-cədvəl) → `smoke`. Staging-də tam dövrə keçib (mənfi kontrollar da: kiçik dump → STOP, say fərqi → STOP). Cutover günü səhər dünənki prod dump-ı ilə **təkrar** | mən | 5 dəq |
| H7 | Elan (tətbiqdaxili, bütün tenantlar): «Bu axşam 21:30–22:00 texniki fasilə. Skan edə bilərsiniz — qeyd telefonunuzda saxlanıb fasilədən sonra özü göndəriləcək. Fasilədən sonra bir dəfə yenidən daxil olmaq lazım gələcək.» | mən, operator təsdiqi ilə | 5 dəq, **T−1 gün** |
| H8 | Cutover səhəri kontrol siyahısı: köhnə/yeni disk, yeni hostda `docker ps`, Telegram kanalı, operatorun Cloudflare-ə girişi açıqdır, bu sənəd əldədir | operator + mən | 10 dəq |

**H-qapısı (T−1 saat):** H1 DNS ixracı əldə deyilsə və ya TTL gözlənilməz dəyərdədirsə, H2 açarları
hazır deyilsə, H4b sertifikatları yeni Caddy-də görünmürsə, yeni hostda soyuq stack sağlam deyilsə —
**keçid başqa günə.**

## 2. Sirlər — nə yenilənir, nə qorunur, təsiri

| Sirr (`.env` adı) | Qərar | Səbəb / təsir |
|---|---|---|
| `POSTGRES_PASSWORD` (+USER/DB) | **YENİ** | Yeni DB konteyneri sıfırdan yaranır; dump-da parol yoxdur; heç nəyi pozmur |
| `Jwt__SigningKey` | **YENİ** | 10 iyul kompromisinin əsas sirri. Təsir: **bütün sessiyalar ləğv olur — hər işçi və admin bir dəfə telefon+PIN ilə yenidən girir**; nativ tətbiq də. **Sübut (2026-08-21, real brauzer):** kəsintidə köhnə JWT ilə növbəyə düşən skan, açar dəyişəndən sonra 401 alır, amma növbə SİLİNMİR; yenidən logindən sonra bir dəfə yazılır. H7 elanında «yenidən giriş lazım olacaq» yazılır |
| `QrToken__Secret` | **QORUNUR** | Dəyişsə 3 şirkətdəki bütün çap olunmuş posterlər ölür. Ayrıca layihə: lokasiya-lokasiya `QrVersion` artırmaqla poster yenidən çap dövrü |
| `Push__PublicKey` / `Push__PrivateKey` / `Push__Subject` (VAPID) | **QORUNUR** | Dəyişsə bütün push abunəlikləri etibarsızlaşır, hər telefon yenidən icazə verməlidir |
| `Fcm__ProjectId` / `Fcm__ServiceAccountBase64` | **QORUNUR** (sonra istəyə bağlı) | Cihaz tokenləri Firebase layihəsinə bağlıdır, service account-a yox — rotasiya təhlükəsizdir, amma Firebase konsolu istəyir; cutover gecəsinə aid deyil |
| `Storage__Minio__AccessKey/SecretKey` (foto R2) | **YENİ** (H2) | Obyektlər bucket-da qalır, yalnız açar dəyişir. Köhnə token **T+7 gün** ləğv edilir (köhnə server rollback üçün işlək qalsın) |
| `Storage__Minio__Endpoint/BucketName/Region/UseSsl` | qorunur | eyni bucket |
| `Backup__R2__AccessKey/SecretKey` | **YENİ** (H2) | eyni məntiq; köhnə token T+7 gün ləğv |
| `Rekognition__AccessKey/SecretKey` | **YENİ** (H2) | IAM-da yeni açar; köhnəsi T+7 gün deaktiv |
| `Rekognition__Region` | qorunur | |
| `Assistant__ApiKey` | **YENİ** (H2) | OpenAI açarı; köhnə T+1 gün ləğv (rollback-da köməkçi çat qısa müddət işləməyə bilər — qəbul edilir) |
| `Assistant__Model` | qorunur | |
| `ALERT_TELEGRAM_TOKEN/CHAT` | qorunur (rotasiya istəyə bağlı) | Bot tokeni dəyişsə watchdog bir müddət lal qalar; cutover gecəsində riskə dəyməz |
| `Cors__AllowedOrigins`, `VITE_API_URL`, `App__*`, `DeviceBinding__*` | qorunur | konfiqurasiya, sirr deyil |
| `App__SuperAdminEmployeeIds`, `App__TaskBoardEmployeeIds`, `App__HiddenEmails` | qorunur | ID-lər DB ilə gəlir |
| Data Protection açarları | **heç nə** | konteyner daxilində, hər deploy yenilənir, heç bir axın onlara bağlı deyil |
| TLS sertifikatları + ACME hesabı (Caddy `caddy_data`) | **KÖÇÜRÜLÜR** (H4b) | Mövcud etibarlı sertifikatlar yeni hostda olduğu kimi işləyir; DNS-dən sonra kütləvi issuance və LE limit riski yoxdur; yenilənmə Caddy-nin adi dövründə. Transit nüsxə operator maşınında `shred` edilir |

Qayda: **bir gecədə yalnız «yaranmadan dəyişə bilən» sirlər** (DB, R2, IAM, OpenAI, JWT). Mövcud
məlumatı və ya cihaz tərəfindəki vəziyyəti poza bilən hər şey (QR, VAPID, FCM) toxunulmaz qalır.

## 3. Cutover gecəsi — addım-addım (T0 = 21:30)

| Vaxt | Addım | Müddət | Yoxlama / abort meyarı |
|---|---|---|---|
| **T−30** | **Autodeploy-lar dayandırılır** — köhnə: `/etc/cron.d/attendanceqr`-da `staging-autodeploy`, `watchdog`, `backup`, `prune`, `restore-test` sətirləri şərhə alınır (watchdog xüsusilə — əks halda dayandırdığımız backend-i özü qaldırar). Yeni hostda cron onsuz da deaktivdir. Git-ə push **qadağan** bitənə qədər | 3 dəq | `pgrep` boşdur |
| T−25 | Köhnə prod sayları çıxarılır və saxlanılır (Tenants, Employees, AttendanceRecords, ProcessedScans, DailySummaries, PendingPhotoUploads, LeaveRecords, DeviceBindings) | 1 dəq | sonra müqayisə üçün |
| T−20 | Yeni hostda H6 restore məşqi bir daha (dünənki dump) | 3 dəq | 0 xəta |
| **T+0** | **Maintenance (köhnə) — YALNIZ API:** Caddyfile-ın maintenance variantı — `api.qrlog.az` və `api-test` → `503 {"error":"Maintenance"}` (`Retry-After: 1500`); **frontend hostlar, landing və statik fayllar OLDUĞU KİMİ qalır** — tətbiq yüklənir, skan ekranı açılır, CORS-suz 503 brauzerdə şəbəkə xətası kimi görünür → skan növbəyə düşür (yaşıl kart; staging-də real brauzerlə sübut edilib). `caddy validate` + force-recreate | 2 dəq | `curl` api → 503; `curl` bax → 200 (SPA) |
| T+2 | **Yazı dayandırılır (sərt):** köhnə backend konteyneri `docker stop` (watchdog dayanıb — qalxmayacaq); köhnə DB-də `ALTER DATABASE attendanceqr SET default_transaction_read_only = on` + aktiv bağlantılar kəsilir. Bu andan köhnə DB-yə heç bir yazı mümkün deyil — **split-brain qoruması №1** | 1 dəq | `docker ps` backend yox; test `INSERT` → xəta |
| T+3 | Foto növbəsi: `PendingPhotoUploads` sayı = 0 olmalıdır (PhotoUploadWorker backend dayanana qədər boşaldıb). Deyilsə qalan sətirlər dump-a düşür və yeni hostda worker davam etdirir — itki yoxdur | 1 dəq | say qeyd olunur |
| T+4 | **Son dump:** `pg_dump --no-owner --clean --if-exists \| gzip` → `attendanceqr_cutover_<ts>.sql.gz`; `sha256sum`; ölçü ≥ dünənki gecə dump-ı (−5 %-dən çox kiçikdirsə → **ABORT**); dump-dan sətir sayları (`COPY` blokları) T−25 sayları ilə üst-üstə düşür | 2 dəq | sha256 + ölçü + saylar |
| T+6 | R2 nüsxəsi: `s3://qrlog-backups/db-backups/cutover/<fayl>` (köhnə backup tokeni ilə) + lokal operator maşınına `scp`; yeni hosta `scp`; yeni hostda `sha256sum` **eynidir** (deyilsə → yenidən köçür, ABORT yox) | 3 dəq | 3 yerdə eyni hash |
| T+8 | **Yeni backend və bütün worker/job-lar DAYANDIRILIR:** `docker stop` backend (PhotoUploadWorker, FaceMatchWorker, ReminderJob, DailySummaryJob, AnnouncementPushWorker hamısı backend prosesindədir — o dayananda heç biri işləmir). Yalnız DB və Caddy işləyir; `docker ps`-də backend yoxdur | 1 dəq | backend yox |
| T+9 | **Restore (yeni, backend-siz):** prod DB sıfırdan → `gunzip \| psql -v ON_ERROR_STOP=1`; saylar T−25 ilə **bire-bir**; `__EFMigrationsHistory` = 64; restore bitənə qədər heç bir tətbiq prosesi bazaya qoşulmur | 2 dəq | fərq varsa → **ABORT (köhnəyə qayıt, bölmə 6-A)** |
| T+11 | Yalnız indi: yeni backend `.env` (H3) ilə qaldırılır; startup miqrasiyası «0 pending» yazmalıdır (eyni app SHA); `/health` ok; Caddy real Caddyfile + köçürülmüş sertifikatlarla artıq işləyir | 2 dəq | loglarda `Applying migration` YOXDUR |
| **T+13** | **DNS-siz smoke (yeni prod)** — operator maşınında `--resolve`/hosts ilə: admin login (bax), işçi login, **demo Elvin ilə real selfili skan** → qeyd + foto açarı + R2 obyekti (real foto bucket-ı, yeni açarla) → izlər silinir; `/api/diag/queues` pending=0 failed=0 dropped=0; today board, tabel, maaş, problems; `/admin/live`; landing 200; `test.qrlog.az` (staging) 200; Telegram `alert.sh` test; Rekognition sorğusu (foto-check 1 çağırış); JWT yeni açarla imzalanır (köhnə token → 401; növbədəki skanlar yenidən logindən sonra düşür — sübutlu); TLS: `--resolve` ilə **real sertifikat** etibarlıdır (`curl` `-k`-sız) | 9 dəq | hər bənd ✓ |
| **T+22 — GO/NO-GO №1** | Smoke-da tək bir qırmızı → **NO-GO → bölmə 6-A** (heç nə itməyib, köhnə 10 dəqiqəyə qayıdır) | — | |
| T+23 | **DNS:** Cloudflare-də bütün A qeydləri → `94.20.153.137` (H1 siyahısı). Caddy artıq real konfiqlə və köçürülmüş sertifikatlarla işləyir — issuance gözlənilmir. **Köhnə server API-maintenance-də QALIR** — split-brain qoruması №2: DNS keşi köhnəyə aparan telefon 503 alır, skanı saxlayır, 60 s heartbeat-lə yenidən cəhd edir və ≤ 5 dəq-ə (TTL 300) yeniyə düşür | 3 dəq | `dig @1.1.1.1 @8.8.8.8 @9.9.9.9` yeni IP; Caddy logunda issuance XƏTASI yoxdur |
| T+26 | **Keçiddən sonra yoxlamalar** (bölmə 4) | 12 dəq | |
| **T+40 — GO/NO-GO №2** | Bölmə 4 meyarları ödənmirsə → **bölmə 6-B** (yeni hostda qəbul edilən qeydlərlə birlikdə geri) | — | |
| T+41 | Yeni hostda cron aktiv (backup 23:15, watchdog, prune, restore-test); staging autodeploy yeni hostda aktiv; Git push qadağası götürülür | 3 dəq | |
| T+45 | Köhnə serverdə: heç nə dəyişmir — backend dayanmış, DB read-only, Caddy maintenance, cron-lar şərhdə. **7 gün belə qalır** | — | |
| T+60 … T+24 saat | Müşahidə: Telegram, `docker stats`, `/api/diag/queues`, loglar; ertəsi səhər 07:30–09:30 pikində operator + mən onlayn | | |

**Ümumi yazı dondurulması: T+0 → T+23 ≈ 23–25 dəq.** Skan itkisi gözlənilmir (növbə). Sessiya
itkisi: JWT rotasiyası seçilibsə — hamı bir dəfə yenidən girir.

## 4. Keçiddən sonra yoxlamalar (T+26 … T+40) və uğur meyarları

| Sahə | Yoxlama | Uğur meyarı |
|---|---|---|
| DNS/TLS | `dig` hər host yeni IP; `curl -I` hər hostda etibarlı LE sertifikatı | 10-12 host, hamısı 200 + etibarlı sertifikat, HSTS/CSP başlıqları yerində |
| Login | bax admin, ecaf admin, cleanfix admin, 1 işçi, nativ tətbiq (`app.qrlog.az` app-login) | hamısı 200, yeni JWT |
| Skan | demo Elvin: selfili check-in → qeyd + `CheckInPhotoKey` + R2 obyekti; eyni `clientScanId` offline təkrarı → `AlreadyRecorded`; izlər silinir | 1 qeyd, 1 foto, dublikat 0 |
| Offline növbə | Telefonlarda T+0–T+23 arası yığılan skanlar: `AttendanceRecords` `WasOffline=true` və `SubmittedAtUtc` ≥ T+23 olanlar peyda olur; `ProcessedScans` dublikat buraxmır | Problems ekranında `OfflineRejected` ≤ keçiddən əvvəlki günlük orta |
| Selfi/R2 | `/api/diag/queues`: pending 0, failed 0, dropped 0; worker logu | 10 dəq ərzində pending → 0 |
| Hesabat | today board, tabel (cari ay), maaş, icmal, problems, live board — bax-da | hamısı 200, < 2 s, köhnə ilə eyni rəqəmlər (T−25 sayları) |
| Queue/jobs | PhotoUploadWorker, AnnouncementPushWorker, ReminderJob (5 dəq), DailySummaryJob (00:30-a planlanıb) loglarda qalxıb | xəta yoxdur |
| Watchdog | yeni hostda `watchdog.sh` əl ilə bir dəfə → «problem yoxdur»; Telegram test mesajı | mesaj gəlir |
| Loglar | backend: `error`/`Unhandled` = 0 (ilk 15 dəq); Caddy: sertifikat xətası 0; Postgres: bağlantı rədd 0 | |
| Resurslar | `docker stats`: backend < 1.5 GiB tavanı, CPU sakit | |
| Landing | qrlog.az 200, `/qiymet/` 200, 404 səhifəsi 404 | |

**Uğur = bu cədvəlin hamısı yaşıl + 24 saat ərzində Telegram-da heç bir `problem` + ertəsi səhər
pikində Problems ekranında anormal artım yoxdur.**

**Keçidi dayandırmaq (abort) meyarları:**
- T+4: dump ölçüsü dünənkindən 5 %-dən çox kiçik və ya sətir sayları uyğun deyil → 6-A
- T+9: restore xətası və ya say fərqi → 6-A
- T+22: smoke-da istənilən qırmızı (login, skan, foto R2, hesabat 500) → 6-A
- T+40: DNS-dən sonra 15 dəqiqədə ≥ 2 host sertifikat ala bilmir, login uğursuz, skan 5xx, foto pending artır → 6-B
- İstənilən an: operatorun Cloudflare-ə girişi itib → 6-A (DNS-dən əvvəl) və ya gözlə (DNS-dən sonra — geri çevirmək üçün də giriş lazımdır)

## 5. Split-brain — iki DB eyni vaxtda yazı qəbul etmir

1. **T+2-dən etibarən köhnə DB fiziki olaraq read-only-dir** və köhnə backend dayanıb (watchdog da dayanıb). Köhnəyə gələn hər API sorğusu Caddy-də 503 alır, backend-ə çatmır.
2. Yeni DB yalnız T+9 restore-dan sonra yazı qəbul edir; ondan əvvəl heç bir trafik ora yönəlməyib (DNS köhnədədir, smoke `--resolve` ilədir).
3. DNS keçidi ərzində (TTL 60 s + telefon keşləri) köhnəyə düşən skanlar **itmir** — 503 → telefonda növbə → 60 s-dən bir yenidən cəhd → DNS yenilənəndə yeniyə düşür; `clientScanId` dublikatı `ProcessedScans` (dump-da var) ilə tutulur.
4. Köhnə server **heç vaxt öz-özünə yazı rejiminə qayıtmır**: backend `docker stop`, cron şərhdə, DB read-only. Yalnız rəsmi rollback (bölmə 6) bunu geri açır — və o da yeni DB-ni köhnəyə köçürəndən SONRA.

## 6. Rollback — yeni serverdə qəbul edilən qeydlər itmir

**DNS-i geri çevirmək təkbaşına KİFAYƏT DEYİL** — DNS geri dönəndən sonra köhnə DB-də yeni hostda
yazılan qeydlər olmayacaqdı. Üç səviyyə:

**6-A — DNS-dən ƏVVƏL (T+0 … T+22):** yeni DB heç bir real trafik görməyib (yalnız smoke, izləri
silinib). Köhnədə: DB `default_transaction_read_only = off`, backend `docker start`, Caddy real
Caddyfile ilə force-recreate, cron-lar şərhdən çıxarılır. **~5 dəq, itki 0.** Telefonlardakı növbə
köhnəyə düşür (eyni `ProcessedScans`). Yeni hostdakı prod DB silinir (növbəti cəhd üçün sıfırdan).

**6-B — DNS-dən SONRA (ilk saatlar/günlər):** tam əks cutover:
1. Yeni hostda maintenance (Caddy 503) + yeni backend `docker stop` + yeni DB read-only → yazı dayanır
2. Yeni DB-dən dump + sha256 + R2 `cutover/rollback/` nüsxəsi → köhnəyə köçür
3. Köhnə DB: read-only söndürülür, **yeni dump restore edilir** (köhnədəki T+4 vəziyyəti yeni dump-ın alt çoxluğudur — heç nə itmir; `PendingPhotoUploads` sətirləri də gəlir, köhnə worker davam etdirir)
4. Köhnə backend `.env`: **rotasiya olunmuş R2/IAM/OpenAI açarları köhnədə də işləyir** (köhnələr hələ ləğv edilməyib — T+7 gün qaydası məhz bunun üçündür); `Jwt__SigningKey` rotasiya olunubsa köhnə `.env`-ə **yeni** açar yazılır (yoxsa hamı yenidən çıxır)
5. Köhnə backend/Caddy qalxır, smoke (bölmə 3 T+13 siyahısı), DNS geri `62.84.179.39`
6. Yeni host maintenance-də qalır; R2-də iki dump da saxlanılır
Müddət ~25 dəq, itki 0 (növbələnən skanlar `ProcessedScans` ilə tutulur).

**6-C — 7 gündən sonra:** köhnə server ehtiyat statusundan çıxır (ayrıca qərar); rollback artıq
yalnız «yenidən cutover» şəklində mümkündür.

## 7. Köhnə server — 7 gün

- Heç nə silinmir, heç nə yenilənmir: konteynerlər (backend dayanmış, DB read-only, Caddy maintenance), volume-lar, `.env`, backup faylları, CompreFace (dayanmış).
- Cron-lar şərhdə qalır (backup köhnə DB-ni yox, yeni host DB-ni çəkir).
- Köhnə R2/IAM/OpenAI açarları **T+7 gün** ləğv edilir — ondan əvvəl yox (6-B üçün).
- 7-ci gün: operator qərarı — söndürmək, yoxsa staging/ehtiyat kimi saxlamaq. Sonra: QR sirri rotasiyası layihəsi (poster çapı ilə), FCM service account rotasiyası (istəyə bağlı), Phase 2 prinsipi köhnə host üçün aktual deyil (söndürüləcək).

## 8. Hazırlanacaq fayllar (icra yox, plan daxilində)

Hamısı yazılıb və 2026-08-21-də staging/izolyasiya olunmuş mühitdə sınanıb (heç biri köhnə serverdə tətbiq olunmayıb):

- `Caddyfile.maintenance` — `make-maintenance-caddyfile.py` ilə real Caddyfile-dan **törədilir**: fərq yalnız `api`/`api-test` bloklarında `reverse_proxy` → 503 JSON + `Retry-After`; `caddy validate` keçib. **Məşq:** yeni hostdakı staging-də API-only maintenance → real brauzerdə tətbiq yükləndi, skan yaşıl «yadda saxlanıldı» kartı ilə növbəyə düşdü, API qayıdan kimi özü göndərildi → 1 qeyd, dublikat yox
- `caddy-data-transfer.sh` — axınla köçürmə + müddət qapısı (yuxarıda H4b)
- `app-parity-check.sh` — qorunan yollar: `src`, `frontend`, hər iki Dockerfile, `.dockerignore`-lar, `docker-compose.prod.yml`, `Caddyfile`, root .NET build faylları; HEAD ↔ `798601a` **PARITY OK**, mənfi kontrol (7504b8a) düzgün BROKEN
- `cutover-checklist.sh` — `snapshot` / `dump` / `compare` / `smoke`, hamısı yalnız oxuyur; staging-də tam dövrə + mənfi kontrollar keçib
- `cron.attendanceqr.template` — yeni host üçün, bütün işlər şərhdə; T+41-də açılır

Hələ edilməyən hazırlıq addımları (operatorun H2 sirlərini gözləyir): H3 (yeni prod `.env`), H4 (soyuq prod stack + staging-in prod modelinə keçməsi), H4b-nin **real** volume-a köçürülməsi, H5 (cron faylının qurulması). Cutover üçün ayrıca «GO» gözlənilir.
