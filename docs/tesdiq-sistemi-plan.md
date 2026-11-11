# Struktur və təsdiq sistemi — backend icra planı

Status: **plan** (2026-11-11). Frontend prototipi (`frontend/src/pages/proto/approvals/`, yalnız development build) bu planın
qaydalarını demo məlumatla göstərir. Bu sənəd həmin qaydaların serverdə necə qurulacağını təsvir edir.

Əsas fikir: **yeni səlahiyyət sistemi yazılmır.** Faza 0–5-in modeli (`docs/strukturlar-ve-selahiyyetler.md`) olduğu kimi
qalır: struktur, rol, təyinat və sahə. Üzərinə bir **müraciət** obyekti və onun vəziyyət maşını gəlir. Müraciət
təsdiqlənəndə dəyişikliyi bu gün işləyən eyni kod yolu tətbiq edir.

---

## 1. Nə hazırdır, nə yenidir

| Sahə | Hazırdır (təkrar istifadə) | Yenidir |
|---|---|---|
| Struktur | `Structure`, `Location.StructureId` (şirkətlə kompozit açar), `StructuresController`, köçürmə auditi | Müraciətdə yaradılan anın strukturunun surəti (tarixçə üçün) |
| Rollar | `AccessRole`, `RoleAssignment` (Şirkət / Struktur / Filial sahəsi), şablonlar: `structure_operator`, `timekeeper`, `observer`, `company_admin` | 3 icazə açarı: `requests.create`, `requests.decide`, `requests.view` |
| Sahə yoxlaması | `AccessGraph`, `LocationScopeRules.ScopeAsync(...).Covers(filial)`, `PermissionGate`, `AccessProjection` | Qərar verənin müəyyən edilməsi (§4.2) |
| Rejimlər | Legacy / Shadow / Enforced, `AuthzShadowMiddleware`, `Tenant.AuthzMode` | Şirkət üzrə `approvals` funksiya bayrağı (`TenantFeatures`) |
| İşçi əməliyyatları | `ManagerController` yaratma/redaktə (giriş istisnaları öz hüququnu istəyir), deaktivasiya hüququ, `EmployeeHasHistory` silmə qorunması | Bu məntiqin controller-dən servisə çıxarılması (bir kod yolu) |
| Məzuniyyət | `LeaveRecord` (`Vacation`, `Sick`, `Permission`, …), `LeaveOverlapRule` | Saatlıq icazə üçün `TimeFrom` / `TimeTo` sahələri |
| Davamiyyət düzəlişi | `AdminAttendanceController` yolları, `CorrectionReason` (3–500 hərf), öz gününə düzəliş qadağası, `AttendanceSelfCorrection` (yalnız əlavə olunan jurnal) | — |
| Növbə | `ScheduleAssignmentRule`, `ShiftOverride` | — |
| Audit | `AuditLogs`, `AccessGranted/Revoked` hadisələri | `ChangeRequestEvents` (yalnız əlavə) + 3 yeni `AuditEventType` |
| Bildiriş | `StaffAlerts` (filial üzrə alıcılar), Web Push | «Yeni müraciət» və «qərar verildi» növləri |
| Panel | `/api/me/access`, `useHolds`, `useScopedRole`, 403 ≠ boş siyahı | Ekranlar — prototipdən, mock store API ilə əvəzlənir |

**Bayram günləri bu layihəyə daxil deyil.** Mövcud bayram mexanizminə toxunulmur.

---

## 2. Məlumat modeli

### 2.1 `ChangeRequests`

