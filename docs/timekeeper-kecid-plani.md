# Köhnə menecerlərin Timekeeper roluna keçidi: plan

- **Tarix:** 2026-11-12
- **Mənbə:** `origin/main` (`b304e49`)
- **Status:** yalnız təhlil. Bu sənəd üçün kodda və datada heç nə dəyişdirilməyib.

**Adlar.** Bunlar yalnız ekranda görünən adlardır, texniki id-lər dəyişmir:

| Ad | Texniki id |
|---|---|
| Company Admin | `company_admin` / `EmployeeRole.Admin` |
| Structure Manager | `structure_operator` |
| Timekeeper | `timekeeper` |
| Employee | `EmployeeRole.Employee` |

**«Köhnə menecer»** `Employee.Role == Manager` olan şəxsdir. Sistem ona `legacy_manager` (hər `ManagedLocation` üçün) və `pin_helper` (şirkət səviyyəsində) rollarını avtomatik verir.

## Qısa nəticə

- **Hüquqlar.** Timekeeper şablonu köhnə menecerin hüquqlarının alt çoxluğudur. 35 açardan 19-u qalır, 16-sı itir, yeni açar əlavə olunmur. PIN sıfırlama şirkət boyu deyil, yalnız öz filiallarında qalır.
- **Ən ağır itki: davamiyyət düzəlişi və saxtakarlıq** (`attendance.correct`, `attendance.fraud`). Bu iki hüquq Structure Manager şablonunda da yoxdur. Yəni açıq günlər, «Qayıb» və unudulmuş çıxışlar yalnız Company Admin-ə qalır.
- **Legacy rejimində keçid alınmır.** Rolu Employee olan Timekeeper Legacy-də paneldə yalnız «Mənim sahəm» və təsdiq səhifələrini görür. Keçid yalnız şirkət Enforced-a keçəndən sonra mümkündür.
- **Rolun dəyişməsi logout deməkdir.** `Role` dəyişəndə şəxs sistemdən çıxarılır və `ManagedLocations` silinir. Rol `Manager` saxlanılsa, `pin_helper` (şirkət boyu PIN və nömrə) qalır və Structure Manager adətən bu şəxsin hesabına toxuna bilmir.
- **Shadow bu keçidi ölçə bilmir.** `legacy_manager` menecerin öz datasından qurulur. Ona görə köhnə və yeni qərar menecer üçün həmişə eyni çıxır.

---

## 1. İndiki vəziyyət

### 1.1 Legacy (`AuthzMode = 0`)

| Nə qərar verir | Harada |
|---|---|
| **Endpoint qapısı:** JWT-dəki `role`. Token-in müddəti bitmir, rol dəyişəndə `TokenVersion` onu etibarsız edir. | `[Authorize(Roles = "Manager")]` — `ManagerController.cs` (bütün sinif). `[Authorize(Roles = "Admin,Manager")]` — `AdminAttendanceController.cs`, `AbsenceMarksController.cs`, `AdminMissedCheckoutController.cs`, `AdminDeviceBindingController.cs`, `AdminDeviceChangeController.cs`, `AdminPinResetController.cs`, `AdminPresenceChecksController.cs`, `AdminLocationsController.cs`, `SchedulesController.cs`, `ShiftOverridesController.cs`, `TasksController.cs`, `BirthdaysController.cs`. `FieldVisitController.cs` — idarəetmə endpoint-ləri. `ReportsController.cs` — `payroll`, eləcə də `hr-stats`, `problems`, `geofence-fit`, `shift-mismatch`, `stuck-devices` (`LegacyRoles`). |
| **Filial sahəsi:** menecerin `ManagedLocations` siyahısı. `Employee.LocationId` icazə üçün oxunmur. | `LocationScopeRules.LegacyManagedAsync` |
| **Görmək:** öz filiallarındakı hər kəs, rolundan asılı olmayaraq. | `LocationScopeRules.CanAccessEmployeeAsync` |
| **Toxunmaq:** öz filialında Employee və ya həmkar menecer. Admin olmaz, özü olmaz. | `CanManageEmployeeAsync` |
| **Davamiyyət düzəlişi:** `CanManage` ilə eyni, amma öz günü olmaz. | `CanCorrectAttendanceAsync` |
| **Növbə:** görə bildiyi hər kəs, özü də daxil. | `CanScheduleEmployeeAsync` |
| **Menecer masasının öz qaydaları** | `ManagerController.cs`: `ManageableEmployeeAsync` (filial, Employee və ya həmkar). `CredentialTargetAsync` — PIN və nömrə: şirkətin istənilən adi işçisi və öz filialındakı həmkar menecer. `LeaveSubjectAsync` — filialdakı hər kəs və özü. |
| **Panel:** səhifəni rol açır. Yalnız `MODEL_GATED` səhifələri (`access`, `my-area`, `approvals`, `approvals/operator`, `approvals/branch`) icazə ilə açılır. | `frontend/src/lib/panelAccess.ts` |
| **Bildiriş alanlar:** filialın `ManagedLocations` siyahısındakı `Role == Manager` şəxslər. Belə şəxs yoxdursa, Company Admin-lər. | `StaffAlerts.cs` (Legacy qolu) |
| **İcazə modeli** | `AccessSeeder` hər startda və rol/filial yazısından sonra (`ReconcileAfterWriteAsync`) sistem təyinatlarını qurur. Admin → `company_admin`. Menecer → hər `ManagedLocation` üçün `legacy_manager` və şirkət səviyyəsində `pin_helper`. Bu təyinatı əl ilə ləğv etmək olmur (`AccessRules.CanRevoke` → `SystemAssignment`), əl ilə vermək də olmur (`AdminAccessController.Assign` → `LegacyRole`). |
| **Legacy-də modelin real təsiri** | `/api/me/access`, «Səlahiyyətlər» qapısı (`AdminAccessController`). Təsdiq sistemi (`ApprovalScope`, `ApprovalGate`) hüququ hər rejimdə modeldən oxuyur. |

### 1.2 Shadow (`AuthzMode = 1`)

