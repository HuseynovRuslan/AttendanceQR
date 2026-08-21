# QRLog — Production cutover planı (62.84.179.39 → 94.20.153.137)

Status: **PLAN — heç nə icra olunmayıb.** Hər addım ayrıca təsdiqlə başlayır. DNS-ə bu sənəd
təsdiqlənib «GO» deyilənə qədər toxunulmur.

Faktlar (2026-08-21): prod DB 18 MB (dump 1 MB, restore 1 s); DNS Cloudflare **proxy-siz** (A qeydləri
birbaşa köhnə IP-yə, TTL «Auto» ≈ 300 s); köhnə server yeni serverin 22 portuna çata bilmir (AZ-only) —
bütün fayllar köhnə → operator maşını → yeni yolu ilə keçir; Data Protection açarları konteyner
daxilindədir və hər deploy-da onsuz da yenilənir (heç nə onlardan asılı deyil); JWT müddətsizdir;
telefonların offline növbəsi 502/503-də skanı saxlayıb özü göndərir (keçid pəncərəsinin sığortası).

## 0. Xülasə

| | |
|---|---|
| Pəncərə | Çərşənbə axşamı və ya çərşənbə, **20:30–22:30 Bakı** (17:00–19:00 pikindən sonra, 23:15 backup və 00:30 gecə işindən əvvəl). Cümə/şənbə axşamı YOX (bazar ertəsi səhəri yoxlanmamış qalar) |
| Yazı dondurulması (downtime) | **~25 dəqiqə** (T+0 → T+25). Bu müddətdə skan edənlər yaşıl «yadda saxlanıldı» kartı görür, skanlar telefonlarda növbələnir və keçiddən sonra özü düşür |
| Oxu kəsilməsi | Admin panel/hesabatlar ~25 dəq; landing (qrlog.az) DNS keçidinə qədər köhnə serverdən xidmət edir |
| GO/NO-GO qapıları | 3 (T−1 saat, T+22, T+40) — hər birində abort meyarı var |
| Köhnə server | **7 gün toxunulmur**, maintenance + DB read-only + backend dayandırılmış vəziyyətdə «isti ehtiyat» |

## 1. Hazırlıq (T−2 gün … T−1 saat) — heç biri downtime yaratmır

| # | Addım | Kim | Müddət |
|---|---|---|---|
| H1 | Cloudflare-də bütün qeydlərin **TTL-ini 60 s-ə** endir: `qrlog.az`, `www`, `api`, `bax`, `app`, `ecaf`, `cleanfix`, `test`, `api-test`, `admin` + catch-all ilə işləyən hər tenant subdomeni (Cloudflare-də siyahıya bax). Proxy statusuna toxunma (DNS-only qalır) | operator (Cloudflare) | 10 dəq, **T−24 saat** (köhnə 300 s TTL-in keşlərdən çıxması üçün) |
| H2 | Yeni sirlər (bölmə 2): Cloudflare-də iki **yeni R2 API tokeni** (foto bucket; backup bucket), AWS IAM-da **yeni Rekognition açarı**, OpenAI-də **yeni Assistant açarı**, BotFather-də Telegram tokeni (istəyə bağlı). Köhnələr **hələ ləğv edilmir** | operator | 30 dəq |
| H3 | Yeni hostda prod `.env`-i yığ: köhnə `.env`-dən qorunan dəyərlər + H2 yeniləri + yeni `POSTGRES_PASSWORD` + yeni `Jwt__SigningKey`. Yalnız `/opt/attendanceqr/.env`, `chmod 600`, Git-ə yox | mən | 20 dəq |
| H4 | Yeni hostda staging-i **prod modelinə** keçir: staging Caddy overlay-ı çıxar (80/443-ü prod Caddy tutacaq), `edge` = `attendanceqr_default` external. Prod stack-i `Caddyfile.cutover-internal` ilə (bütün prod hostlar `tls internal`) **soyuq** qaldır: DB boş, backend sağlam, frontend, landing (`ops/build-landing.sh`). Hələ heç bir istifadəçi buraya gəlmir | mən | 40 dəq |
| H5 | Yeni hostda cron faylı (`backup`, `watchdog`, `prune`, `restore-test`, staging `autodeploy`) yazılır, **hamısı şərhdə** (deaktiv) | mən | 10 dəq |
| H6 | Restore məşqi — artıq edilib (2026-08-21: prod dump, 1 s, 0 xəta, saylar eyni). Cutover günü səhər **təkrar** edilir | mən | 5 dəq |
| H7 | Elan (tətbiqdaxili, bütün tenantlar): «Bu axşam 20:30–21:00 texniki fasilə. Skan edə bilərsiniz — qeyd telefonunuzda saxlanıb fasilədən sonra özü göndəriləcək.» | mən, operator təsdiqi ilə | 5 dəq, **T−1 gün** |
| H8 | Cutover səhəri kontrol siyahısı: köhnə/yeni disk, yeni hostda `docker ps`, Telegram kanalı, operatorun Cloudflare-ə girişi açıqdır, bu sənəd əldədir | operator + mən | 10 dəq |

