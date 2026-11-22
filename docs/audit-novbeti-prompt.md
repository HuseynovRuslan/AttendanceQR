# Audit — növbəti raundun promptu

## İstifadə qaydası
1. Yeni Claude Code sessiyasını açın. Model və effort üçün defolt kifayətdir, ayrıca seçmək lazım deyil.
2. Aşağıdakı «QƏRARLAR» bölməsini oxuyub cavablarınızı yazın. Hər bəndin altında tövsiyəm var. Cavab vermək istəmədiyiniz bəndi silin, agent ona toxunmayacaq.
3. Bütün bloku kopyalayıb sessiyaya yapışdırın.

Raund 1-də nə edildiyi [`audit-2026-11-22.md`](audit-2026-11-22.md)-də yazılıb.

---

```text
QRLog auditinin RAUND 2-si. Kontekst: docs/audit-2026-11-22.md (raund 1 nəticələri, status cədvəli)
və bu fayl. Raund 1 artıq birləşdirilib (testlər: backend 1863, frontend 761).

İŞ QAYDASI (raund 1 kimi):
- Workflow ilə işlə, agentlərlə. Hər qrup ayrıca git worktree-də, `audit-fix/<qrup>` branch-ında,
  testlə birlikdə yazılsın. Hər qrupu ayrıca agent tənqidi baxışla yoxlasın. Problem tapılsa, düzəliş
  turu olsun.
- Sonra branch-ları sessiyanın inkişaf branch-ına birləşdir. dotnet test, `cd frontend && npm run build
  && npx vitest run` və ops/tests/*.sh hamısı yaşıl olsun, sonra push et. Testlər sınarsa, push etmə,
  mənə göstər.
- `staging`-ə və `main`-ə push etmə, PR açma (mən deməsəm).
- Hər düzəliş üçün əvvəlcə uğursuz olan, sonra keçən test yaz. Mövcud testi zəiflətmə.
- CLAUDE.md qaydaları qüvvədədir. Xüsusən bu ikisinə diqqət: «check-in heç vaxt optional şeyə görə
  bloklanmır», «gün yalnız ShiftResolver-də seçilir».
- Bitəndə: docs/audit-2026-11-22.md status cədvəlini yenilə, növbəti raund üçün bu faylı yenidən yaz
  və push et.

QRUPLAR (biznes qərarı tələb etməyənlər):

[A] auth-tail
 1. Impersonation və view token-ləri ExpiryMinutes (~100 il) ilə yaradılır (JwtService.cs:27-47).
    Onlara qısa müddət ver (məs. 60 dəq). Operatorun icazəsi alınanda (allowlist və ya
    OperatorProfile) açıq sessiya da ölsün.
 2. ImpersonationBoundary.cs:57-66 path-i EndsWith ilə müqayisə edir. Sona "/" əlavə etməklə bu
    yoxlama keçilə bilərmi? Testlə doğrula, keçilirsə normallaşdırılmış route ilə düzəlt.
 3. PushController.cs:128-139 və :183-193 IgnoreQueryFilters ilə başqa şirkətin endpoint/FCM
    sətrini öz hesabına bağlaya bilir. Tenant sərhədini keçən yenidən bağlamanı ya başqa tenant-ın
    sətrini silib yenisini yaratmaqla et, ya da rədd et.
 4. Lockout: hesab axtarışı (DB) exception atanda BeginAttempt cəhdi geri qaytarılmır.
    ILoginLockoutStore-a "cancel attempt" əlavə et; o, həmin cəhdin qoyduğu kilidi də açsın.
 5. Aktivləşdirmə və PhotoUploadWorker-in ilk check-in zamanı yazdığı referans şəkil audit
    olunmur. SetMyReferencePhoto-nun check-then-set əməliyyatını atomik et.
 6. Anonim endpoint-lərə request body limiti qoy (login, activate, forgot-pin/*: məs. 64 KB;
    foto olanlar: ~8 MB). AddRateLimiter əlavə etməyi dəyərləndir. Skan endpoint-inə ehtiyatla
    yanaş: pikdə real işçini 429 ilə qaytarmamalıdır.

[B] checkin-tail
 1. Kiosk QR-ı (TTL 60 s) ilə edilmiş offline skan sonra həmişə TokenExpired alır
    (QrTokenService.cs:114, AttendanceController.cs:733). Offline skanda token-in müddətini
    ClientTimestampUtc-yə görə yoxla, OfflineTrustWindow daxilində.
 2. Offline check-in canlı skandan gec çatanda "TooSoonToCheckOut" kimi səssizcə düşür
    (AttendanceController.cs:1069-1074, mənfi fərq). Elastik yoldakı OutOfOrder qoruması kimi
    bir qoruma əlavə et, client də bunu itirilmiş skan kimi göstərsin.
 3. `08:00–08:00` (24 saat, «sutka») növbəsi gecə növbəsi sayılmır (EffectiveShift.cs:89-114,
    `end < start`). start == end olanda 24 saatlıq növbə kimi işlə.
 4. Geofence söndürülmüş filialda GPS alınmayanda skan dayanır (ScanPage.tsx:387-394, 941-947).
    Belə filialda koordinatsız davam et.
 5. 00:00–04:00 (Bakı) arasında UTC tarixi istifadə olunan yerlər:
    AdminAttendanceController.cs:342, AttendanceController.cs:658-660 və 844, att.ts:142,
    ReportQueryService.cs:1937, ReportsController.cs:339, frontend DateRangePicker.tsx,
    LeaveForm.tsx:48, ReportsPage.tsx:13, TasksPage.tsx:22, HistoryPage.tsx:27, push.ts:203,
    EmployeeProfilePage.tsx:150. Hamısını şirkətin Bakı gününə keçir.

[C] calc-tail
 1. AttendanceCalculator.cs:337-370 gecə yarısından sonrakı çıxışı saat əsasında hesablayır:
    15:00–23:30 növbəsində 00:15-də çıxış 1395 dəq "erkən çıxış" kimi yazılır. Tam timestamp
    ilə hesabla.
 2. Çox bloklu gün yalnız birinci bloka görə qiymətləndirilir (DailySummaryService.cs:244,
    ReportQueryService.cs:385). Gün bütün bloklara görə qiymətləndirilsin.
 3. Yalnız sahə ziyarəti olan günlərdə saxlanmış sətir (DailySummaryService.cs:232-244) ilə canlı
    lövhə (ReportQueryService.cs:395-400) fərqli hesablanır. Bir qaydaya gətir.
 4. GetShiftMismatchAsync (ReportQueryService ~2180) növbəni ScheduleId ilə birbaşa götürür,
    ShiftResolver-dən keçir.
 5. AssistantDataService.cs:164-166-da `gecikmeGunu` sahəsi var. «Gecikmə» qəsdən çıxarılıb,
    bu sahəni sil. İşlənmiş günləri tabel ilə eyni qaydada say.

[D] ops-tail
 1. watchdog.sh: `docker logs --since 15m` çıxışında "BackgroundService failed" və
    DailySummaryJob-un "gave up after" sətirləri görünərsə alert ver.
 2. watchdog.sh: `.last-offbox-ok` marker faylı silinəndə 48 saat əlavə vaxt verilir. Seed yalnız
    bir dəfə olsun. backup.sh: pg_dump uğursuz olanda ERR trap yarımçıq faylı silsin.
 3. watchdog.sh bir dəfəlik 10 s timeout-da backend-i restart edir. Restart yalnız ardıcıl N (məs. 3)
    uğursuzluqdan sonra olsun.
 4. /health yalnız `SELECT 1` icra edir. Ayrıca bir deep-health endpoint-i yarat (Employees-ə real
    sorğu və pending migration yoxlaması) və deploy-prod.sh-un health gate-i onu istifadə etsin.
    deploy-prod.sh-a da GitHub Actions-dakı kimi "yalnız əlavə edən miqrasiya" yoxlaması əlavə et.
 5. Model drift: Npgsql provider ilə (DB-yə qoşulmadan) `HasPendingModelChanges()` testi yaz.
 6. SummaryRebuildWorker.SweepPlanNotesAsync və AnnouncementPushWorker sweep-ində hər tenant üçün
    ayrıca try olsun.
 7. OfflineTrustWindowHours sabitini AttendanceController-dən Application/Domain qatına köçür.
 8. docker-compose.staging.yml başlıq şərhini yeni qaydaya uyğunlaşdır (main və prod deploy ilə
    birlikdə gedəcək).
 9. AuditLogs-a (TenantId, EmployeeId, CreatedAtUtc) indeksi əlavə et. Bu, əlavə edən miqrasiyadır,
    pikdə lock olmasın deyə CONCURRENTLY-ni dəyərləndir.

[E] frontend-tail
 1. EmployeesPage.tsx:740/800 və EmployeeProfilePage.tsx:220-244-də saxlama try/finally olmadan
    gedir: şəbəkə xətası düyməni həmişəlik bağlı saxlayır. Düzəlt.
 2. ForgotPinPage.tsx:109, ActivatePage.tsx:42, SetPinPage.tsx:96: ekrandan çıxanda gecikmiş
    getUserMedia axını dayandırılmır. FieldCheckoutSheet-dəki kimi `cancelled` bayrağı əlavə et.
 3. sw.js:64-66: naviqasiya fetch-inin timeout-u yoxdur, zəif şəbəkədə ağ ekran qalır. ~4 s-dən
    sonra cache-dən versin.
 4. sw.js:139: bütün push-lar eyni `'qrlog-checkout'` tag ilə gəlir, yenisi köhnəsini əvəz edir.
    Tag-ı növə görə ayır. Köhnə build-lərin asset cache-ini təmizlə.
 5. `npm audit fix` ilə dompurify-ı yenilə (`jspdf` diapazonu daxilində, breaking olmadan).
 6. PushGate: «Yoxlanılır…» mətni yalnız ~1 s-dən sonra görünsün.
 7. «İş günü» rəqəmi «Gəlib» rəqəmindən kiçik ola bilər (istirahət günü işlənib). Maaş səhifəsində
    bunun üçün qısa izah (tooltip) əlavə et.

QƏRARLAR (hər bəndin altına cavab yazın; cavabsız bəndə toxunma):

Q1 Offline geri-tarixləmə (Y3). İşçi offline bayrağı ilə vaxtı geri çəkə bilir.
   Tövsiyəm: bloklama yox. İddia olunan vaxt ilə serverə çatma vaxtı arasında fərq >30 dəq
   olarsa VƏ bu fərq günün statusunu dəyişirsə, qeyd «şübhəli» kimi işarələnsin və adminin
   «Problemlər» ekranına düşsün. Cavab: ...

Q2 Kiosk (Y4). Kiosk ekranları real istifadə olunurmu? Olunursa, hər kiosk üçün gizli açar
   lazımdır (kiosklar yenidən konfiqurasiya olunacaq). Olunmursa, endpoint bağlansın.
   Əlavə: /me/device bütün filialların koordinatlarını verir. Yalnız işçinin filialı və sahə
   icazəsi olan filiallar qalsın? Cavab: ...

Q3 Mütənasib maaş (Y8). İşə qəbul, işdən çıxma, ay ortasında filial dəyişmə, "1–5 noyabr"
   kimi qısa aralıq. Tövsiyəm: maaş × (işlədiyi dövrdəki qrafik günləri / ayın qrafik günləri).
   Aralıq ayın içindədirsə, ayın tam bölənindən istifadə olunsun. Cavab: ...

Q4 Tabel ilə maaş (Y9). Sətir yoxdursa tabel «Q» yazır. Tövsiyəm: tabel də canlı hesablama
   (ComputeDayLive) etsin, gecə job-u buraxdığı günü avtomatik doldursun. Cavab: ...

Q5 Keçmiş günlərin yenidən hesablanması (Y10). Filial saatlarının, maskanın, işçinin
   filialının və maaşının tarixçəsi saxlanılsın (Schedule revision kimi)? Bu, böyük işdir,
   ayrıca dizayn sənədi tələb edir. Cavab: ...

Q6 Elastik qrafikdə maaş. Elastik işçinin skan etmədiyi gün DayOff olur və bölənə düşmür.
   Qayda necə olsun? Cavab: ...

Q7 «Qayıb yaz» istirahət və bayram günündə: rədd edilsin, yoxsa icazə verilsin (menecerin
   sözü)? Cavab: ...

Q8 Enforced rejim: RoleAssignments ilə panel hüququ olan "Employee" (Timekeeper,
   CompanyAdmin) üz ilə PIN sıfırlaya bilsin? Tövsiyəm: xeyr. Cavab: ...

Q9 Çıxışda push abunəliyi server tərəfində də silinsin? (Ortaq telefonda çıxan işçinin
   bildirişləri dayansın.) Tövsiyəm: bəli. Cavab: ...

Q10 GroupHead rolu yalnız öz qrupunun şirkətlərini görsün. Qrup necə müəyyən olunur
    (Tenant.GroupId var, yoxsa yeni sahə lazımdır)? Cavab: ...

Q11 "Heç kim öz gününü düzəltmir" qaydası. Menecer öz keçmiş qayıb günlərinə «İstirahət» və ya
    məzuniyyət yaza bilir. Bu bağlansın? Tövsiyəm: bəli, öz adına yalnız gələcək tarixə.
    Cavab: ...

Q12 Prod resurs limitləri: docker-compose.limits.prod.yml deploy-a qoşulsun? (Limit dəyərlərini
    real istifadə ilə müqayisə etmək lazımdır.) Cavab: ...

Q13 Staging üçün ayrıca Docker şəbəkəsi (prod DB-yə çıxışı kəsmək). Prod compose və Caddy
    dəyişir. Cavab: ...

Q14 react-router v6 → v7 major yeniləmə (qalan open-redirect advisory). Cavab: ...

Q15 CLAUDE.md-dəki «IgnoreQueryFilters yalnız 2 yerdə» qaydası. Mövcud ~10 istifadə sənədləşdirilmiş
    istisna siyahısına yazılsın, yoxsa kod qaydaya gətirilsin? Cavab: ...

Q16 Referans şəkil photo-url-də menecerə verilməyə davam etsin? (Adi işçi üçün K1-in qalıq
    riski.) Tövsiyəm: referans yalnız admin və `attendance.photos.view` ilə verilsin, menecerə
    yalnız check-in şəkli göstərilsin. Cavab: ...
```