- Qərarlar Legacy ilə eynidir.
- `AuthzShadowMiddleware` authorization-dan əvvəl işləyir. `[Permission]` olan hər endpoint üçün köhnə qapını modelin qapısı ilə müqayisə edir və fərqi `Gate` və ya `Twin` kimi yazır.
- `AuthzObserver` (`AuthzShadow.cs`) `LocationScopeRules`-un qaydalarını modellə yenidən hesablayır. Qaydalar: `CanAccess`, `CanManage`, `CorrectAttendance`, `CanSchedule`, `CredentialTarget`, `ManageableEmployee`, `LeaveSubject`.
- `ManagedSet` müqayisəsi yalnız çağıran `Role == Manager` olanda aparılır.
- Fərqlər `AuthzShadowEvents` cədvəlinə gündəlik sayğac kimi yazılır (`DbShadowSink`, xam SQL ilə upsert). Bu cədvəli oxuyan kod yoxdur.
- Rejim `Tenants.AuthzMode` sütunundadır. Keşi 30 saniyədir və hər prosesdə ayrıdır (`AuthzObserver.ModeAsync`). Rejimi dəyişən API və ya ekran yoxdur.

### 1.3 Enforced (`AuthzMode = 2`)

- **Qapı:** `PermissionGateHandler` (`PermissionGate.cs`). Bu gün yalnız adminin keçdiyi endpoint, `Scoped` deyilsə, Company Admin-də qalır.
- **Rol:** `AuthzEnforcedMiddleware` token-dəki rolun yerinə `AccessProjection.RoleFor` cavabını qoyur:
  - Company Admin → Admin;
  - endpoint-in icazəsi harasa varsa → Manager forması;
  - qalan hər kəs → Employee.

  `[Permission]` olmayan endpoint-lərə, o cümlədən skana (`AttendanceController` `scan`), toxunulmur.
- **Hesabatlar:** `LocationScopeRules.EffectiveRoleAsync` (`ReportQueryService.cs`): bugünkü lövhə, tabel, xülasə, dashboard, problemlər.
- **Sahə:** `DecideManagedSetAsync`. Bu, `employees.view` ilə sorğunun bütün hüquqlarının kəsişməsidir.
- **Sətir qaydaları:** `ShadowRules.Decide`, `EnforcedRows`. Hesab əməliyyatlarında (PIN, nömrə, profil) qayda ciddidir: yalnız yuxarıdan aşağı (`AccessRules.Outranks`).
- **Bildirişlər:** `StaffAlertDispatcher.ModelRecipientsAsync`.
- **Panel:** `panelAccess.ts` → `modelOpens`; `AccessContext.tsx` → `useScopedRole`, `useHolds`. Panel `MeController`-də yalnız təyinatı olan şəxsə açılır (`panel = təyinat sayı > 0`).
- Xəta olanda cavab «yox»dur (fail-closed).
- **Köhnə menecer üçün Enforced heç nəyi dəyişmir.** O, `legacy_manager` və `pin_helper` daşıyır, ona görə hüququ eyni qalır. Bunu `AuthzEnforcedMatrixTests.The_enforced_gate_takes_nothing_from_todays_admins_and_managers_and_gives_staff_nothing` testi yoxlayır.
- Kodda qəsdən saxlanmış iki istisna var:
  - `Twin` marşrutlar;
  - `Scoped` zəng (`AdminNotificationsController`). Enforced-da bu zəng menecerə də açılır.

  Hüquq rejimlə yox, təyinatla dəyişir.

---

## 2. Hüquq müqayisəsi

Mənbə: `src/AttendanceQR.Domain/Access/AccessRoleTemplates.cs`, `AccessPermissions.cs`.

Qısaltmalar:
- **LM** — `legacy_manager`, Branch sahəsi, hər `ManagedLocation` üçün;
- **PH** — `pin_helper`, Company sahəsi;
- **TK** — `timekeeper` şablonu;
- **SO** — `structure_operator` şablonu. Müqayisə üçündür: itən işi kimin götürə biləcəyini göstərir.

Kilidsiz rolların (TK, SO) şirkətdəki nüsxəsi şablondan fərqli ola bilər, bax §5.0.

| İcazə açarı | LM (+PH) | TK | SO | Fərq | İzah |
|---|---|---|---|---|---|
| `dashboard.view` | ✓ | ✓ | ✓ | eyni | |
| `attendance.view` | ✓ | ✓ | ✓ | eyni | Lövhə, tabel, hesabat, problemlər |
| `attendance.export` | ✓ | ✓ | ✓ | eyni | Xülasə və tabel Excel-i |
| `attendance.photos.view` | ✓ | — | ✓ | **itir** | Giriş selfisi (`AttendanceController` `photo-url`) |
| `attendance.correct` | ✓ | — | — | **itir** | Düzəliş, əl ilə qeyd, açıq gün, «Qayıb», unudulmuş çıxış |
| `attendance.fraud` | ✓ | — | — | **itir** | Günü ləğv, foto xəbərdarlığı, mövcudluq yoxlaması |
| `employees.view` | ✓ | ✓ | ✓ | eyni | |
| `employees.contacts.view` | ✓ | ✓ | ✓ | eyni | |
| `employees.create` | ✓ | ✓ | ✓ | eyni | Təsdiq sahəsində hər iki halda 403 `ApprovalRequired` |
| `employees.update` | ✓ | ✓ | ✓ | eyni* | *Enforced-da hesab qaydası ciddidir: həmkar olmaz (§3) |
| `employees.deactivate` | ✓ | — | ✓ | **itir** | |
| `employees.reset_pin` | PH: şirkət | filial | ✓ | **daralır** | Şirkət boyu → yalnız öz filialları, yalnız yuxarıdan |
| `employees.change_phone` | PH: şirkət | — | ✓ | **itir** | Ayrıca «nömrəni dəyiş» əməliyyatı. Tam redaktədə nömrə yenə dəyişir. |
| `devices.view` | ✓ | ✓ | ✓ | eyni | |
| `devices.approve` | ✓ | ✓ | ✓ | eyni | |
| `devices.revoke` | ✓ | — | ✓ | **itir** | |
| `devices.share_grant` | ✓ | — | — | **itir** | Ortaq telefon (toplu) |
| `field_checkin.grant` | ✓ | — | — | **itir** | «Səyyar» (fərdi və toplu) |
| `checkin_rules.override` | ✓ | — | — | **itir** | Fərdi QR-siz, GPS divarı və foto istisnası |
| `field_visits.view` | ✓ | ✓ | ✓ | eyni | |
| `field_visits.manage` | ✓ | — | ✓ | **itir** | Təyin, yoxlama, ləğv, məcburi çıxış |
| `leaves.view` / `leaves.manage` | ✓ | ✓ | ✓ | eyni | |
| `schedules.view` / `manage` / `assign` | ✓ | ✓ | ✓ | eyni | |
| `payroll.view` | ✓ | — | — | **itir** | |
| `locations.view` | ✓ | — | ✓ | **itir** | «Filiallar», «Radius uyğunsuzluğu» |
| `locations.update` | ✓ | — | — | **itir** | Ad, saat, günlər, GPS nöqtəsi, radius, GPS divarı, QR-siz rejim |
| `locations.geofence_manage` | ✓ | — | — | **itir** | Kodda heç yerdə oxunmur; GPS ayarı `locations.update` altındadır |
| `locations.qr_print` | ✓ | — | ✓ | **itir** | QR posteri |
| `notifications.staff_alerts` | ✓ | ✓ | ✓ | eyni | Kimin aldığı rejimdən asılıdır (§3.2) |
| `tasks.use` | ✓ | — | — | **itir** | |
| `requests.create` / `requests.view` | ✓ | ✓* | ✓* | eyni* | *Şablonda var. 11.11-dən əvvəl yaradılmış şirkət nüsxəsində yoxdur. |
| `requests.decide` | — | — | ✓* | eyni | Köhnə menecer müraciəti təsdiqləmir |
| `assignments.view` | — | — | ✓ | eyni | |