| Sütun | Tip | Qeyd |
|---|---|---|
| `Id` | uuid | |
| `TenantId` | uuid | sorğu filtri; bütün xarici açarlar `(TenantId, …)` kompozit |
| `Number` | int | şirkət daxilində ardıcıl (ekranda «R-1041») |
| `Type` | smallint | 9 növ (§5) |
| `Status` | smallint | `Pending`, `Approved`, `Rejected`, `Cancelled` |
| `EmployeeId` | uuid? | yeni işçi müraciətində boşdur |
| `LocationId` | uuid | müraciətin filialı |
| `StructureIdAtCreation` | uuid? | yaradılan anın strukturu. Filial sonra köçsə də müraciət öz yerində qalır |
| `DateFrom`, `DateTo` | date? | məzuniyyət, icazə, xəstəlik, düzəliş, növbə |
| `TimeFrom`, `TimeTo` | time? | yalnız saatlıq icazə |
| `Note` | text | «Qeyd / Səbəb», 3–500 hərf, məcburi |
| `Payload` | jsonb | təklif olunan dəyərlər (növə görə sxem) |
| `Snapshot` | jsonb | yaradılan anın dəyərləri: «İndiki → Təklif olunan» müqayisəsi bu sütundan qurulur |
| `CreatedByEmployeeId`, `CreatedAtUtc` | | |
| `DecidedByEmployeeId`, `DecidedAtUtc`, `DecisionNote` | | rədd edəndə səbəb məcburidir |
| `AppliedAtUtc` | timestamptz? | |
| `Direct` | bool | operatorun öz strukturunda birbaşa əməliyyatı |
| `IdempotencyKey` | uuid | klient yaradır; `(TenantId, IdempotencyKey)` unikal |
| `xmin` | | EF concurrency token |

İndekslər:
- `(TenantId, Status, LocationId, CreatedAtUtc)` — növbə;
- `(TenantId, EmployeeId, CreatedAtUtc)` — işçinin tarixçəsi;
- **təkrar gözləyən müraciətin qarşısı** — qismən unikal indeks:
  - işçi dəyişikliyi üçün `(EmployeeId, Type) WHERE Status = Pending`;
  - günlük növlər üçün `(EmployeeId, Type, DateFrom) WHERE Status = Pending`.

### 2.2 `ChangeRequestEvents` — yalnız əlavə olunan jurnal

`Id`, `TenantId`, `RequestId`, `Kind` (`Created`, `Approved`, `Rejected`, `Cancelled`, `Applied`, `ApplyRefused`, `Direct`),
`ActorEmployeeId`, `AtUtc`, `Note`, `Data jsonb`.

- Kod sətri heç vaxt dəyişmir və silmir. `AttendanceSelfCorrection` nümunəsi ilə eynidir.
- Miqrasiyalar yalnız əlavə edir, mövcud cədvəllərə toxunulmur.

---

## 3. Vəziyyət maşını

    Pending ──approve──▶ Approved ──(eyni tranzaksiyada)──▶ tətbiq + Applied hadisəsi
       │ └─reject(səbəb)─▶ Rejected
       └──cancel (yalnız yaradan)──▶ Cancelled

- **Son vəziyyətlər dəyişməzdir.** İkinci qərar 409 `AlreadyDecided` alır.
- **Keçid şərtli `UPDATE` ilə olur:** `… WHERE Status = Pending AND xmin = @version`. Təsirlənən sətir 0-dırsa, qərar
  verilməyib sayılır və cavab 409-dur.
- **Tətbiq alınmırsa** (məsələn, məzuniyyət artıq başqa qeydlə üst-üstə düşür):
  - tranzaksiya geri qaytarılır və müraciət `Pending` qalır;
  - ayrıca tranzaksiyada `ApplyRefused` hadisəsi səbəbi ilə yazılır;
  - operator səbəbi görür və rədd edə bilər.

---

## 4. Hüquq və sahə

### 4.1 Yeni icazələr və şablonlar

| İcazə | Kimdə |
|---|---|
| `requests.create` | Tabelçi (filial üzrə), Struktur operatoru |
| `requests.decide` | Struktur operatoru (struktur üzrə), Şirkət administratoru |
| `requests.view` | Hamısı, öz sahəsində. Müşahidəçi yalnız bunu alır |

Kilidli şablonlar startup-da koddan sinxronlaşır. Kilidsiz şablonlara (operator, tabelçi, müşahidəçi) icazəni sahib
«Rollar» ekranından əlavə edir. Avtomatik əlavə edilmir.

### 4.2 Qərarı kim verir

1. Müraciətin filialını **qərar anında** əhatə edən `requests.decide` sahibləri: əvvəlcə struktur və ya filial səviyyəsində.
   Şirkət administratoru gündəlik təsdiqçi deyil, ona görə onun şirkət səviyyəsindəki hüququ bu addımda nəzərə alınmır.
