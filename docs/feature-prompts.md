# AttendanceQR — Yeni funksiya promtları (Sonnet üçün)

Bu sənəd QR Gate ilə müqayisədən çıxan boşluqları doldurmaq üçün **Sonnet-ə verilə bilən hazır promtlardır**.
Hər promt öz-özünə yetərlidir. Sıra ilə tətbiq et: **PROMT 0-ı hər dəfə əvvəl oxut**, sonra PROMT 1 → 2 → 3.

Dizayn qərarları artıq verilib (Opus tərəfindən) — Sonnet-in yenidən qərar verməsinə ehtiyac yoxdur.

---

## PROMT 0 — Ümumi kontekst (hər promtdan əvvəl bunu ver)

```
Bu, AttendanceQR adlı işləyən bir davamiyyət sistemidir: ASP.NET Core (.NET 10) backend +
React/TypeScript (Vite) frontend + PostgreSQL, Docker Compose ilə lokal işləyir.

Layihə konvensiyaları (HƏR ZAMAN riayət et):
- Backend qatları: Domain (entity/enum), Application (Reporting məntiqi), Infrastructure
  (AppDbContext, Migrations, Security), Api (Controllers, Contracts, Program.cs).
- DB dəyişikliyi = EF Core migration:
    dotnet ef migrations add <Ad> --project src/AttendanceQR.Infrastructure --startup-project src/AttendanceQR.Infrastructure
  Migration startup tətbiqdə avtomatik işləyir (Program.cs: db.Database.Migrate()).
- Admin panel səhifələri theme.css semantik siniflərini işlədir (.card, .btn, .btn-primary,
  .inp, .form-label, .sidebar, .nav-item) — Tailwind YOX. Nümunə: frontend/src/pages/admin/*.
- İşçi (skan) səhifələri Tailwind işlədir — amma yeni işlər ADMIN paneldədir, yəni theme.css.
- Bütün UI mətnləri Azərbaycan dilində.
- API klientləri: frontend/src/api/*.ts, hamısı apiRequest<T>(path,{method,body}) işlədir
  (frontend/src/api/client.ts). 401-də token avtomatik təmizlənir.
- Admin route-ları AdminOnly ilə qorunur (frontend/src/components/AdminRoute.tsx),
  route-lar frontend/src/App.tsx-də, admin naviqasiya frontend/src/pages/admin/AdminLayout.tsx-də.
- Admin controller nümunəsi: [Authorize(Roles="Admin")], route "api/admin/...".

Status enumu (src/AttendanceQR.Domain/Enums/DailySummaryStatus.cs) HAZIRDA:
  OnTime=0, Late=1, Absent=2, Incomplete=3

Status hesablaması TƏK bir yerdədir:
  src/AttendanceQR.Application/Reporting/AttendanceCalculator.cs → Compute(record, location, timeZone)
  Bunu həm gecə job (DailySummaryService.cs), həm canlı "bugün" sorğusu (ReportQueryService.cs) çağırır.
  Qeyd YOXDURSA hazırda avtomatik Absent qaytarır (bu, düzəldəcəyimiz əsas problemdir).

TEST DÖVRÜ (hər funksiyadan sonra, deploy-dan ƏVVƏL):
  1. dotnet build src/AttendanceQR.Api/AttendanceQR.Api.csproj -c Release   → 0 error
  2. cd frontend && npm run build                                            → 0 error
  3. docker compose build backend frontend && docker compose up -d
  4. curl ilə yeni endpoint-ləri yoxla (admin login: POST /api/auth/login)
  5. YALNIZ istifadəçi təsdiqləyəndən sonra deploy: ./scripts/coolify-deploy.sh both

MÜHÜM: Mövcud davranışı sındırma. Skan axını, cihaz bağlama, GPS, mövcud hesabatlar
toxunulmaz qalmalıdır. Hər dəyişiklikdən sonra köhnə funksiyaları da yoxla.
```

---

## PROMT 1 — İş təqvimi (həftəsonu + bayram günləri)

**Problem:** Sistem hazırda həftəsonu/bayram günlərində gəlməyən hər kəsi "Qayıb" sayır, çünki
status hesablaması "iş günü" anlayışını bilmir. Bazar bağlıdır; müdür ara-sıra bayram günləri elan edir.