**H-qapısı (T−1 saat):** H1 TTL-in 24 saatdır 60 s olduğu `dig` ilə təsdiqlənməyibsə, H2 açarları
hazır deyilsə, yeni hostda soyuq stack sağlam deyilsə — **keçid başqa günə.**

## 2. Sirlər — nə yenilənir, nə qorunur, təsiri

| Sirr (`.env` adı) | Qərar | Səbəb / təsir |
|---|---|---|
| `POSTGRES_PASSWORD` (+USER/DB) | **YENİ** | Yeni DB konteyneri sıfırdan yaranır; dump-da parol yoxdur; heç nəyi pozmur |
| `Jwt__SigningKey` | **YENİ (tövsiyə)** | 10 iyul kompromisinin əsas sirri. Təsir: **bütün sessiyalar ləğv olur — hər işçi və admin bir dəfə telefon+PIN ilə yenidən girir**; nativ tətbiq də. H7 elanında yazılır. Alternativ (operator qərarı): qorumaq və 1 həftə sonra ayrıca rotasiya — təhlükəsizlik borcu qalır |
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
| TLS sertifikatları (Caddy `caddy_data`) | **köçürülmür** | Yeni Caddy DNS-dən sonra ilk sorğuda Let's Encrypt-dən yenisini alır (host başına saniyələr; LE limiti 50/həftə — 10-12 host problemsiz) |

Qayda: **bir gecədə yalnız «yaranmadan dəyişə bilən» sirlər** (DB, R2, IAM, OpenAI, JWT). Mövcud
məlumatı və ya cihaz tərəfindəki vəziyyəti poza bilən hər şey (QR, VAPID, FCM) toxunulmaz qalır.

## 3. Cutover gecəsi — addım-addım (T0 = 20:30)