2. Filial heç bir struktura bağlı deyilsə — şirkət səviyyəsindəki sahiblər (Şirkət administratoru).
3. **Heç kim öz müraciətinə qərar vermir**, administrator da daxil: `CreatedByEmployeeId ≠ qərar verən`.
4. Hüquq qərar anında yenidən oxunur. Müraciət yaradılandan sonra rolu alınan operator qərar verə bilmir.

### 4.3 Birbaşa əməliyyat və tabelçinin birbaşa yazması

- **Operator öz strukturunda** bu günkü endpoint-lərlə birbaşa işləyir. Hər əməliyyat `Direct = true` müraciəti kimi də
  yazılır, beləliklə tarixçə bir yerdə qalır.
- **Tabelçinin birbaşa yazma hüququ** (`employees.update` və s.) — sahibin qərarıdır. `approvals` bayrağı açıq olan
  şirkətdə tabelçi şablonundan bu hüquqlar çıxır və dəyişiklik yalnız müraciətlə olur. Bayraq bağlı olan şirkətdə heç nə
  dəyişmir.

---

## 5. Növlər üzrə tətbiq — mövcud kod yolları

| Növ | Tətbiq edən mövcud məntiq | Qeyd |
|---|---|---|
| Yeni işçi | `ManagerController.CreateEmployee` → `EmployeeService.Create` | vəzifə yalnız kataloqdan; giriş istisnaları öz hüququnu istəyir |
| Məlumat dəyişikliyi | `UpdateEmployee` | **patch kimi tətbiq**: `EmployeeUpdateRequest` göndərilməyən sahəni boşaldır, ona görə tətbiq cari dəyərlər üzərində qurulur |
| Deaktiv / aktiv | deaktivasiya yolu (`employees.deactivate`) | deaktivasiya sessiyanı bağlayır, bu günkü kimi |
| İşçini silmək | `EmployeeHasHistory` qaydası | tarixçəsi olan işçi heç vaxt silinmir. Müraciətlə «zorla silmə» yolu yoxdur, deaktivasiya təklif olunur |
| Saatlıq və ya günlük icazə | `LeaveRecord` (`Permission`) | **yeni:** `TimeFrom` / `TimeTo` |
| Məzuniyyət, xəstəlik | `LeaveRecord` (`Vacation`, `Sick`), `LeaveOverlapRule` | gələcək tarixli məzuniyyət «Qayıb» xülasəsi yaratmır (mövcud qayda) |
| Davamiyyət düzəlişi | `AdminAttendanceController` yolları | səbəb məcburidir; öz gününə düzəliş qadağası qalır |
| Növbə dəyişikliyi | `ScheduleAssignmentRule`, `ShiftOverride` | rotasiya həftəlik təqvimi əvəz edir, üstünə əlavə olunmur (mövcud qayda) |

Gün xülasəsi davamiyyətə təsir edən növlərdən sonra mövcud `IDailySummaryService` ilə yenidən hesablanır (commit-dən
sonra, idempotent).

---

## 6. API

| Endpoint | İcazə | Qeyd |
|---|---|---|
| `GET /api/requests?status&type&locationId&q&page` | `requests.view` | sahəyə görə süzülür, gözləyənlər ən köhnəsi yuxarıda |
| `GET /api/requests/{id}` | `requests.view` | hadisələr, müqayisə və kimin qərar verəcəyi ilə |
| `POST /api/requests` | `requests.create` | `Idempotency-Key` başlığı; təkrar göndəriş eyni müraciəti qaytarır |
| `POST /api/requests/{id}/approve` | `requests.decide` | `If-Match: <version>`; 409 — artıq qərar verilib |
| `POST /api/requests/{id}/reject` | `requests.decide` | gövdə `{ reason }`, 3–500 hərf |
| `POST /api/requests/{id}/cancel` | yaradan | yalnız `Pending` |
| `GET /api/requests/summary` | `requests.view` | panel sayğacları: gözləyən, 24 saatdan çox gözləyən, bu gün qərar verilən |
| `GET /api/structure-tree` | `structures.view` və ya sahə | struktur → filial → təyinatlar → saylar (Struktur xəritəsi üçün) |