```
FUNKSİYA: İş təqvimi — qeyri-iş günlərində "Qayıb" əvəzinə "İstirahət" statusu.

1) ENUM — src/AttendanceQR.Domain/Enums/DailySummaryStatus.cs:
   Yeni dəyər əlavə et:  DayOff = 4   // İstirahət (həftəsonu/bayram)
   (Mövcud 0-3 dəyərlərə TOXUNMA.)

2) LOCATION — src/AttendanceQR.Domain/Entities/Location.cs:
   Yeni sahə:  public int WorkDaysMask { get; set; } = 126;
   Bitmask, .NET DayOfWeek indeksi ilə (Sunday=0 ... Saturday=6):
     bir gün iş günüdürsə həmin bit 1-dir.  126 = 0b1111110 = Bazardan başqa hamısı (bazar bağlı).
   İş günü yoxlaması:  (WorkDaysMask & (1 << (int)date.DayOfWeek)) != 0

3) YENİ ENTITY — src/AttendanceQR.Domain/Entities/NonWorkingDay.cs:
     Id (Guid), Date (DateOnly), Description (string), LocationId (Guid?  null = bütün lokasiyalar)
   AppDbContext-ə DbSet<NonWorkingDay> əlavə et; (Date, LocationId) üzrə indeks.

4) MIGRATION:  dotnet ef migrations add AddWorkCalendar ...

5) HESABLAMA — src/AttendanceQR.Application/Reporting/AttendanceCalculator.cs:
   Compute imzasını dəyiş:
     Compute(AttendanceRecord? record, Location location, TimeZoneInfo tz,
             bool isWorkingDay, DailySummaryStatus noRecordStatus)
   Məntiq:
     - record varsa (check-in var) → İNDİKİ kimi hesabla, AMMA isWorkingDay==false olduqda
       "Late" tətbiq etmə (qeyri-iş günündə gecikmə anlayışı yoxdur → status = OnTime, saatlar sayılsın).
     - record yoxdursa → status = noRecordStatus (Absent ƏVƏZİNƏ çağıran tərəf verir).

6) ÇAĞIRAN TƏRƏFLƏR:
   - DailySummaryService.cs (GenerateForDateAsync): hər işçi üçün həmin tarixin iş günü olub-olmadığını
     hesabla: isWorkingDay = location.WorkDaysMask biti VƏ (o tarix + o lokasiya üçün NonWorkingDay YOXDUR).
     noRecordStatus = isWorkingDay ? Absent : DayOff.  Compute-a ötür.
     NonWorkingDay-ləri metodun əvvəlində bir dəfə oxu (o tarix üçün), lokasiya-null olanlar hamıya aiddir.
   - ReportQueryService.cs canlı "bugün" sorğusu: eyni məntiqi tətbiq et (bugünkü tarix üçün).

7) NonWorkingDay dəyişəndə həmin tarixin DailySummary-si yenilənməlidir:
   Admin əlavə/sil edəndə DailySummaryService.GenerateForDateAsync(date) çağır.

8) ADMIN API — yeni controller AdminCalendarController ([Authorize(Roles="Admin")], "api/admin/non-working-days"):
     GET    /            → bütün qeyri-iş günləri (tarixə görə)
     POST   /            → {date, description, locationId?}  yarat, sonra o tarixi yenidən hesabla
     DELETE /{id}        → sil, sonra o tarixi yenidən hesabla
   Location create/update DTO-suna WorkDaysMask əlavə et (AdminController + Contracts, LocationsPage forması).

9) FRONTEND:
   - frontend/src/api/calendar.ts: getNonWorkingDays, addNonWorkingDay, deleteNonWorkingDay.
   - frontend/src/pages/admin/NonWorkingDaysPage.tsx (theme.css .card/.btn): siyahı + əlavə forması
     (tarix seçici, təsvir, lokasiya seçici "Hamısı" default) + sil düyməsi.
   - AdminLayout.tsx naviqasiyaya "Qeyri-iş günləri" əlavə et; App.tsx-ə route.
   - LocationsPage formasına həftənin 7 günü üçün checkbox qrupu ("B.e Ç.a Çər C.a Cümə Şən Bazar"),
     WorkDaysMask ilə sinxron (default bazar boş).
   - Status etiketləri: DayOff üçün "İstirahət" (neytral boz/mavi badge) — StatusBadge komponentinə əlavə et.

10) TEST (Docker):
   - İş günü, giriş yox → Absent (əvvəlki kimi).
   - Bazar günü, giriş yox → DayOff (İstirahət), QAYIB DEYİL.  ← əsas yoxlama
   - Bazar günü, giriş var → sayılır, gecikmə yox.
   - NonWorkingDay əlavə et (məs. bir çərşənbə) → o gün hamı İstirahət olur; sil → geri Absent.
   Admin login: POST /api/auth/login {email, password}. reports/today və reports/summary yoxla.
```