**Açar olmayan fərqlər:**

| Xüsusiyyət | LM / PH | TK |
|---|---|---|
| Sahə | LM hər `ManagedLocation` üçün ayrıca. PH bütün şirkət. | Təyin olunduğu filial və ya struktur. Struktur seçiləndə ora sonradan qoşulan filiallar da avtomatik daxil olur (`AccessGraph`). |
| Mənbə | `Role` və `ManagedLocations` sahələri. Kilidlidir, hər startda koddan bərpa olunur. | Əl ilə təyinat. Rol kilidsizdir, şirkət onu redaktə edə bilər və dəyişiklik bütün sahiblərə birdən təsir edir. |
| Həmkar qaydası (`PeersManageable`) | Var: öz filialındakı həmkar menecerə toxunur. | Yoxdur: ciddi qayda, yalnız yuxarıdan aşağı. |
| Hesab əməliyyatında hədəf | Şirkətin istənilən adi işçisi (PH) və öz filialındakı həmkar. | Öz filialının adi işçisi. Başqa Timekeeper və köhnə menecer olmaz. |

---

## 3. İtiriləcək funksiyalar

### 3.1 Ekran və əməliyyatlar

Enforced-da, köhnə menecer Timekeeper şablonu ilə əvəz olunanda itən imkanlar. Sonuncu sütun: işi Structure Manager şablonu götürə bilirmi?

| Funksiya | Ekran | Server | Açar | SO? |
|---|---|---|---|---|
| Saat redaktəsi, əl ilə qeyd, açıq günü bağlamaq, çıxışı ləğv etmək, günü silmək | Bugünkü davamiyyət, İşçi profili, Çıxışı unudulan günlər | `AdminAttendanceController`: `PUT {id}`, `POST`, `POST close-open`, `POST {id}/clear-checkout`, `DELETE {id}` | `attendance.correct` | **yox**, yalnız Company Admin |
| «Qayıb yaz» | Bugünkü davamiyyət, profil | `AbsenceMarksController`: `POST`, `DELETE` | `attendance.correct` | **yox** |
| Unudulmuş çıxış sorğusunu təsdiq və ya rədd etmək | Unudulmuş çıxış | `AdminMissedCheckoutController`: `approve`, `reject` | `attendance.correct` | **yox** |
| Günü saxtakarlıq kimi ləğv etmək, bərpa etmək, foto xəbərdarlığı | Lövhə, profil | `AdminAttendanceController`: `void-fraud`, `unvoid`, `send-photo-warning` | `attendance.fraud` | **yox** |
| Mövcudluq yoxlaması (oxumaq, açmaq) | İşçi profili | `AdminPresenceChecksController` | `attendance.fraud` | **yox** |
| Giriş selfisi | Lövhə, profil (`mayViewPhotos`) | `AttendanceController` `photo-url` | `attendance.photos.view` | bəli |
| İşdən çıxarmaq və geri qaytarmaq | İşçilər, profil | `ManagerController.UpdateEmployee` → `MayDeactivateAsync` | `employees.deactivate` | bəli |
| Başqa filialın adi işçisinə PIN; öz filialındakı həmkar menecerə PIN | İşçilər, PIN axtarışı | `ManagerController.ResetPin`, `CredentialTargets` | PH `employees.reset_pin` | öz strukturunda |
| Giriş nömrəsini ayrıca dəyişmək | PIN və nömrə masası | `ManagerController.ChangePhone` | `employees.change_phone` | öz strukturunda |
| Telefon bağlantısını ləğv etmək | Cihazlar | `AdminDeviceBindingController`: `revoke`, `device/revoke-all` | `devices.revoke` | bəli |
| Ortaq telefon icazəsi (toplu) | İşçilər | `ManagerController` `bulk-permission/share-device` | `devices.share_grant` | **yox** |
| Səyyar giriş hüququ (fərdi və toplu) | İşçi forması, toplu əməliyyat | `CheckInExceptionsRefusalAsync`, `bulk-permission/field-check-in` | `field_checkin.grant` | **yox** |
| Fərdi QR-siz, GPS divarı, foto istisnası | İşçi forması | `CheckInExceptionsRefusalAsync` (dəyişiklikdə 403) | `checkin_rules.override` | **yox** |
| Sahə ziyarəti: təyin, yoxlama, ləğv, məcburi çıxış | Sahə ziyarətləri | `FieldVisitController`: `POST`, `review`, `cancel`, `force-checkout` | `field_visits.manage` | bəli |
| Filiallar səhifəsi, filial ayarları, GPS nöqtəsi və radius | Filiallar | `AdminLocationsController`: `GET`, `PUT {id}` | `locations.view`, `locations.update` | yalnız baxır |
| QR posteri çapı | Filiallar → QR çapı | `AdminLocationsController` `static-qr` | `locations.qr_print` | bəli |
| Radius uyğunsuzluğu | Radius uyğunsuzluğu | `ReportsController` `geofence-fit` (içəridə `locations.view` yoxlanır) | `locations.view` | bəli |
| Maaş cədvəli | Maaş | `ReportsController` `payroll` | `payroll.view` | **yox** |
| Tapşırıqlar lövhəsi | Tapşırıqlar | `TasksController` | `tasks.use` | **yox** |
| Həmkar qaydası: öz filialındakı həmkar menecerin profili, PIN-i, cihazı | İşçilər, profil | `ShadowRules.ManageableEmployee` / `CredentialTarget` (ciddi), `AccessRules.Dominates` | — | — |