Hamısı `[Permission]` daşıyır. `PermissionCoverageTests` yeni endpoint-ləri avtomatik yoxlayır.

---

## 7. Paralel müraciətlər və təkrar təsdiq

- **Eyni müraciətə iki qərar** (iki operator və ya iki klik): `xmin` ilə şərtli yeniləmə. Biri keçir, o biri 409 alır və
  ekran «artıq qərar verilib» deyir.
- **Təkrar göndəriş** (zəif şəbəkədə təkrar cəhd): `IdempotencyKey` unikal indeksi.
- **Eyni işçi üçün iki gözləyən müraciət**: qismən unikal indeks (§2.1). Cavab 409 olur və mövcud müraciətə keçid verilir.
- **Tətbiq zamanı dəyişən məlumat**:
  - müqayisə `Snapshot` ilə aparılır;
  - cari dəyər snapshot-dan fərqlidirsə, tətbiq dayanır;
  - `ApplyRefused` hadisəsi yazılır, səbəb «məlumat arada dəyişib».

---

## 8. Audit

- Hər keçid `ChangeRequestEvents`-ə yazılır, bunlar isə `AuditLogs`-a: `RequestCreated`, `RequestDecided`, `RequestApplied`.
- Operatorun birbaşa əməliyyatı da `Direct` müraciəti kimi yazılır.
- Panelin audit ekranları və platforma konsolu bu hadisələri göstərir.

---

## 9. Bildirişlər

- **Yeni müraciət:** filialın qərar verənlərinə push gedir (`StaffAlerts` yolu, alıcılar `requests.decide` üzrə).
- **Qərar:** müraciəti yaradana push gedir.
- **24 saatdan çox gözləyən:** gündə bir dəfə xülasə.
- Bildiriş heç bir əməliyyatı bloklamır və tətbiqə təsir etmir.

---

## 10. Testlər

| Səviyyə | Nə |
|---|---|
| Vahid | vəziyyət maşını, qərar verənin seçimi (§4.2), öz müraciətinə qərar qadağası, rədd səbəbi, silmə qorunması, patch tətbiqi |
| İcazə | Legacy / Shadow / Enforced matrisi; qapı endpoint metadatasından; `PermissionCoverageTests` |
| Real PostgreSQL (staging probe, geri qaytarılan tranzaksiya) | iki paralel təsdiq, unikal indekslər, tətbiq uğursuz olanda geri qaytarma, `Snapshot` uyğunsuzluğu |
| Frontend | prototipin komponent testləri real API üzərində |

---

## 11. Legacy → Shadow → Enforced

1. **Bayraq bağlı (susmaya görə)** — heç bir şirkətdə heç nə dəyişmir.
2. **Staging `test` şirkəti** — bayraq açılır, probe və əl ilə qəbul.
3. **Prod, bayraq bağlı** — deploy, miqrasiyalar yalnız əlavə edir.
4. **Pilot** — bir şirkətin bir strukturunda bayraq açılır. Yeni endpoint-lər bütün rejimlərdə modelə görə qapılanır
   (`/api/admin/access` kimi), ona görə pilot Enforced-dan asılı deyil.
5. **Enforced** — tabelçinin birbaşa yazma hüququ şablondan çıxarılanda (§4.3). Ayrıca qərar və ayrıca deploy pəncərəsi
   tələb olunur.

Geri qaytarma: bayrağı bağlamaq. Müraciət cədvəlləri qalır, mövcud yollar əvvəlki kimi işləyir.

---

## 12. Sahibin qərarı lazım olanlar

1. Struktursuz filialların müraciətləri Şirkət administratoruna getsin? (Prototip belə göstərir.)
2. `approvals` açılanda tabelçi birbaşa yazma hüququnu itirsin?
3. Saatlıq icazə üçün `LeaveRecord`-a saat sahələri əlavə olunsun, yoxsa ayrıca cədvəl?
4. «24 saatdan çox gözləyən» müraciət üçün bildiriş — kimə, gündə neçə dəfə?
5. Operator məzuniyyətdə olanda kim əvəz edir (müvəqqəti təyinat)?