| Vaxt | Addım | Müddət | Yoxlama / abort meyarı |
|---|---|---|---|
| **T−30** | **Autodeploy-lar dayandırılır** — köhnə: `/etc/cron.d/attendanceqr`-da `staging-autodeploy`, `watchdog`, `backup`, `prune`, `restore-test` sətirləri şərhə alınır (watchdog xüsusilə — əks halda dayandırdığımız backend-i özü qaldırar). Yeni hostda cron onsuz da deaktivdir. Git-ə push **qadağan** bitənə qədər | 3 dəq | `pgrep` boşdur |
| T−25 | Köhnə prod sayları çıxarılır və saxlanılır (Tenants, Employees, AttendanceRecords, ProcessedScans, DailySummaries, PendingPhotoUploads, LeaveRecords, DeviceBindings) | 1 dəq | sonra müqayisə üçün |
| T−20 | Yeni hostda H6 restore məşqi bir daha (dünənki dump) | 3 dəq | 0 xəta |
| **T+0** | **Maintenance (köhnə):** Caddyfile-ın maintenance variantı — `api.qrlog.az` və `api-test` → `503 {"error":"Maintenance"}`; frontend hostlar → statik «texniki fasilə» səhifəsi (`Retry-After: 1800`); landing olduğu kimi. `caddy validate` + force-recreate. **Nəticə:** telefonlar 503 alır → skan növbəyə düşür (yaşıl kart) | 2 dəq | `curl` api → 503; bax → fasilə səhifəsi |
| T+2 | **Yazı dayandırılır (sərt):** köhnə backend konteyneri `docker stop` (watchdog dayanıb — qalxmayacaq); köhnə DB-də `ALTER DATABASE attendanceqr SET default_transaction_read_only = on` + aktiv bağlantılar kəsilir. Bu andan köhnə DB-yə heç bir yazı mümkün deyil — **split-brain qoruması №1** | 1 dəq | `docker ps` backend yox; test `INSERT` → xəta |
| T+3 | Foto növbəsi: `PendingPhotoUploads` sayı = 0 olmalıdır (PhotoUploadWorker backend dayanana qədər boşaldıb). Deyilsə qalan sətirlər dump-a düşür və yeni hostda worker davam etdirir — itki yoxdur | 1 dəq | say qeyd olunur |
| T+4 | **Son dump:** `pg_dump --no-owner --clean --if-exists \| gzip` → `attendanceqr_cutover_<ts>.sql.gz`; `sha256sum`; ölçü ≥ dünənki gecə dump-ı (−5 %-dən çox kiçikdirsə → **ABORT**); dump-dan sətir sayları (`COPY` blokları) T−25 sayları ilə üst-üstə düşür | 2 dəq | sha256 + ölçü + saylar |
| T+6 | R2 nüsxəsi: `s3://qrlog-backups/db-backups/cutover/<fayl>` (köhnə backup tokeni ilə) + lokal operator maşınına `scp`; yeni hosta `scp`; yeni hostda `sha256sum` **eynidir** (deyilsə → yenidən köçür, ABORT yox) | 3 dəq | 3 yerdə eyni hash |
| T+9 | **Restore (yeni):** yeni prod DB volume-u sıfırdan (H4-də boş yaranmışdı) → `gunzip \| psql -v ON_ERROR_STOP=1`; saylar T−25 ilə **bire-bir**; `__EFMigrationsHistory` = 64 | 2 dəq | fərq varsa → **ABORT (köhnəyə qayıt, bölmə 6-A)** |
| T+11 | Yeni backend `.env` (H3) ilə qaldırılır; startup miqrasiyası «0 pending» yazmalıdır (eyni commit); `/health` ok; Caddy hələ `tls internal` | 2 dəq | loglarda `Applying migration` YOXDUR |
| **T+13** | **DNS-siz smoke (yeni prod)** — operator maşınında `--resolve`/hosts ilə: admin login (bax), işçi login, **demo Elvin ilə real selfili skan** → qeyd + foto açarı + R2 obyekti (real foto bucket-ı, yeni açarla) → izlər silinir; `/api/diag/queues` pending=0 failed=0 dropped=0; today board, tabel, maaş, problems; `/admin/live`; landing 200; `test.qrlog.az` (staging) 200; Telegram `alert.sh` test; Rekognition sorğusu (foto-check 1 çağırış); JWT yeni açarla imzalanır (köhnə token → 401) | 9 dəq | hər bənd ✓ |
| **T+22 — GO/NO-GO №1** | Smoke-da tək bir qırmızı → **NO-GO → bölmə 6-A** (heç nə itməyib, köhnə 10 dəqiqəyə qayıdır) | — | |
| T+23 | **DNS:** Cloudflare-də bütün A qeydləri → `94.20.153.137` (H1 siyahısı). Eyni anda yeni hostda Caddy `Caddyfile` (real, LE + on-demand) ilə force-recreate. **Köhnə server maintenance-də QALIR** — split-brain qoruması №2: DNS keşi köhnəyə aparan telefon 503 alır, skanı saxlayır, 60 s heartbeat-lə yenidən cəhd edir və DNS yenilənəndə yeniyə düşür | 3 dəq | `dig @1.1.1.1` yeni IP; Caddy logunda `certificate obtained` hər host üçün |
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

- `ops/new-server/Caddyfile.maintenance` — köhnə server üçün: api hostlar 503 JSON, frontend hostlar statik fasilə səhifəsi (`Retry-After`), landing dəyişməz
- `ops/new-server/Caddyfile.cutover-internal` — yeni hostda DNS-siz smoke üçün bütün prod hostlar `tls internal`
- `ops/new-server/cutover-checklist.sh` — T−25 sayları, dump+sha256, saylar müqayisəsi, smoke curl-ları (yalnız oxuyan/yoxlayan; heç bir dəyişiklik etmir)
- Yeni hostda cron faylı şablonu (hamısı şərhdə)

Bunlar «GO — hazırlığa başla» təsdiqindən sonra yazılır və yoxlanılır; cutover gecəsinə qədər heç biri köhnə serverdə tətbiq olunmur.