**Dəyişməyənlər** (Timekeeper-də qalır):
- bugünkü lövhə (sahə: öz filialları);
- «Səbəb seçin», məzuniyyət və icazə;
- tabel və onun Excel-i;
- xülasə, dashboard;
- problemlər, növbə uyğunsuzluğu;
- doğum günləri;
- işçi əlavə etmək və redaktə etmək;
- öz filialında PIN sıfırlama növbəsi;
- yeni telefonu təsdiqləmək;
- növbələr və günlük növbə dəyişikliyi;
- sahə ziyarətlərinə baxmaq;
- «Yeni işçi» müraciəti (bax §5.0).

Bunu `AuthzEnforcedMatrixTests.A_timekeeper_reaches_the_daily_work_of_their_branch_and_nothing_that_corrects_pay` testi təsdiqləyir.

### 3.2 Bildirişlər

| Bildiriş | Legacy (bu gün) | Enforced, köhnə menecer yerində | Enforced, Timekeeper-ə keçəndən sonra |
|---|---|---|---|
| Skan edə bilmir və s. | Filialın menecerləri, yoxdursa Company Admin | `staff_alerts` daşıyan yerli şəxslər: LM və SO (struktur sahəsi də yerli sayılır) | Timekeeper və Structure Manager |
| Yeni telefon | Eyni | `devices.approve` + `staff_alerts` | Timekeeper və Structure Manager |
| PIN unudulub | Hədəf adi işçidirsə menecerlər, əks halda Company Admin | `CredentialTarget` qaydası ilə | Adi işçi üçün Timekeeper. Timekeeper-in öz PIN-i üçün yalnız onu üstələyən şəxs. |
| Filialdan kənarda (mövcudluq) | Menecerlər | `attendance.fraud` daşıyanlar | TK və SO-da `attendance.fraud` yoxdur, ona görə **yalnız Company Admin** |

Mənbə: `StaffAlerts.cs` (`HandleAsync`, `ModelRecipientsAsync`).

Enforced-da Structure Manager strukturunun **hər filialının** bildirişlərini alır, çünki struktur sahəsi şirkət sahəsi deyil, «yerli» sayılır. Bu gün belə deyil.

### 3.3 Rola bağlı yerlər: icazədən asılı deyil

- **Legacy və Shadow-da panel.** `panelAccess.ts`-də `roles: BOTH` olan bütün səhifələr `Role == Manager` ilə açılır. Server də `[Authorize(Roles)]` ilə qərar verir; model heç nəyi dəyişmir.
- **Legacy-də rolu Employee olan Timekeeper** yalnız `MODEL_GATED` səhifələri açır: «Mənim sahəm», «Təsdiq mərkəzi», «Mənim filialım». «Səlahiyyətlər» `assignments.view` istəyir, Timekeeper-də yoxdur. Qalan hər səhifə «keçiddən sonra açılacaq» deyir (`AdminRoute.tsx` → `PanelPage`).
- **Hər rejimdə DB və ya token rolu oxuyan yerlər** (variant B/C üçün vacibdir):
  - `frontend/src/lib/jwt.ts` → `roleHome`. Employee girişdən sonra `/home`-a düşür, panelə `MenuPage.tsx` menyusundakı «İdarəetmə paneli» ilə keçir.
  - `TasksController` (`assignable`, `assign`). Tapşırıq yalnız `Role` Admin və ya Manager olan şəxsə verilə bilər.
  - Gözləyən siyahılar hədəfi DB-də `e.Role == Employee` ilə süzür: `AdminDeviceChangeController.Pending`, `AdminPinResetController.Pending`, `AdminMissedCheckoutController.Pending`, `ManagerController.BulkGrantAsync`. Rolu Employee olan Timekeeper başqalarının siyahısında adi işçi kimi görünür.
  - `ManagerController.Employees` → `isColleague` sahəsi DB rolundan hesablanır.
  - `AdminController.ApplyManagedLocationsAsync`. Rol Manager deyilsə, `ManagedLocations` silinir.
  - `StaffAlerts.cs` və `SelfCorrection.cs`, Legacy qolu: `Role == Manager` + `ManagedLocations`.
  - `VoteController` (nəticələri kim görür). Modul hazırda bağlıdır.

### 3.4 Qazanılanlar

- **Bir mənbə.** Hüquq yalnız təyinatdan gəlir. `ManagedLocations` arxa qapı kimi qalmır. Variant C-dən sonra hər növbəti dəyişiklik logout-suz olur (`AdminAccessController`, `EffectiveAccess` hər sorğuda bazadan oxunur).
- **Struktur sahəsi.** Strukturda açılan yeni filial təyinatı yeniləmədən əhatəyə düşür.
- **Structure Manager hesaba toxuna bilir:** PIN, redaktə, deaktiv, telefon təsdiqi. Şərt: Timekeeper-in hər hüququ Structure Manager-də də eyni sahədə olmalıdır (`AccessRules.Dominates`, `Outranks`). Köhnə menecerə bu gün yalnız özü də `legacy_manager` daşıyan Structure Manager toxuna bilir.
- **Daha dar əhatə:**
  - şirkət boyu PIN və nömrə yoxdur;
  - selfi, maaş və filialın GPS ayarına yazma yoxdur.

  GPS radiusunu səhv dəyişən daha az adam olur. Bu, skan uğursuzluqlarının tanış mənbəyidir.