---

## PROMT 2 — Məzuniyyət & İcazə (icazəli yoxluq + əvəzləşdirmə)

**Problem:** Qanuni yoxluq (məzuniyyət/xəstəlik) və "bazar gəldi, həftə içi getmədi" halı hazırda
"Qayıb" görünür. Admin bunları qeyd edə bilməlidir. **PROMT 1 tamamlandıqdan sonra tətbiq et.**

```
FUNKSİYA: Məzuniyyət və İcazə qeydləri — o günlər "Qayıb" sayılmır.

1) ENUM — DailySummaryStatus.cs, yeni dəyərlər:
     OnLeave = 5      // Məzuniyyət (və xəstəlik, ödənişsiz)
     Permission = 6   // İcazə (qısa/gündəlik icazəli yoxluq, əvəzləşdirmə)

2) YENİ ENUM — src/AttendanceQR.Domain/Enums/LeaveType.cs:
     Vacation = 0 (Məzuniyyət), Sick = 1 (Xəstəlik), Unpaid = 2 (Ödənişsiz), Permission = 3 (İcazə)
   Xəritələmə: Vacation/Sick/Unpaid → OnLeave;  Permission → Permission.

3) YENİ ENTITY — src/AttendanceQR.Domain/Entities/LeaveRecord.cs:
     Id (Guid), EmployeeId (Guid), FromDate (DateOnly), ToDate (DateOnly),
     Type (LeaveType), Note (string?), CreatedByEmployeeId (Guid), CreatedAtUtc (DateTime)
   AppDbContext-ə DbSet; (EmployeeId, FromDate, ToDate) üzrə indeks.

4) MIGRATION:  dotnet ef migrations add AddLeaveRecords ...

5) HESABLAMA — çağıran tərəflərdə (DailySummaryService + ReportQueryService), PROMT 1-dəki
   noRecordStatus təyinini GENİŞLƏNDİR. Prioritet (yalnız check-in YOXDURSA):
     a. həmin tarix işçinin LeaveRecord aralığındadırsa → noRecordStatus =
        (Type==Permission ? Permission : OnLeave)
     b. əks halda iş günü deyilsə → DayOff
     c. əks halda → Absent
   (Check-in VARSA yenə normal işlənmiş kimi hesablanır — məzuniyyətdə gəlibsə, sayılır.)
   DailySummaryService-də həmin tarixi əhatə edən LeaveRecord-ları bir dəfə oxu.

6) LeaveRecord əlavə/silindikdə əhatə etdiyi BÜTÜN tarixlərin DailySummary-si yenilənməlidir:
   FromDate..ToDate arası hər gün üçün GenerateForDateAsync(date) çağır.

7) ADMIN API — AdminLeaveController ([Authorize(Roles="Admin")], "api/admin/leaves"):
     GET    /?from=&to=&employeeId=   → filtrlənmiş siyahı (işçi adı ilə)
     POST   /   → {employeeId, fromDate, toDate, type, note}  yarat + o aralığı yenidən hesabla
     DELETE /{id}   → sil + o aralığı yenidən hesabla
   CreatedByEmployeeId = JWT "sub".

8) HESABAT — ReportModels.cs / ReportQueryService.cs:
   AttendanceReport totals-a LeaveDays (Məzuniyyət) və PermissionDays (İcazə) sayğaclarını əlavə et
   (DailySummary.Status == OnLeave / Permission sayı).

9) FRONTEND:
   - frontend/src/api/leaves.ts: getLeaves, addLeave, deleteLeave.
   - frontend/src/pages/admin/LeavesPage.tsx (theme.css): işçi seçici + tarix aralığı + tip seçici
     (Məzuniyyət/Xəstəlik/Ödənişsiz/İcazə) + qeyd; siyahı + sil. Tipə görə filtr.
   - AdminLayout.tsx-ə "Məzuniyyət / İcazə" menyusu; App.tsx route.
   - StatusBadge: OnLeave → "Məzuniyyət" (yaşıl), Permission → "İcazə" (mavi).

10) TEST (Docker):
   - İşçiyə 3 günlük Vacation ver → o 3 gün OnLeave (Məzuniyyət), Qayıb deyil.
   - Bir günə Permission ver → Permission (İcazə); bu, "əvəzləşdirmə" halını həll edir.
   - Məzuniyyət günündə giriş edilsə → işlənmiş sayılır (leave üstünü örtmür).
   - Sil → status geri qayıdır (Absent və ya DayOff).
   - reports/summary totals-da LeaveDays/PermissionDays düzgündür.
```