- **Təsdiq axını dəyişmir.** «Yeni işçi» müraciəti eyni yoldan gedir (`ApprovalGate.DirectChangeAsync`, `ApprovalScope.MayRequest`).

---

## 4. Keçid variantları

| | **A. Rol saxlanır, Timekeeper əlavə olunur** | **B. Rol saxlanır, filiallar silinir, Timekeeper əlavə olunur** | **C. Rol Employee olur, Timekeeper əlavə olunur** |
|---|---|---|---|
| Nə edilir | `Role = Manager` və `ManagedLocations` toxunulmur. Timekeeper təyinatı verilir. | Şirkət Enforced-dadır. `ManagedLocations` boşaldılır, seeder `legacy_manager`-i ləğv edir. Timekeeper verilir. | Əvvəl Timekeeper verilir, sonra işçi formasında rol Employee edilir. |
| Real hüquq | Dəyişmir: LM ∪ PH ∪ TK = LM ∪ PH | TK ∪ PH: şirkət boyu PIN və nömrə qalır | Yalnız TK |
| Logout | Yox | Yox | **Bəli**. `AdminController.Update` rol dəyişəndə `TokenVersion++` edir. |
| Legacy-də işləyirmi | Bəli, heç nə dəyişmir | **Yox**. `ManagedLocations` boşdursa, Legacy-də heç nə görmür. | **Yox**. Panel demək olar ki, bağlanır. |
| Structure Manager hesabına toxuna bilirmi (Enforced) | Yox | Yox (`pin_helper` şirkət sahəsindədir). Structure Manager özü də `Role = Manager` deyilsə. | Bəli, şirkətin rol nüsxələri alt çoxluq qaydasını saxlayırsa (§5.0) |
| DB rolu yolları (§3.3) | Manager | Manager kimi qalır: tapşırıq siyahısı, `/admin`-ə giriş, admin siyahısında «Menecer» | Employee kimi |
| Kövrəklik | — | Kimsə işçi formasında filial əlavə etsə, `legacy_manager` səssizcə geri qayıdır | — |
| Geri qaytarma | Təyinatı ləğv etmək | `ManagedLocations`-u bərpa etmək, logout yoxdur | Rolu Manager etmək (**ikinci logout**) və `ManagedLocations`-u saxlanmış siyahıdan bərpa etmək |
| Faydası | Hazırlıqdır: «Mənim sahəm» və etiket. Keçid deyil. | Logout-suz yarım keçid | Təmiz son vəziyyət |

**D. Şirkətin öz rolu («Tabelçi+»).** B və ya C ilə birlikdə işlədilə bilər. «Rollar» ekranında Timekeeper-in surəti yaradılır və itən hüquqlardan seçilənlər əlavə olunur (`AccessPage.tsx`-də nümunə: «Tabelçi, plus attendance correction»).

Məhdudiyyət: bu rol Structure Manager rolunun alt çoxluğu qalmalıdır. Məsələn, `attendance.correct` əlavə edilsə, amma Structure Manager-də olmasa, Structure Manager bu şəxsə toxuna bilmir: nə PIN, nə redaktə, nə telefon təsdiqi (`AccessRules.Dominates`).

**Logout barədə:**
- Token-in müddəti bitmir, «təbii» çıxış anı yoxdur (`Employee.cs`: refresh flow yoxdur).
- Rolu SQL ilə, `TokenVersion` artırmadan dəyişmək olmaz. Köhnə token `role = Manager` ilə qalar, bu da `ClaimsPrincipalExtensions.Role()`-un fərziyyəsini pozur.
- C seçilsə, logout planlı olmalıdır:
  - şəxs yerində olur;
  - PIN-i bilir və ya Structure Manager dərhal yenisini verə bilir.

---

## 5. Legacy → Shadow → Enforced riskləri

### 5.0 Başlamazdan əvvəl: şirkət nüsxələri

- **Nüsxə bir dəfə çıxarılır.** `timekeeper` və `structure_operator` kilidsizdir. Şirkətə rollar ilk yaradılanda bir dəfə köçürülür və sonra şirkətin özünündür (`AccessSeeder.EnsureTemplatesAsync`). Mövcud şirkətlərdə bu rollar 2026-10-15 şablonundandır: `requests.*` yoxdur (`docs/tesdiq-sistemi-faza1.md`, staging addım 3).
- **Risk 1: request hüquqları.** Timekeeper nüsxəsində `requests.create` yoxdursa, təsdiq sahəsindəki filialda Timekeeper nə birbaşa əlavə edə, nə də müraciət göndərə bilər. Səbəb: `ApprovalGate` 403 qaytarır, `canRequest: false`.
- **Risk 2: alt çoxluq qaydası.** Timekeeper nüsxəsi Structure Manager nüsxəsinin alt çoxluğu deyilsə, Structure Manager Timekeeper-ə toxuna bilmir.
- **Struktur təyinatı.** Structure Manager-in `structure_operator` təyinatı **struktur** səviyyəsində olmalıdır (`ApprovalScope.IsApprover`, `ApproverCandidates`).
- **Təyinatı kim verir.** Timekeeper-i Company Admin təyin edir. Şablonda Structure Manager-də `assignments.manage` yoxdur. Əlavə edilsə belə, rolu hələ `Manager` olan şəxsə Timekeeper verə bilməz: `AccessRules.CanGrant` → `AssigneeNotBelowYou`.

### 5.1 Addımlar

| Addım | Kimə təsir edir | Nə sına bilər | Necə izlənir | Nə çatışmır |
|---|---|---|---|---|
| **1. Legacy → Shadow** (bütün şirkət) | Heç kimə: qərarlar dəyişmir | Hər panel sorğusunda əlavə qraf yüklənir, fərq olanda `AuthzShadowEvents`-ə yazılır. Yükü ölçülməyib. | `AuthzShadowEvents` (bax §6). Gözlənilən nəticə: admin və menecerlər üçün `Gate`, `CanManage`, `ManagedSet` sıfırdır, yalnız `Twin` ola bilər. | Rejim açarı yoxdur, dəyişiklik DB-də əllə edilir. Hesabat ekranı yoxdur, SQL ilə oxunur. **Shadow bu keçidi ölçmür**: `legacy_manager` eyni datadan qurulur. O, yalnız Enforced-ın bugünkü istifadəçilərə heç nə etmədiyini sübut edir. |
| **2. Shadow → Enforced** (hələ heç kim köçürülməyib) | Paneldəki hər kəs: admin, köhnə menecerlər, Structure Manager. Skana təsir etmir. | Zəng (`Scoped`) menecerlərə açılır. Bildiriş alanlar modelə keçir: Structure Manager hər filialın bildirişini alır, «kənarda» bildirişləri `attendance.fraud` daşıyana gedir. Qraf oxunmasa, panel sorğuları rədd olunur (fail-closed). | Tətbiq loqunda «Authz enforced: … refused» yazıları, 403 sayı. `AuditLogs` → `StaffAlertSent` (Reason: `növ\|çatan/alıcı menecer\|admin`). | Enforced-da fərq jurnalı yoxdur: kölgə yalnız Shadow-da yazır. 403-ləri ayrıca toplayan yer yoxdur. |
| **3. Şəxs-şəxs keçid** (Enforced-da, variant B və ya C) | Köçürülən şəxs. Onun filialının işçiləri: gün düzəlişi, deaktiv, cihaz ləğvi kimə qalır. Structure Manager: iş artır. Company Admin: düzəliş və saxtakarlıq ona qalır. | Açıq günlər bağlanmır (0 saat). Unudulmuş çıxışlar yığılır. «Kənarda» yoxlamalarına baxan olmur. C-də şəxs çıxarılır. Rol nüsxəsində `requests.create` yoxdursa, «Yeni işçi» tam bağlanır. | `AuditLogs`: `AccessGranted` / `AccessRevoked`. «Çıxışı unudulan günlər» sayı. Unudulmuş çıxış növbəsi. PIN və cihaz növbələri. Structure Manager-dən gələn şikayətlər. | Kim nəyi itirdi — avtomatik hesabat yoxdur. §3.1-dəki cədvəl əl ilə yoxlanır. |
| **4. Bütün köhnə menecerlər köçəndən sonra** | Şirkət | `legacy_manager` boşalır. Yeni menecer təyin edilsə (işçi formasında Manager), köhnə qayda yenidən yaranır. | Sistem təyinatlarının sayı (§6, sorğu 3) | Formada «Menecer» seçimini bağlamaq bu planın hissəsi deyil |

### 5.2 Ən böyük risklər

1. **Davamiyyət düzəlişi boşluğu.**
   - `attendance.correct` və `attendance.fraud` nə Timekeeper, nə Structure Manager şablonunda var.
   - Köhnə menecer gedəndən sonra filialın açıq günlərini, «Qayıb»ı və unudulmuş çıxışı yalnız Company Admin düzəldir.
   - Bağlanmamış gün 0 saat sayılır. Bu, maaşa birbaşa təsir edir.
2. **Logout və ya yarımçıq keçid.**
   - C şəxsi çıxarır və geri qaytarma ikinci logout-dur.
   - B `pin_helper`-i saxlayır və Structure Manager-i bu şəxsin hesabından kənarda qoyur.
   - Legacy şirkətdə C şəxsi panelsiz qoyur.
3. **Şirkət rol nüsxələri.**
   - Nüsxələr kilidsizdir, redaktə olunur və 11.11-dən əvvəlki şirkətlərdə request hüquqları yoxdur.
   - Səhv nüsxə ya «Yeni işçi» yolunu bağlayır, ya da Structure Manager-in Timekeeper üzərindəki üstünlüyünü pozur.
   - Rolun redaktəsi bütün sahiblərə eyni anda təsir edir.
4. **Alət çatışmazlığı.**
   - Rejim açarı yoxdur, DB-də əllə dəyişilir.
   - Shadow hesabatı yoxdur.
   - Enforced-da rəddlərin jurnalı yoxdur.
   - Shadow bu keçidi ölçə bilmir.
5. **Bildirişlərin yenidən paylanması.**
   - Structure Manager strukturun bütün filiallarının bildirişlərini alır.
   - «Kənarda» bildirişləri Company Admin-ə keçir.

---

## 6. Staging test planı

Yalnız staging-dəki test şirkətində aparılır. Rejim dəyişikliyini və DB sorğularını operator edir. Hər addımın nəticəsi qeyd olunur. `:t` test şirkətinin `TenantId`-sidir.

1. **Hazırlıq**
   - Staging `origin/main` ilə eyni commit-dədir.
   - Test şirkətinin indiki rejimi oxunur: `SELECT "AuthzMode" FROM "Tenants" WHERE "Id" = :t;`. Əvvəlki fazalarda bu şirkət Enforced-a keçirilmiş ola bilər.
   - `Approvals__Enabled` dəyəri yoxlanılır.
   - Staging bazasının nüsxəsi götürülür.
2. **Inventar (yalnız oxuma)**
   ```sql
   -- (1) şablon rolların şirkət nüsxələri
   SELECT "TemplateKey", "IsLocked", "Permissions" FROM "AccessRoles"
   WHERE "TenantId" = :t AND "TemplateKey" IS NOT NULL AND "ArchivedAtUtc" IS NULL;
   -- (2) timekeeper ⊂ structure_operator?
   SELECT (SELECT "Permissions" FROM "AccessRoles" WHERE "TenantId" = :t AND "TemplateKey" = 'timekeeper')
       <@ (SELECT "Permissions" FROM "AccessRoles" WHERE "TenantId" = :t AND "TemplateKey" = 'structure_operator');
   -- (3) canlı təyinatlar (system = AccessSeeder-in)
   SELECT a."EmployeeId", r."TemplateKey", a."Scope", a."StructureId", a."LocationId",
          a."CreatedByEmployeeId" IS NULL AS system
   FROM "RoleAssignments" a JOIN "AccessRoles" r ON r."Id" = a."AccessRoleId"
   WHERE a."TenantId" = :t AND a."RevokedAtUtc" IS NULL;
   -- (4) köhnə menecerlər və filialları — GERİ QAYTARMA ÜÇÜN SAXLANIR
   SELECT e."Id", e."LocationId", array_agg(m."LocationId") AS managed
   FROM "Employees" e LEFT JOIN "ManagedLocations" m ON m."EmployeeId" = e."Id"
   WHERE e."TenantId" = :t AND e."Role" = 1 GROUP BY e."Id", e."LocationId";
   ```