---

## PROMT 3 — Qeyd düzəltmə (çıxışı unudan)

**Problem:** İşçi çıxışı unutsa qeyd əbədi "Yarımçıq" qalır; admin düzəldə bilmir. **PROMT 1-dən sonra istənilən vaxt.**

```
FUNKSİYA: Admin bir günün giriş/çıxış saatını düzəltsin və ya çatışmayan qeydi əlavə etsin (audit ilə).

1) ADMIN API — AttendanceController-ə admin əməliyyatları (yeni controller AdminAttendanceController,
   [Authorize(Roles="Admin")], "api/admin/attendance"):
     PUT  /{recordId}   → {checkInAtUtc?, checkOutAtUtc?}  mövcud qeydi yenilə
     POST /             → {employeeId, date, checkInAtUtc?, checkOutAtUtc?}  əl ilə qeyd yarat
                          (o gün üçün qeyd yoxdursa; varsa 409)
   Hər ikisində:
     - AuditLog yaz (yeni AuditEventType: RecordEditedByAdmin) — kim, nə vaxt, hansı qeyd.
     - Dəyişiklikdən sonra həmin (employee, date) üçün DailySummaryService.GenerateForDateAsync(date) çağır.
     - Validasiya: checkOut >= checkIn; tarix gələcəkdə olmasın.

2) FRONTEND:
   - frontend/src/api/attendance.ts-ə: adminUpdateRecord, adminCreateRecord.
   - Mövcud admin görünüşündə (TodayPage və/və ya ReportsPage-də işçi sətri) "Düzəlt" düyməsi →
     kiçik modal: giriş/çıxış saatı seçiciləri (datetime-local), saxla.
   - "Yarımçıq" statuslu qeydlərdə "Çıxışı əlavə et" qısa yolu.

3) TEST (Docker):
   - Yarımçıq qeyd yarat (yalnız check-in) → status Incomplete.
   - Admin çıxış saatını əlavə et (PUT) → status OnTime/Late olur, WorkedMinutes hesablanır.
   - Olmayan gün üçün əl ilə qeyd yarat (POST) → görünür, summary yenilənir.
   - AuditLog-da RecordEditedByAdmin qeydi var.
```

---

## MƏRTƏBƏ 2 (sonra genişləndiriləcək — hazırda qısa cizgi)

### PROMT 4 — Zəngin hesabat paneli
QR Gate-dəki kimi: 12 KPI tile (Toplam giriş/çıxış, Gecikənlər, Erkən ayrılanlar, İşlənən günlər,
Məzuniyyət, İcazə, Koord. xarici, Overtime), Giriş/Çıxış trend qrafiki, Həftənin günləri qrafiki,
TOP-5 gecikən, Ümumi baxış nisbətləri. Data mənbəyi: mövcud DailySummary + reports/summary.
Frontend: DashboardPage genişləndirilir (recharts və ya sadə SVG). Backend: summary endpoint-ə
əlavə aqreqatlar (weekday breakdown, top-5 late). **PROMT 1+2 bitəndən sonra yazılacaq (statuslar lazımdır).**

### PROMT 5 — Bildirişlər
In-app bildiriş (zəng ikonu) + gecikmə/anomaliya xəbərdarlığı. Yeni Notification entity, admin üçün
"gözləyən cihaz-dəyişimi tələbi" və "bugün gecikən" bildirişləri. Kanal: əvvəlcə yalnız in-app
(sonra push/SMS ayrıca qərar). **Ən sonda.**

---

## Tətbiq sırası (xülasə)

1. PROMT 0 (kontekst) + PROMT 1 (iş təqvimi) → test → **deploy təsdiqi**
2. PROMT 0 + PROMT 2 (məzuniyyət/icazə) → test → deploy
3. PROMT 0 + PROMT 3 (qeyd düzəltmə) → test → deploy
4. Mərtəbə 2 promtlarını (4, 5) Opus-dan genişləndir, sonra tətbiq et.

Hər addımdan sonra köhnə funksiyaları da yoxla və yalnız istifadəçi təsdiqləyəndə deploy et.