3. **Test dünyası.** Hamısı test hesablarıdır, `frontend/e2e/README.md`-dəki yolla yaradılır.
   - Struktur S, içində filiallar F1 və F2. Strukturdan kənarda filial F3.
   - Structure Manager: `structure_operator`, S səviyyəsində.
   - Köhnə menecerlər: M1 (idarə etdiyi filial F1, öz filialı F1), M2 (idarə etdiyi filiallar F1 və F2, öz filialı F1, yəni M1-in həmkarı), M3 (F3).
   - F1, F2, F3-də adi işçilər. Bir Company Admin.
   - S üçün təsdiq sahəsi.
   - Rol nüsxəsinə əskik request hüququ lazımdırsa, yalnız «Rollar» ekranından əlavə olunur (`AccessRoleChanged` auditi yazılır).
4. **Legacy-də ilkin ölçmə.** Hər yoxlama M1 və M2 üçün aparılır, nəticə qeyd olunur:
   - **Filiallar:** `GET /api/reports/my-locations`, işçi siyahısı.
   - **Bugünkü lövhə:** sətir sayı F1 üçün, bir keçmiş tarix üçün də.
   - **Tabel:** ay, `tabel/export`.
   - **Sahə ziyarətləri:** siyahı və təyin.
   - **Təsdiq:** F1-də birbaşa işçi əlavəsi → 403 `ApprovalRequired`, `canRequest: true`. Müraciət göndərilir → 201. Structure Manager təsdiqləyir → hesab yaranır. F3-də birbaşa əlavə → 200.
   - **PIN:** F3-dəki adi işçiyə → 200 (şirkət boyu). M2-yə (həmkar) → 200.
   - **Telefon:** F1-dəki işçinin yeni telefonunu təsdiq. Bir bağlantının ləğvi.
   - **Gün:** unudulmuş çıxış təsdiqi. Açıq günün bağlanması.
   - **Bildirişlər:** F1-də cihaz sorğusu və PIN sorğusu açılır. `AuditLogs` → `StaffAlertSent` sətrinin `Reason`-u: kim aldı.
5. **Shadow.**
   - Rejim 1-ə keçirilir, ən azı 30 saniyə gözlənilir.
   - 4-cü addım təkrarlanır, nəticələr eyni olmalıdır.
   - Fərqlər oxunur:
     ```sql
     SELECT "Day", "Kind", "Subject", "Legacy", "New", SUM("Count") AS n, COUNT(DISTINCT "EmployeeId") AS people
     FROM "AuthzShadowEvents" WHERE "TenantId" = :t AND "Day" >= :since
     GROUP BY 1,2,3,4,5 ORDER BY n DESC;
     ```
   - Gözlənilən: `Twin`-dən başqa sətir yoxdur. Hər `Gate` və ya qayda sətri izah olunana qədər növbəti addıma keçilmir.
6. **Enforced, hələ heç kim köçürülməyib.**
   - Rejim 2-yə keçirilir.
   - 4-cü addım təkrarlanır, nəticələr eyni olmalıdır.
   - İki gözlənilən fərq:
     - M1 və M2-də zəng (badge) görünür;
     - F1 bildirişlərini Structure Manager də alır.
7. **Keçid.** Hər variant ayrı şəxsdə sınanır:
   - **M1 → C:** əvvəl Timekeeper F1 üçün verilir, sonra rol Employee edilir.
   - **M2 → B:** Timekeeper F1 və F2 üçün verilir, sonra `ManagedLocations` boşaldılır.
   - İkinci sınaq: Timekeeper filial yox, S səviyyəsində verilir.
8. **Keçiddən sonra yoxlama** (M1 və M2 üçün)

   | Yoxlama | Gözlənilən |
   |---|---|
   | C: köhnə token | 401, yenidən giriş. Girişdən sonra `/home` açılır, menyuda «İdarəetmə paneli» var. |
   | «Mənim sahəm» | Timekeeper rolu, filiallar |
   | `my-locations`, işçi siyahısı, bugünkü lövhə | Yalnız təyin olunan filiallar. F1-in sayı 4-cü addımdakı ilə eyni. |
   | Lövhədə selfi | Görünmür (`attendance.photos.view` yoxdur) |
   | «Səbəb seçin», məzuniyyət | İşləyir |
   | Əl ilə qeyd, «Qayıb», açıq gün, unudulmuş çıxış | 403 və ya düymə yoxdur. Eyni işi Structure Manager də edə bilmir, yalnız Company Admin edir. |
   | Tabel və Excel | İşləyir |
   | Sahə ziyarəti | Siyahı var, təyin 403 |
   | Təsdiq | 4-cü addımla eyni: F1-də müraciət, Structure Manager qərar verir |
   | PIN | F1-in adi işçisinə 200. F3-ün adi işçisinə C-də rədd, B-də 200 (`pin_helper`). M2-yə (həmkar) rədd. |
   | Structure Manager → Timekeeper-ə PIN, redaktə, deaktiv | C-də 200. B-də rədd, Structure Manager özü `Role = Manager` deyilsə. |
   | Telefon | Təsdiq 200, ləğv 403. İki Timekeeper bir-birinin telefonunu təsdiqləyə bilirmi — yoxlanır, kodla görə bəli (`CanManage` ciddi deyil). |
   | Deaktiv | 403 |
   | Filiallar, QR çapı, radius uyğunsuzluğu, maaş, tapşırıqlar | Səhifə bağlıdır |
   | Bildirişlər | F1 cihaz sorğusu → Timekeeper və Structure Manager. F1 PIN sorğusu → Timekeeper. «Kənarda» (sınanarsa) → Company Admin. |
   | M3 (köçürülməyib) | 4-cü addımla eyni |

9. **Avtomatik testlər.**
   - `frontend/e2e/03-scope.spec.ts` və `04-tabelci.spec.ts` staging-ə qarşı işlədilir.
   - Lokal mühitdə `dotnet test` və `cd frontend && npm run build`.
10. **Geri qaytarma məşqi.** §7-dəki addımlar staging-də sınanır və nəticələri qeyd olunur.
11. **Təmizlik.**
    - Test təyinatları ləğv olunur.
    - Rol nüsxəsinə edilən əlavələr geri alınır.
    - Rejim əvvəlki dəyərinə qaytarılır.
    - Test `AuthzShadowEvents` sətirləri silinir.

---

## 7. Geri qaytarma planı

Ən təhlükəsizdən başlayaraq:

| # | Nə geri alınır | Necə | Logout | Nə vaxt qüvvəyə minir | Qeyd |
|---|---|---|---|---|---|
| 1 | Rol nüsxəsinə əlavə edilən hüquq | «Rollar» ekranı | Yox | Növbəti sorğudan | Rolun bütün sahiblərinə təsir edir |
| 2 | Timekeeper təyinatı (variant A) | «Səlahiyyətlər» → ləğv | Yox | Növbəti sorğudan | Sətir silinmir, kim və nə vaxt ləğv etdiyi qalır |
| 3 | Variant B | Admin işçi formasında `ManagedLocations`-u §6, sorğu 4 siyahısından bərpa edir, sonra Timekeeper ləğv olunur | Yox | Dərhal: `ReconcileAfterWriteAsync` `legacy_manager`-i qaytarır | |
| 4 | Enforced → Shadow | DB: `"AuthzMode" = 1` | Yox | 30 saniyəyə qədər (keş) | C ilə köçürülən şəxslər paneli itirir. **Əvvəl 5-ci addım edilir.** |
| 5 | Variant C | Rol Manager edilir, `ManagedLocations` saxlanmış siyahıdan bərpa olunur, Timekeeper ləğv olunur | **Bəli, ikinci dəfə** | Yenidən girişdən sonra | Siyahı saxlanmayıbsa, filiallar əl ilə yığılmalıdır. Rol dəyişəndə `ManagedLocations` silinir. |
| 6 | Shadow → Legacy | DB: `"AuthzMode" = 0` | Yox | 30 saniyəyə qədər | Heç kim hiss etmir. Kölgə sətirləri qalır. |

**Qaydalar:**
- Rejim dəyişikliyi və şəxslərin keçidi pik saatlarda edilmir: 07:30–09:30 və 17:00–19:00.
- Enforced skanı dəyişmir, amma paneldə səhər cihaz və PIN təsdiqləri gedir.

---

## 8. Açıq qərarlar

1. **Variant:** B (logout yoxdur, `pin_helper` qalır) və ya C (təmiz keçid, logout). C seçilsə, logout necə təşkil olunur: şəxs yerində, Structure Manager yanında.
2. **Davamiyyət düzəlişi və saxtakarlıq** (`attendance.correct`, `attendance.fraud`) kimdə olacaq?
   - Yalnız Company Admin.
   - Structure Manager rolunun şirkət nüsxəsinə əlavə edilir.
   - «Tabelçi+» rolu yaradılır (D). Bu halda Structure Manager-də də olmalıdır.
3. **Şirkət boyu PIN və nömrə imkanı** (2026-09-14 qərarı) Timekeeper-də qalsın, yoxsa yox?
4. **Həmkar qaydası** (2026-09-18 qərarı) Timekeeper-lər arasında itir. Qəbul olunurmu?
5. **Qalan itkilər kimdə olacaq:** deaktiv, cihaz ləğvi, sahə ziyarəti idarəsi, Səyyar, QR-siz və foto istisnaları, filial ayarları, QR çapı, maaş, tapşırıqlar. Hər biri Structure Manager-ə, Company Admin-ə və ya «Tabelçi+»-a.
6. **Timekeeper-in sahəsi:** filial və ya struktur.
7. **Timekeeper-i kim təyin edir:** yalnız Company Admin, yoxsa Structure Manager-ə `assignments.manage` verilir (C-dən sonra mümkündür).
8. **Bildirişlər:** Structure Manager-in bütün filialların bildirişlərini alması və «kənarda» bildirişlərinin Company Admin-ə keçməsi qəbul olunurmu?
9. **Alətlər:** rejim əllə, DB-də dəyişilir, yoxsa əvvəl rejim açarı və Shadow hesabatı qurulur.
10. **Shadow mərhələsi:** keçirilirmi, nə qədər çəkir. `docs/strukturlar-ve-selahiyyetler.md` §4.3-də ən azı 3 iş günü yazılıb. Bu keçidi ölçmür, yalnız Enforced-ın təhlükəsiz olduğunu göstərir.
11. **Ardıcıllıq:** bir şirkət, bir struktur, əvvəl bir neçə şəxs.
12. **Structure Manager-in özü:** onun `Role`-u nə olacaq? Hazırda `Manager`-dirsə, eyni keçidi o da keçirmi? Bu, B variantında hesaba toxunmağı birbaşa dəyişir.

---

## Əlavə: data ilə yoxlananlar və açıq qalanlar

2026-11-12-də verilənlər bazasında yalnız oxumaqla yoxlanıb:
- Bütün şirkətlər Legacy-dədir (`AuthzMode = 0`).
- Təsdiq açarı production-da açıqdır (PR #50) və sahə bir şirkətin bir neçə filialında açılıb. Həmin filiallarda köhnə menecerlər «Yeni işçi» müraciəti göndərir, Structure Manager qərar verir.
- Həmin şirkətdə Structure Manager-in özü hələ `Role = Manager`-dir. Bu, §8-in 12-ci sualını aktual edir.
- Həmin şirkətin `timekeeper` nüsxəsində `requests.*` yoxdur. `structure_operator` nüsxəsinə yalnız `requests.decide` əlavə olunub. Yəni keçiddən əvvəl Timekeeper nüsxəsinə ən azı `requests.create` və `requests.view` əlavə edilməlidir, əks halda sahədə nə birbaşa, nə müraciətlə işçi əlavə edə bilməz (§5.0).
- Staging test şirkəti Enforced-dadır.

Hələ yoxlanmayıb:
- Digər şirkətlərin rol nüsxələrində hansı hüquqların olduğu.
- Shadow rejiminin performansa təsiri.
