# Strukturlar, səlahiyyətlər və vahid panel — Faza 0: audit və dizayn

Status: **təsdiq gözləyir** (2026-10-15). Bu sənəd kod deyil — nəyi, hansı ardıcıllıqla və hansı
qorumalarla quracağımızı göstərir. Faza 1-ə yalnız sahibin təsdiqindən sonra keçilir.

---

## 0. Qısa xülasə

**Bu gün:** iki sərt rol (Admin, Menecer). Menecerin sahəsi `ManagedLocations` cədvəlidir. Kimin nə edə
biləcəyi 240 endpoint-ə, 55 `User.Role()` çağırışına və beş ayrı «menecer qaydası» köməkçisinə
səpələnib. Rol JWT-nin içindədir və yalnız hesabdan çıxarmaqla (TokenVersion) dəyişir.

**Yeni model:**

    Şirkət ─┬─ Struktur (istəyə bağlı) ── Filiallar
            └─ Struktursuz filiallar

    İşçi ── Təyinat ── Rol (icazələr dəsti)
                 └──── Sahə: Şirkət | Struktur | Filial

- İcazə hər sorğuda **bazadan** oxunur → dəyişiklik dərhal işləyir, **heç kim hesabdan çıxmır**.
- İlk gün **heç kimin girişi dəyişmir**: indiki adminlər «Şirkət administratoru», menecerlər
  «Filial meneceri (köhnə qayda)» rolunu öz filiallarında alır. Yeni rolları (Struktur operatoru,
  Tabelçi, Müşahidəçi) adamlara **sahib özü** təyin edir — addan təxmin edilmir.
- Keçid **kölgə rejimi** ilə: yeni sistem köhnə ilə paralel hesablayır, fərqləri jurnala yazır,
  heç nəyi bloklamır. Sıfır fərqdən sonra şirkət-şirkət açılır. Geri qaytarma = bayrağı söndürmək.

**Bu layihədən asılı olmayan iki təcili tapıntı** (§2): operatorun hesabını ələ keçirmək mümkündür
(SEC-02, kritik, 02.10-dan açıqdır) və menecer redaktəsi fərdi giriş istisnalarını silir.

---

## 1. Audit — bu gün necə işləyir

### 1.1 Rəqəmlər

| | Say |
|---|---|
| Endpoint (40 controller, 53 fayl) | 240 |
| Anonim | 12 (+3 yalnız dev) |
| İstənilən giriş etmiş (44-ü yalnız «özüm») | 60 |
| Yalnız Admin | 60 |
| Admin + Menecer | 54 |
| Yalnız Menecer (`/api/manager`) | 18 |
| Platforma operatoru (`/api/super`) | 33 |

Prod (15.10): Bakı Abadlıq — **82 filial, 914 işçi, 1 admin, 34 menecer**. Filialların 53-ünə iki
menecer, 18-nə bir menecer baxır, 2 filialın meneceri yoxdur. Bir menecer 38 filiala, üçü 11–12
filiala baxır — bunlar faktiki olaraq «struktur operatoru»dur. Digər şirkətlər kiçikdir (1–2 filial).

### 1.2 Giriş necə yoxlanır

- **Rol JWT-dədir.** `[Authorize(Roles=…)]` və `User.Role()` tokenin `role` claim-ini oxuyur. Rol
  dəyişəndə `AdminController.Update` `TokenVersion++` edir — yəni adamı hesabdan çıxarır. SQL ilə
  dəyişdirilən rol köhnə tokenlərdə ~100 il köhnə qalır.
- **Menecer sahəsi** `ManagedLocations`-dur və hər sorğuda bazadan oxunur. Yeganə yazan yer:
  `AdminController.ApplyManagedLocationsAsync`.
- **Sahə qaydaları üç yerdədir:**
  - `LocationScopeRules`: `CanAccess` (görmək), `CanSchedule` (növbə), `CanManage` (əməliyyat);
  - `ManagerController`-in öz köməkçiləri: `ManageableEmployeeAsync`, `CredentialTargetAsync`,
    `IsColleague`, `LeaveSubjectAsync`;
  - `ReportQueryService` və `LocationScope`: hesabatlarda rola görə filtr.

### 1.3 Menecerin şirkət boyu imkanları (bugünkü «istisnalar»)

| İmkan | Harada | Qeyd |
|---|---|---|
| İstənilən adi işçinin PIN-i və nömrəsi | `ManagerController` reset-pin, phone, credential-targets | Sahibin 14.09 qərarı, 18.09-da daraldılıb |
| Açıq günlər — skanın filialına görə | `AdminAttendanceController.Open` | İşçinin öz filialı yox, skanın filialı |
| Şirkət boyu növbə yaratmaq, istifadə olunmayan istənilən növbəni silmək | `/api/manager/schedules` | Admin tərəfində eyni siyahı filialla məhdudlaşdırılıb — **uyğunsuzluq** |
| Tapşırıqlar lövhəsi | `TasksController` | Hamısı şirkət boyu |
| «Ayın işçisi» canlı nəticələri | `VoteController.Results` | Bütün filiallar |
| Növbə əvəzləməsində istənilən növbə | `ShiftOverridesController` | Hədəf məhduddur, növbə yox |
| Filialın GPS dairəsi, QR-siz rejim, GPS divarı | `AdminLocationsController.Update` | Öz filiallarında, yalnız jurnala yazılır |

### 1.4 Uyğunsuzluqlar

- **Siyahı və əməliyyat fərqlidir.** PIN, cihaz və unudulmuş çıxış sorğularının siyahısı menecerə
  yalnız adi işçiləri göstərir. Təsdiq/rədd isə `CanManage` ilə yoxlanır, o da həmkar meneceri
  buraxır. Menecer siyahıda görmədiyi sorğunu id ilə həll edə bilir. Bildiriş də onu görmədiyi
  sorğu haqqında gedə bilər.
- **Məzuniyyət və növbə qaydaları** filialdakı adminləri də əhatə edir. «Əməliyyat» qaydası isə
  adminləri buraxmır.
- **Köhnəlmiş şərhlər:** 6 yerdə şərh kodun etdiyinin əksini deyir. `CLAUDE.md`-də yazılıb ki,
  `IgnoreQueryFilters` yalnız 2 yerdədir — əslində super-admin konsolundan kənarda da 10 yerdə var
  (hamısının əsaslı səbəbi var).

### 1.5 Frontend

- **İki işçi siyahısı:**
  - `EmployeesPage` 2259 sətir, menecer variantı 553 + `CredentialDesk` 207 sətir;
  - menecer variantının ~60%-i admin kodunun təkrarıdır və davranış artıq ayrılıb.
- **API təkrarları:** `api/manager.ts`-in 18 funksiyasından 16-sı admin funksiyasının yalnız sahəsi
  fərqli variantıdır. Tiplər ayrılıb, səhifələr onları `as unknown as` ilə birləşdirir.
- **Giriş qaydaları üç yerdədir:**
  - `panelAccess.ts`;
  - `AdminLayout` (10 lazımsız və 1 ziddiyyətli rol şərti);
  - səhifələrin özləri (`isManager ? … : …`).
- **Menecerə açıq səhifə, yalnız adminə açıq API.** Dörd yerdə menecer bu səbəbdən ümumi xəta görür:
  - Maaş Excel-i;
  - QR-u etibarsız etmək;
  - sahə ziyarətini silmək;
  - filial səhifəsində «Yeni qrafik».
- **403 boş siyahı kimi göstərilir.** Təxminən 12 səhifədə icazə xətası «Qeyd yoxdur» kimi görünür.
  Yalnız 4 səhifə bunu düz edir.
- **Sayğac və zəng ikonu yalnız admindədir**, halbuki «Müraciətlər» səhifələri menecerə də açıqdır.

---

## 2. Təcili — bu layihəni gözləməməli

| # | Nə | Təsir | Düzəliş |
|---|---|---|---|
| T1 | **SEC-02.** `AdminPinResetController.Resolve`-də operator yoxlaması yoxdur. Digər 5 PIN sıfırlama yolunda var. | Operatorların işçi sətirləri `bax` şirkətindədir, orada 6 aktiv admin var. Hər biri anonim «PIN-i unutdum» sorğusu açıb sonra özü həll edərək operatorun müvəqqəti PIN-ini ala bilər. Bununla bütün platforma konsoluna, yəni bütün şirkətlərə girişdir. **Kritik.** | 1 yoxlama + test. Ayrıca kiçik deploy. |
| T2 | Menecer siyahısında «Yadda saxla», «İşdən çıxart» və «Geri qaytar» işçinin fərdi QR-siz və GPS istisnasını silir. Server sahəni yazır, menecer forması onu göndərmir. | İndi yalnız 1 nəfərdə var (Green Garden). Gələcəkdə səssiz itki. | Formada sahəni geri göndərmək + test. T1 ilə eyni deploy. |

---

## 3. Yeni model

### 3.1 Struktur

- **`Structure`** — `Id`, `TenantId`, `Name`, `CreatedAtUtc`, `ArchivedAtUtc`. Şirkətə bağlıdır və
  sorğu filtri ilə qorunur.
- **`Location.StructureId`** — nullable, yəni struktur məcburi deyil.
- **Başqa şirkətin strukturu bazanın özündə qadağandır.** Kompozit xarici açar
  `(TenantId, StructureId) → Structures(TenantId, Id)` istifadə olunur. Yalnız kodla deyil, bazanın
  özü də başqa şirkətin strukturuna filial bağlamağa imkan vermir.
- **Silmək yerinə arxivləmək.** Yalnız heç vaxt filialı olmamış struktur həqiqətən silinir.
- **Filialı köçürmək** yalnız `StructureId`-ni dəyişir. Davamiyyət sətirləri işçiyə və filiala
  bağlıdır, struktura yox. Tarixçə yerində qalır. Hər köçürmə audit-ə yazılır.
- **Diqqət:** struktur üzrə hesabat filialın **indiki** strukturunu götürür. Ayın ortasında
  köçürülən filialın bütün ayı yeni strukturda görünür. Tarixçə cədvəli bu mərhələdə lazım deyil,
  sonra əlavə etmək olar.
- GPS, QR, növbə və geofence məntiqinə toxunulmur.

### 3.2 Rol, icazə, təyinat

- **`AccessRole`** — `Id`, `TenantId`, `Name`, `Description`, `TemplateKey`, `IsLocked`,
  `Permissions` (mətn massivi), `CreatedAtUtc`, `ArchivedAtUtc`. Şirkətə bağlıdır, hər şirkət öz
  rollarını qurur.
- **`RoleAssignment`** — `Id`, `TenantId`, `EmployeeId`, `AccessRoleId`, `Scope`
  (Company / Structure / Branch), `StructureId?`, `LocationId?`, `CreatedAtUtc`, `CreatedBy`,
  `RevokedAtUtc`, `RevokedBy`.
  - Baza yoxlaması: sahə növü ilə doldurulmuş sütun uyğun gəlməlidir.
  - Kompozit açarlarla eyni şirkət şərti qoyulur.
  - Təyinatı geri almaq silmək deyil. Sətir qalır, kimin nə vaxt verib geri aldığı görünür.
- **Sahə canlı hesablanır:**
  - Şirkət → bütün filiallar, gələcəkdə açılacaqlar da;
  - Struktur → indi o strukturda olan filiallar;
  - Filial → yalnız o filial.
- Bir adamın bir neçə təyinatı ola bilər. Məsələn, A strukturunda operator, B filialında müşahidəçi.
- **Yalnız şirkət sahəsində verilə bilən icazələr:** rollar, strukturlar, bayramlar, vəzifələr,
  elanlar, kompüter reyestri, abunə, «Ayın işçisi», yenidənhesablama, diaqnostika.
- **`Employee.Role` keçid dövründə qalır** — «hesab növü» kimi: adi işçi və ya panel istifadəçisi.
  İcazəni artıq o vermir.

### 3.3 İcazə kataloqu

Açarlar daxili texniki addır, ekranda Azərbaycan dilində görünür. «Bu gün» sütunları miqrasiyada
nəyin köçürüləcəyini göstərir.

| Açar | Nə verir | Növ | Bu gün Admin | Bu gün Menecer |
|---|---|---|---|---|
| `dashboard.view` | İdarəetmə paneli, sayğaclar | Oxu | ✓ | ✓ |
| `attendance.view` | Bugünkü lövhə, tabel, xülasə, problemlər, növbə uyğunsuzluğu, radius, ilişən telefonlar, doğum günləri | Oxu | ✓ | ✓ filial |
| `attendance.photos.view` | Giriş şəkli (yalnız şübhəli sətirdə) | Oxu | ✓ | ✓ filial |
| `attendance.export` | Excel: xülasə, tabel, günlük lövhə | Oxu | ✓ | ✓ filial |
| `attendance.correct` | Əl ilə qeyd yaratmaq, dəyişmək, silmək; çıxışı silmək; açıq günləri bağlamaq; «qayıb»; unudulmuş çıxış sorğuları | Yazı | ✓ | ✓ filial |
| `attendance.fraud` | Günü saxtakarlıq kimi ləğv etmək və bərpa etmək, foto xəbərdarlığı | Yazı | ✓ | ✓ filial (backend icazə verir, ekran gizlədir) |
| `attendance.maintenance` | Xülasələri və üzləri yenidən hesablamaq | Yazı · şirkət | ✓ | — |
| `employees.view` | İşçi siyahısı və kartı | Oxu | ✓ | ✓ filial |
| `employees.contacts.view` | Telefon və e-poçt | Oxu | ✓ | ✓ (adminlərinki gizli) |
| `employees.create` | İşçi əlavə etmək | Yazı | ✓ | ✓ öz filialına |
| `employees.import` | Toplu dəvət və Excel idxalı | Yazı | ✓ | — |
| `employees.update` | Ad, vəzifə, filial, növbə, sənəd məlumatı | Yazı | ✓ | ✓ filial |
| `employees.deactivate` | İşdən çıxartmaq və qaytarmaq | Yazı | ✓ | ✓ filial |
| `employees.delete` | Tarixçə ilə silmək | Yazı | ✓ | — |
| `employees.reset_pin` | PIN sıfırlamaq + PIN sorğuları növbəsi | Yazı | ✓ | ✓ **şirkət boyu adi işçilər** |
| `employees.change_phone` | Giriş nömrəsini dəyişmək | Yazı | ✓ | ✓ **şirkət boyu adi işçilər** |
| `employees.reinvite` | Dəvət linkini yeniləmək | Yazı | ✓ | — |
| `employees.salary` | Maaşı təyin etmək | Yazı | ✓ | — |
| `employees.reference_photo` | İstinad şəklini sıfırlamaq | Yazı | ✓ | — |
| `employees.reset_attendance` | Davamiyyət tarixçəsini silmək | Yazı · şirkət | ✓ | — |
| `devices.view` | Bağlı telefonlar, ortaq telefonlar | Oxu | ✓ | ✓ filial |
| `devices.approve` | Yeni telefon tələbini təsdiq və ya rədd etmək | Yazı | ✓ | ✓ filial |
| `devices.revoke` | Telefonu ləğv etmək | Yazı | ✓ | ✓ filial |
| `devices.share_grant` | Ortaq telefon icazəsi | Yazı | ✓ | ✓ filial |
| `field_checkin.grant` | Fərdi səyyar giriş hüququ (`CanFieldCheckIn`) | Yazı | ✓ | ✓ filial |
| `checkin_rules.override` | Fərdi QR-siz və GPS divarı istisnası | Yazı | ✓ | ✓ filial |
| `field_visits.view` | Sahə ziyarətləri lövhəsi | Oxu | ✓ | ✓ filial |
| `field_visits.manage` | Ziyarət təyin etmək, qiymətləndirmək, ləğv etmək, məcburi çıxış | Yazı | ✓ | ✓ filial |
| `field_visits.discard` | Tamamlanmış ziyarəti silmək | Yazı | ✓ | — |
| `leaves.view` / `leaves.manage` | Məzuniyyət və icazələr | Oxu / Yazı | ✓ | ✓ filial + özü |
| `schedules.view` | Növbələr | Oxu | ✓ | ✓ |
| `schedules.manage` | Növbə yaratmaq, dəyişmək, silmək | Yazı | ✓ | ✓ (bu gün şirkət boyu — §9 Q2) |
| `schedules.assign` | Növbə təyini, əvəzləmə günləri | Yazı | ✓ | ✓ filial |
| `payroll.view` / `payroll.export` | Maaş cədvəli / Excel | Oxu | ✓ / ✓ | ✓ filial / — |
| `locations.view` | Filiallar | Oxu | ✓ | ✓ öz |
| `locations.create` / `locations.activate` | Filial yaratmaq / aktivləşdirmək və silmək | Yazı | ✓ | — |
| `locations.update` | Ad, iş saatları, iş günləri | Yazı | ✓ | ✓ öz |
| `locations.geofence_manage` | GPS dairəsi, QR-siz rejim, GPS divarı | Yazı | ✓ | ✓ öz (§9 Q2) |
| `locations.qr_print` / `locations.qr_invalidate` | QR posteri / QR-u etibarsız etmək | Yazı | ✓ / ✓ | ✓ / — |
| `structures.view` / `structures.manage` | Strukturlar, filial köçürmək | Oxu / Yazı · şirkət | yeni: ✓ | — |
| `assignments.view` | Kimin hansı hüququ var | Oxu | yeni: ✓ | — |
| `assignments.manage` | Rol təyin etmək — öz sahəsində, öz hüququndan artıq olmayan | Yazı | yeni: ✓ | — |
| `roles.manage` | Rol yaratmaq və icazə dəstini dəyişmək | Yazı · şirkət | yeni: ✓ | — |
| `notifications.staff_alerts` | «Skan edə bilmir», «PIN-ini unudub», «yeni telefon» push-ları | — | ✓ (ehtiyat) | ✓ filial |
| `tasks.use` | Tapşırıqlar lövhəsi | Yazı | ✓ | ✓ |
| `holidays.manage`, `positions.manage`, `announcements.manage`, `votes.manage`, `equipment.view` / `equipment.manage`, `billing.view`, `diag.view` | Şirkət ayarları | · şirkət | ✓ | — |

Platforma operatoru konsolu (`/api/super`, `OperatorProfile`) bu sistemə **qarışmır**, ayrıca qalır.

### 3.4 Şablon rollar

Bu rollar başlanğıc nümunədir. Hər şirkət onları dəyişə və öz rollarını yarada bilər.
Kilidli olan yalnız «Şirkət administratoru»dur: bərpa yolu itməsin deyə onun icazələri dəyişmir.

| Rol | Adətən sahə | İcazələr | İstəyə bağlı (sahib seçir) |
|---|---|---|---|
| **Şirkət administratoru** 🔒 | Şirkət | Hamısı | — |
| **Struktur operatoru** | Struktur | panel; davamiyyət (oxu, export); işçi (görmək, əlaqə, yaratmaq, dəyişmək, işdən çıxartmaq, PIN, nömrə); telefon (görmək, təsdiq, ləğv); növbə (görmək, idarə, təyin); məzuniyyət; filial (görmək, QR çapı); sahə ziyarətləri; təyinatları görmək; bildirişlər | `assignments.manage` (tabelçiyə filial təyin etmək — tapşırıqda «icazəlidirsə»), `attendance.correct`, `field_checkin.grant`, `checkin_rules.override`, `locations.update` |
| **Tabelçi** | Filial(lar) | panel; davamiyyət (oxu, export); işçi (görmək, əlaqə, yaratmaq, dəyişmək, PIN); telefon (görmək, təsdiq); növbə (görmək, təyin); məzuniyyət; sahə ziyarətləri (oxu); bildirişlər | `attendance.correct`, `field_checkin.grant` |
| **Müşahidəçi** | Struktur və ya filial | panel; davamiyyət (oxu, export); işçi (görmək); məzuniyyət (oxu); növbə (oxu); filial (oxu); sahə ziyarətləri (oxu). **Heç bir yazı yoxdur.** | — |
| **Filial meneceri (köhnə qayda)** | Filial(lar) | Bugünkü menecerin dəqiq icazələri, §3.3-ün «Menecer» sütunu | Keçid rolu: sahib adamları yeni rollara keçirdikcə boşalır |
| **PIN köməkçisi** | Şirkət | `employees.reset_pin`, `employees.change_phone`, axtarış üçün `employees.view` (yalnız ad, vəzifə, filial, nömrənin sonu) | Bugünkü menecerin 14.09 imkanı, olduğu kimi |

### 3.5 Səlahiyyət artırmanın qarşısı

1. **Verdiyin hüquq səndə olmalıdır.** `assignments.manage` olan şəxs R rolunu S sahəsində yalnız o
   halda verə bilər ki, R-in hər icazəsi onun özündə S-i əhatə edən sahədə olsun. Şirkət
   icazələrini ancaq şirkət sahəsində olan verə bilər.
2. **Özünə hüquq vermək olmaz.** Öz təyinatını dəyişmək qadağandır.
3. **Səndən güclünü idarə etmək olmaz.** Başqasının PIN-ini, nömrəsini, məlumatını yalnız o halda
   dəyişmək olar ki, onun bütün hüquqları səndə də olsun, eyni və ya daha geniş sahədə. Bu gün
   «menecer admini idarə edə bilməz» və «yalnız öz filialının həmkar meneceri» qaydaları bu bir
   qaydanın xüsusi halıdır.
4. **Platforma operatoru heç vaxt hədəf olmur.** Bu, bütün yollarda yoxlanır (bu gün bir yolda
   yoxdur — T1).
5. **Hər dəyişiklik audit-ə yazılır:** kim, kimə, hansı rolu, hansı sahədə verdi və ya geri aldı.

### 3.6 Logout olmadan dərhal qüvvəyə minmə

- **İcazələr hər panel sorğusunda bazadan oxunur.** Bunu sorğu daxilində bir dəfə yüklənən
  `AccessContext` edir: 2 kiçik sorğu, indeksli.
- **Skan yolu buna toxunmur.** İşçinin öz endpoint-ləri «özüm» qaydası ilə qalır.
- **`TokenVersion` icazə üçün artırılmır.** Sahib qaydası belədir: məcburi logout yoxdur.
- **JWT-dəki `role` server üçün icazə mənbəyi olmaqdan çıxır.**
  - `[Authorize(Roles=…)]` əvəzinə `[Authorize]` + icazə siyasəti (`perm:employees.update`) gəlir.
  - Siyasət keçid rejiminə baxır (§4.3).
- **Frontend rolu tokendən yox, `GET /api/me/access`-dən götürür.** Panel açılanda bu sorğu
  icazələri və sahələri qaytarır. Yeni tabelçi təyin edilən adam yenidən girmədən panelini görür.

---

## 4. Köçürmə və geri qaytarma

### 4.1 Miqrasiyalar

Hamısı **yalnız əlavə edir**: köhnə cədvəl və sütunlara toxunulmur.

| Faza | Miqrasiya | Data | Geri qaytarma |
|---|---|---|---|
| 1 | `Structures` + `Locations.StructureId` (nullable, kompozit FK) | Heç biri: bütün filiallar struktursuz başlayır, avtomatik bölgü yoxdur | Köhnə kod sütunu görmür |
| 2 | `AccessRoles`, `RoleAssignments` + toxum | Hər şirkətə şablon rollar. Admin → Şirkət administratoru (şirkət). Hər `ManagedLocation` → Filial meneceri (köhnə qayda) (filial). Hər menecer → PIN köməkçisi (şirkət). | `ManagedLocations` toxunulmaz qalır |

### 4.2 Keçid dövründə ikili yazı

Köhnə admin formasında «menecerin filialları» dəyişəndə həm `ManagedLocations`, həm də uyğun
təyinat yazılır. Yeni təyinat ekranı köhnə rol üçün eyni şeyi edir. İki mənbə ayrılmır.

### 4.3 Kölgə rejimi və şirkət-şirkət keçid

Hər şirkətin bir bayrağı olur: `Legacy` → `Shadow` → `Enforced`.

- **Legacy:** bu günkü kimi. Yeni mühərrik yalnız `/api/me/access`-i doldurur.
- **Shadow:**
  - köhnə qayda qərar verir, yeni qayda paralel hesablayır;
  - hər fərq jurnala yazılır: endpoint, işçi, köhnə qərar, yeni qərar;
  - heç kim heç nə hiss etmir.
- **Enforced:** yeni qayda qərar verir. Geri qaytarma = bayrağı `Shadow`-a qaytarmaq, deploy yoxdur.

Sahə yoxlamalarının bərabərliyini testlər də sübut edir. Generasiya olunmuş fixture-larda köhnə
`LocationScopeRules` cavabı yeni mühərrikin cavabı ilə müqayisə olunur: hər rol, hər filial
kombinasiyası, həmkar, admin, özü.

Sıra: əvvəl staging → prod-da Bakı Abadlıq `Shadow` (ən azı 3 iş günü, sıfır izah olunmamış fərq) →
`Enforced` → digər şirkətlər. Deploylar pik saatlardan kənar (07:30–09:30, 17:00–19:00).

### 4.4 Təxmin yoxdur

Kimin Struktur operatoru, Tabelçi və ya Müşahidəçi olacağını sahib ekranda seçir. Miqrasiya yalnız
bugünkü vəziyyəti köçürür. 38 filiala baxan menecer «struktur operatoru»na bənzəyir, amma struktur
da, rol da ona yalnız sahib təyin edəndə verilir.

---

## 5. Vahid panel

- **`GET /api/me/access`** qaytarır:
  - `{ permissions: { key → { all | structureIds[] | locationIds[] } }, structures, branches }`.
- **`panelAccess.ts`**: hər səhifə rol yox, icazə tələb edir (biri kifayətdir). Nav menyusu eyni
  cədvəldən qurulur. `AdminLayout`-dakı rol şərtləri silinir.
- **Düymələr:** server hər sətrə `can` bayraqları qaytarır, məsələn işçi sətrində
  `{ edit, resetPin, deactivate, … }`. Frontend düyməni gizlədir, amma qərarı yenə server verir.
- **Bir işçi səhifəsi:**
  - `EmployeesPage` vahid `/api/employees` üzərində qurulur, sahəyə görə süzülür;
  - `/admin/my-employees` → `/admin/employees` yönləndirməsi qalır;
  - `CredentialDesk` şirkət boyu PIN hüququ olanlar üçün «Şirkət üzrə axtarış» panelinə çevrilir;
  - 16 menecer API əkizi keçid dövründə ləqəb kimi qalır, sonra silinir.
- **403 ≠ boş:** ortaq `useScopedList` + «Bu bölməyə icazəniz yoxdur» komponenti. Təxminən 12
  səhifədə səhv «Qeyd yoxdur» düzəlir.
- **Sayğac və zəng hər panel istifadəçisi üçün**, öz sahəsindəki saylarla.
- **Struktur və filial filtrləri:** işçi, davamiyyət və tabel səhifələrində yalnız icazəli
  struktur və filiallar göstərilir. Filtr yalnız daraldır, server sahəni heç vaxt genişlətmir.

## 6. Strukturlar ekranı

- **Görünüş:** yalnız `structures.manage` olanlara görünür. Strukturu olmayan şirkətdə menyuda
  yalnız admin görür, başqa heç nə dəyişmir.
- **Funksiyalar:**
  - struktur yaratmaq, adını dəyişmək, arxivləmək;
  - «Struktursuz filiallar» siyahısı;
  - filialı struktura əlavə etmək və ya başqasına köçürmək (təsdiqlə);
  - hər strukturun və filialın kimə həvalə olunduğu — operatorlar, tabelçilər, müşahidəçilər —
    və oradan təyin etmək. Təyin etmək §3.5 qaydaları ilə məhdudlaşır.
- **Filiallar siyahısında** struktur üzrə filtr.

## 7. Səyyar və giriş qaydaları

Üç fərqli anlayış qarışdırılmır:

| Anlayış | Sahə | İcazə |
|---|---|---|
| Fərdi səyyar hüquq (`Employee.CanFieldCheckIn`) | İşçi | `field_checkin.grant` |
| Fərdi QR-siz və GPS divarı istisnası (`*Override`) | İşçi | `checkin_rules.override` |
| Filialın QR-siz rejimi, GPS divarı, dairəsi | Filial | `locations.geofence_manage` |
| Sahə ziyarəti (`FieldVisit`) | Əməliyyat | `field_visits.view` / `manage` / `discard` |

- **Tarixçə:** hər dəyişiklik yeni audit hadisəsi ilə yazılır (`CheckInRuleChanged`): kim, kimə,
  əvvəl → sonra. Bu gün yalnız geofence köçürməsi yazılır.
- **Yoxlamalar zəifləmir:** skan yolunda GPS və QR yoxlamasının özü dəyişmir.

## 8. Bildirişlər (StaffAlerts)

- **Kimə gedir:** `notifications.staff_alerts` və uyğun əməliyyat icazəsi filialı əhatə edən şəxsə.
  Əməliyyat icazəsi növə görədir:
  - «skan edə bilmir» → `attendance.view`;
  - «PIN-ini unudub» → `employees.reset_pin`;
  - «yeni telefon» → `devices.approve`.
- **Ardıcıllıq:** əvvəl ən dar sahə (filial), sonra struktur, sonra şirkət. Şirkət boyu PIN
  köməkçiləri hər PIN sorğusunu almasın deyə dar sahə üstündür.
- **Bakı Abadlıqda keçiddən sonra** bildiriş tabelçi və operatora gedir, yalnız izləyən menecerə yox.
- **Keçidə qədər** bugünkü kimi menecerə gedir.

## 9. HR modulu ilə uyğunluq

- **Kim təsdiq edir.** HR sənədi (`hr-modulu` branch) «işçi → birbaşa rəhbər → HR» təsdiq zənciri
  istəyir. Bu gün «birbaşa rəhbər» sahəsi yoxdur. Yeni modeldə filiala `leaves.approve` olan şəxs
  həmin filialın təsdiqçisidir — ayrıca sütun lazım deyil.
- **HR qaydaları qorunur:**
  - `LeaveRecord` müqaviləsi;
  - `hr` bayrağı;
  - fail-closed sorğu filtri;
  - `IgnoreQueryFilters` olmaması.

---

## 10. Fazalar

| Faza | Nə | Əsas risk | Bitmə şərti |
|---|---|---|---|
| **T** | T1 + T2 (ayrıca branch, kiçik deploy) | Yoxdur | Testlər + prod yoxlaması |
| **1** | Struktur modeli, miqrasiya, CRUD API, filial köçürmə, audit, testlər | Aşağı (yalnız əlavə) | Struktursuz şirkət dəyişməz işləyir |
| **2** | `AccessRole` / `RoleAssignment`, toxum miqrasiyası, `AccessContext`, icazə siyasəti, `/api/me/access`, kölgə rejimi, bərabərlik testləri | Orta | Bütün şirkətlərdə Legacy = Shadow, fərq 0 |
| **3** | Rol və təyinat API + ekran, §3.5 qaydaları, şablonlar, audit | Orta | Eskalasiya testləri yaşıl |
| **4** | Vahid panel: icazəyə görə marşrut və nav, vahid işçi səhifəsi, 403 ≠ boş, filtrlər, Strukturlar ekranı, bildiriş ünvanı | Orta | Köhnə URL-lər işləyir, 15 ssenari yaşıl |
| **5** | Controller-lərin icazə siyasətinə köçürülməsi, rol-sətir testlərinin icazə testinə çevrilməsi, əkiz endpoint-lərin ləqəbə çevrilməsi | Yüksək (ən geniş dəyişiklik) | Shadow-da 3 iş günü sıfır fərq |
| **6** | Təhlükəsizlik auditi (IDOR, eskalasiya, şirkətlərarası), yekun hesabat, PR | — | Sahibin təsdiqi |

Qaydalar:
- `main`-ə birbaşa yazılmır.
- Prod deploy yoxdur. Staging yalnız ayrıca icazə ilə.
- Ödəniş və hesabat hesablamalarına toxunulmur.
- Skan axınına toxunulmur.

## 11. Testlər

- **Tapşırığın 15 ssenarisi** — hamısı avtomatik test kimi.
- **Bərabərlik testləri:** köhnə `LocationScopeRules`, `ManagerController` köməkçiləri və
  `ReportQueryService` sahəsi ilə yeni mühərrik.
- **Rol sətirlərini refleksiya ilə yoxlayan mövcud testlər:**
  - `ManagerScreenScopeTests`;
  - `OpenRecordsManagerScopeTests`.

  Bu testlər bugünkü təhlükəsizlik qərarlarını kodlayır. Silinmir, icazə sətirlərini yoxlayan
  testlərə çevrilir.
- Hər fazada `dotnet test` və `npm run build` + vitest yaşıl olmalıdır.

## 12. Risklər

| Risk | Qarşısı |
|---|---|
| Səhv köçürmə menecerləri kilidləyir (səhər cihaz/PIN təsdiqi) | Kölgə rejimi, şirkət-şirkət keçid, bayraqla geri qaytarma, pikdən kənar deploy |
| Səhv köçürmə kiməsə artıq hüquq verir | Bərabərlik testləri, §3.5 qaydaları, audit |
| Performans | Sorğu başına 2 indeksli sorğu, yalnız panel sorğularında, skan yolunda yox |
| Köhnə tokenlər | Server tokendəki rola etibar etmir. Logout yoxdur. |
| Böyük frontend dəyişikliyi | Köhnə URL yönləndirmələri, keçid dövründə API ləqəbləri |
| HR modulu ilə toqquşma | `leaves.approve` sahəsi HR-ın «kim təsdiq edir» sualını həll edir. HR sənədinin qaydaları qorunur. |

## 13. Sahibin qərarları (2026-10-15)

1. **Keçid** — kölgə rejimi + şirkət-şirkət keçid. ✅ Qəbul edildi.
2. **Bugünkü menecer «istisnaları»** (§1.3) köhnə qayda rolunda **saxlanır**:
   - tapşırıqlar lövhəsi;
   - filialın GPS və QR-siz ayarları;
   - filialdakı adminə məzuniyyət yazmaq;
   - şirkət boyu PIN və nömrə.

   **Daraldılır:** şirkət boyu növbə yaratmaq və istifadə olunmayan istənilən növbəni silmək. Menecer
   yalnız öz filiallarına bağlı növbələri yarada və silə bilər. Yeni şablonlarda bu istisnaların
   heç biri yoxdur. ✅
3. **Bildirişlər** keçiddən sonra filialda əməliyyat icazəsi olan tabelçi və operatora gedir,
   yalnız izləyən menecerə getmir. Keçidə qədər bugünkü kimi menecerə gedir. ✅
4. **T1 + T2** — ayrıca branch-də indi düzəldilir və deploy olunur. ✅

---

## 14. Faza 2 — qurulub (2026-10-15, branch `feat/permissions-phase2`)

Hələ heç nəyi **qərar vermir**. Bütün şirkətlər `Legacy` rejimindədir. Bugünkü rollar və sahə qaydaları
olduğu kimi işləyir, yeni model yalnız hesablayır, müqayisə edir və `/api/me/access`-i doldurur.

### 14.1 Nə var

| Hissə | Harada |
|---|---|
| İcazə kataloqu (58 açar, Azərbaycanca adlar, 14-ü yalnız şirkət sahəsində) | `Domain/Access/AccessPermissions.cs` |
| 6 rol şablonu | `Domain/Access/AccessRoleTemplates.cs` |
| `AccessRoles`, `RoleAssignments`, `AuthzShadowEvents` cədvəlləri, `Tenants.AuthzMode` (0 = Legacy) | miqrasiya `AddAccessRoles` — yalnız əlavə edir, data yazmır |
| Bugünkü Admin və Menecer girişinin sistem təyinatlarına köçürülməsi | `AccessSeeder`. Hər start-da və rolu və ya menecer filiallarını dəyişən 4 yazıdan sonra işləyir. İnsanın verdiyi təyinata toxunmur. |
| Kim nə edə bilər, harada | `AccessGraph` / `EffectiveAccess` — sorğuda bazadan, tokendən yox |
| Üstünlük, özünə vermə qadağası, şirkət icazəsi, operator qorunması | `AccessRules` |
| Bugünkü hər sahə qaydasının yeni modeldəki cavabı (bir cədvəl) | `ShadowRules.Decide` |
| Kölgə: sahə qaydaları | `AuthzObserver` — `LocationScopeRules` və menecer masasının üç köməkçisi ona deyir |
| Kölgə: qapı (endpoint) | `AuthzShadowMiddleware` + 109 `[Permission]` qeydi |
| «Mənim icazələrim» | `GET /api/me/access` |

### 14.2 İşləyərkən dəqiqləşən qaydalar

- **Həmkar qaydası.** Təmiz «üstünlük» qaydası Bakı Abadlıqda 9 menecer cütündən həmkar hüququnu alardı.
  Bu, prod datasında yalnız oxuma ilə ölçülüb. Ona görə köhnə menecer rolu `PeersManageable` daşıyır:
  - hədəfin evi olan filialda eyni rolu daşıyan həmkar onu idarə edə bilir — bu, bugünkü qaydadır;
  - başqa yerdə yaşayıb eyni filialları idarə edən idarə edə bilmir — bu da bugünkü kimidir.

  Yeni rollar ciddi qaydanı işlədir.
- **Özünə yox.** PIN, giriş nömrəsi və profil kimi hesab əməliyyatları heç vaxt adamın özünə tətbiq olunmur.
  Bu gün də belədir, bərabərlik testi bunu tapdı: PIN köməkçisi şirkət sahəsində olduğu üçün menecer öz
  PIN-ini sıfırlaya bilərdi. Öz qeydlərinə düzəliş isə yalnız şirkət sahəsində mümkündür: admin edə
  bilər, menecer edə bilməz, bugünkü kimi.
- **`AtCompany`.** Şirkət boyu siyahılar, cəmlər və ayarlar icazənin şirkət sahəsində olmasını tələb edir.
  Admin-ə xas endpoint-lərin bugünkü «yalnız admin» qapısı belə dəqiq köçürülür.
- **`Twin`.** Menecer üçün `/api/manager/*` və admin üçün onun əkiz yolları olan endpoint-lərdə qapı
  fərqi var. Amma həmin güc o biri yolla eynilə mövcuddur. Belə hallar «Gate» yox, «Twin» kimi yazılır.
- **Şirkət silmə sırası.** `TenantPurge` cədvəlləri əlifba ilə deyil, xarici açarlara görə silir. Əvvəlki
  sıra sadəcə şanslı idi, təyinatlar onu pozdu.

### 14.3 Köhnə və yeni qərarlar arasında fərq

- **Testlərdə: 0.**
  - Bugünkü 3 sahə qaydası × 13 nəfər × 13 hədəf.
  - Menecer masasının 3 qaydası × 5 menecer × 13 hədəf.
  - Hər menecerin filial dəsti.
  - 3 hesab növü (admin, menecer, işçi) × hər `[Permission]` endpoint-i.
- **Prod datasında, yalnız oxuma ilə:** köçürməni pozacaq heç nə yoxdur.
  - 159 `ManagedLocations` sətri: şirkət uyğunsuzluğu 0, menecer olmayanın sətri 0.
  - Filialsız menecer yoxdur.
  - Gözlənilən təyinatlar: 11 şirkət administratoru, 159 köhnə menecer, 39 PIN köməkçisi.
- **Qəsdən fərq, sahibin qərarı:** şirkət boyu növbə yaratmaq və silmək yeni modeldə şirkət sahəsi tələb
  edir. Kod hələ köhnə qaydanı işlədir, bu dəyişiklik Faza 5-də qüvvəyə minəcək.

### 14.4 Hələ yoxdur

- Rol və təyinat ekranı, API — Faza 3.
- Vahid panel; siyahıların və StaffAlerts alıcılarının yeni modelə keçməsi — Faza 4.
- Qərarın yeni modelə verilməsi (`Enforced`) — Faza 5.
- Hesabat endpoint-lərinin **qapısı** (`ReportsController` yalnız `[Authorize]` daşıyır, `[Permission]` yox).
  Onların **sahəsi** isə müqayisə olunur: tabel, hesabat, lövhə və maaş menecerin filiallarını
  `LocationScopeRules.ManagedLocationIdsAsync`-dən alır və hər sorğuda `ManagedSet` kimi yoxlanılır.
  Adminin şirkət sahəsi müqayisə olunmur. Faza 5-də bu endpoint-lərə öz icazə yoxlaması lazımdır.

### 14.5 Növbəti addımlar (hər biri ayrıca təsdiqlə)

1. ~~Staging-də miqrasiya və uzlaşdırma real PostgreSQL-də. Staging şirkətlərindən birini `Shadow`-a keçirib
   `AuthzShadowEvents`-ə baxmaq.~~ Edildi — §15.
2. Prod deploy. Bütün şirkətlər `Legacy` qalır, yalnız təyinatlar yaranır.
3. Bakı Abadlıq `Shadow` rejiminə, ən azı 3 iş günü.
4. `AuthzShadowEvents` hesabatı. Hər «Gate» və sahə fərqinin izahı olmalıdır.

## 15. Faza 2 — staging yoxlaması (2026-10-15)

Staging əvvəl `a452e8d`, düzəlişdən sonra `4af121e` idi. Production-a və Bakı Abadlığa toxunulmayıb. Real
data üzərində yoxlamaları konteynerin içində işləyən probe etdi: deploy olunmuş DLL-lərlə, konteynerin öz
bağlantısı ilə. Bütün müvəqqəti yazılar geri qaytarılan tranzaksiyada idi, bazanın md5 izi əvvəl və sonra
eynidir.

### 15.1 Yoxlamalar

| # | Yoxlama | Nəticə |
|---|---|---|
| 1 | Staging-in prod bazasına çıxışı | Konfiqurasiyada yoxdur: staging-in bağlantısı öz bazasına baxır, prod parolu staging mühitində yoxdur. Şəbəkə barədə — §15.4. |
| 2 | Backup | `qrlog_stg-2026-10-15-pre-access.dump`: 8,5 MB, 37 cədvəl. Saylar ayrıca fayldadır. |
| 3 | Miqrasiya və köçürmə real PG-də | `AddAccessRoles` tətbiq olundu. CK, 4 kompozit FK və `NULLS NOT DISTINCT` unique yerindədir. Gözlənilən və faktiki sistem sətirləri arasında fərq 0, şirkətlərarası bağlantı 0. Mövcud cədvəllərin sayları dəyişməyib. |
| 4 | Təkrar işləmə və əl ilə təyinatlar | İkinci restart 0 dəyişiklik verdi, md5 eyni qaldı. Əl ilə verilən 3 təyinat toxunulmaz qaldı. İki qüsur tapıldı və düzəldildi — §15.2. |
| 5 | Shadow, real API | `test` Shadow-dadır. Admin keçidlərində fərq 0. Nəzarət testində adminin təyinatı müvəqqəti ləğv edildi: 11 endpoint «bu gün ✓ / yeni ✗» kimi yazıldı və sorğuların hamısı 200 qaytardı — kölgə heç nəyi bloklamır. Sətir eyni Id ilə bərpa olundu, test hadisələri silindi. Menecer sessiyası ilə yoxlama gözləyir. |
| 6 | Siyahıların əhatəsi | Hər menecer üçün 10 siyahı icazəsinin hamısında yeni dəst bugünkü `ManagedLocations` ilə eynidir: 3/2/2/9 filial, 39/41/152/170 işçi. Admin şirkət boyu görür. 909 adi işçinin heç birində siyahı icazəsi yoxdur. |
| 7 | Yeni rollar | Müvəqqəti 2 struktur və 9 təyinat yaradıldı, sonra geri qaytarıldı. Yoxlananlar: qapı (145 endpoint), «kimə» (19 hal), təyinat vermə (21 cəhd). Hamısı gözlənildiyi kimidir. |
| 8 | Həmkar qaydası | Real 4 menecer cütündə bugünkü və yeni cavab eynidir. İstisna olmasaydı, 2 icazə itərdi. İstisna yalnız köhnə menecer rolundadır, yeni rollarda yoxdur (TK1 ↔ TK3). |
| 9 | Əlavə yük | Qraf 1,1 ms (21 təyinat), 600 əlavə təyinatla 3,2 ms. Rejim sorğusu 0,25 ms və 30 s keşlənir. Bir qərar 10 µs. Brauzerdə median Legacy-də 60 ms, Shadow-da 65 ms; nəzarət endpoint-ində 63/66 ms — fərq şəbəkə səs-küyü qədərdir. |
| 10 | QR skan | Faza 1–2 skan, kiosk və giriş fayllarında heç bir sətri dəyişməyib. Skan endpoint-ləri `[Permission]` daşımır, ona görə middleware bazaya getmədən qayıdır. Telefonla real skan gözləyir. |

Real datada bərabərlik: 6 qayda üzrə 57 582 qərar, fərq 0.

### 15.2 Tapılan və düzəldilən qüsurlar (`4dd8ac6`)

In-memory testlər unique index və FK yoxlamır. Ona görə hər üç qüsur yalnız real PostgreSQL-də göründü.

1. **Əl ilə verilən sətirlə toqquşma.** Rol əl ilə verilib, sonra eyni açarı sistem də istəyəndə reconcile
   ikinci aktiv sətir əlavə edirdi. Məsələn, menecerə əl ilə admin verilir, sonra formada `Role=Admin`
   olur. Nəticə 23505 idi və həmin şirkətin bütün reconcile-ı hər start-da sınırdı. İndi insanın sətri
   sistemin sətrini əvəz edir. İnsan onu götürərsə, növbəti reconcile sistemin öz sətrini qoyur.
2. **Bir şirkətin xətası hamısını saxlayırdı.** Startup-da bütün şirkətlər bir context-i bölüşür. Bir
   şirkətin xətası dövrdən çıxırdı və sonrakı şirkətlər reconcile olunmurdu. Staging-də sınaq halı
   başqa şirkətin filialına baxan pozuq `ManagedLocations` sətri idi (23503). İndi hər şirkət ayrıca
   işlənir, xəta olanda onun yazılmamış sətirləri atılır.
3. **Yazıdan sonra qalan sətirlər.** `ReconcileAfterWriteAsync` uğursuz olanda yazılmamış sətirlər context-də
   qalırdı və sorğunun növbəti save-i onları təkrar edib sınardı. İndi atılır.

Testlər:
- 3 reconcile testi — düzəliş olmadan sınır.
- 3 dual-write testi (`AccessDualWriteTests`): `AdminController.Update`-dən sonra yeni model dərhal dəyişir.
  Reconcile çağırışı silinsə, sınırlar.

Backend 1268/1268.

### 15.3 Gözlənilən fərqlər və Faza 3-dən əvvəl sahibin qərarı

- **Köhnə menecer → struktur operatoru.** Bu gün menecer öz filialında işləyən operatoru idarə edə bilir,
  yeni modeldə isə yox: operatorun strukturu boyu hüququ menecerdə yoxdur. Operator təyin olunan kimi
  Shadow-da «bu gün ✓ / yeni ✗» görünəcək. **Sual:** sərt qayda davamiyyət düzəlişinə də aiddir, yoxsa
  yalnız hesab əməliyyatlarına (PIN, profil)?
- **Təyinat alanın evi.** Təyinat hüququ olan operator öz strukturuna müşahidəçini strukturdan kənarda
  işləyən adama da verə bilir. Səbəb: `CanGrant` sahəni yoxlayır, alanı yox. **Sual:** belə qalsın, yoxsa
  məhdudlaşsın?
- **Eyni səviyyə.** Təyinat hüququ olan operator öz səviyyəsində rol verə bilir: başqa operatora, hətta
  təyinat hüququ olan rola. Qayda «öz hüquqlarının alt çoxluğu»dur və bərabərliyi də əhatə edir. Eyni
  səviyyəli iki operator bir-birinin PIN-ini və profilini dəyişə bilir — bu, bugünkü həmkar qaydasının
  yeni rollardakı qarşılığıdır.
- **Faza 3 ekranı.** Təyinat ekranı «bu hüquq artıq sistemdən gəlir» halını özü göstərməlidir, yoxsa
  istifadəçi 23505 alar.

### 15.4 Şəbəkə ayrılması

Staging-in prod bazasına qoşulmadığı konfiqurasiya səviyyəsində təsdiqləndi. Şəbəkə səviyyəsində ayrılma
barədə infra qeydi bu sənədə yox, sahibə ayrıca verilib, çünki repo açıqdır. Prod infra dəyişikliyidir: ayrıca
təsdiqlə və pik saatlardan kənarda edilir.

### 15.5 Staging-in indiki vəziyyəti və geri dönüş

- **Rejimlər:** `test` = `Shadow` (1), `bax` və `loadtest` = `Legacy` (0).
  - Geri qaytarmaq: `UPDATE "Tenants" SET "AuthzMode" = 0 WHERE "Slug" = 'test';`
  - Dəyişiklik 30 s keşdən sonra qüvvəyə minir.
- **Test filialları:** `test`-in 4 menecerinə 16 `ManagedLocations` sətri yazılıb.
  - Geri qaytarmaq: `DELETE FROM "ManagedLocations" WHERE "TenantId" = (SELECT "Id" FROM "Tenants" WHERE "Slug" = 'test');`
  - Sonra backend restart.
- **Kod:** əvvəlki staging commit-inə qayıtmaq olar. Yeni cədvəllər əlavədir, köhnə kod onları oxumur.
- **Tam geri dönüş:** `qrlog_stg-2026-10-15-pre-access.dump`.

## 16. Faza 3 — rol və təyinat (2026-10-15, branch `feat/permissions-phase3`)

Sahib Faza 2-nin staging nəticələrini qəbul etdi və Faza 3-ə icazə verdi. Qərarlar və onların kodda necə
ifadə olunduğu aşağıdadır. Hamısı yeni modeldədir, Faza 5-də qərar verməyə başlayır. İstisna yalnız davamiyyət
düzəlişidir: o, bu gündən hər iki rejimdə işləyir.

### 16.1 Sahibin qaydaları (2026-10-15)

1. **Hesab yalnız yuxarıdan dəyişir.**
   - Eyni strukturun iki operatoru bir-birinin PIN-ini, nömrəsini, profilini və rolunu dəyişə bilmir:
     `AccessRules.Outranks` — biri o birini əhatə edir, amma o biri onu əhatə etmir.
   - Bu qayda yalnız hədəf yeni modelin rolunu daşıyanda işə düşür.
   - Bugünkü həmkar qaydası və adminlərin bir-birini idarə etməsi olduğu kimi qalır. Şirkət boyu hüquq
     sahibi «şirkət hesabı»dır.
2. **Davamiyyətə düzəliş.**
   - Yalnız `attendance.correct` ilə olur.
   - Öz gününə düzəliş **heç kimə**, admin daxil, olmur. Bu, `LocationScopeRules.CanCorrectAttendanceAsync`
     və `ShadowRules.CorrectAttendance` ilə 9 yolda yoxlanılır: redaktə, əl ilə qeyd, açıq günləri bağlama,
     çıxışı ləğv, silmə, «qayıb» qoyma və götürmə, unudulmuş çıxışı təsdiq və rədd.
   - `void-fraud` və `unvoid` da öz gününə toxunmur.
   - Toplu bağlamada adamın öz günü sadəcə ötürülür.
   - 5 düzəliş yolunda səbəb **məcburidir** (3–500 hərf). Səbəb audit sətrinə «əvvəl → sonra» vaxtları ilə
     yazılır.
3. **Təyinat.**
   - Operator yalnız öz sahəsində işləyən adama rol verir (`AssigneeOutOfScope`). Başqa sahənin adamına
     vermək üçün ayrıca şirkət icazəsi var: `assignments.cross_area`.
   - Yalnız özündən **aşağı** rol vermək olar (`NotBelowYou`). Müqayisədə «hüquqlar haqqında hüquqlar»
     (`AccessPermissions.Meta`: təyinat, rol idarəsi) nəzərə alınmır. Səbəb: operator gündəlik hüquqlarını
     verib yalnız təyinat hüququnu saxlasa, yenə də bərabərini təyin etmiş olur.
   - Yalnız özündən aşağı adama rol vermək olar (`AssigneeNotBelowYou`).
   - Şirkət sahəsində təyinat hüququ olan hesab bərabərini də təyin edə bilər.
   - Geri alma (`CanRevoke`) eyni qaydalarla işləyir. Öz təyinatını geri almaq olmaz. Sistem sətrini əl ilə
     geri almaq olmaz — o, işçi formasından gəlir.

### 16.2 API — `/api/admin/access` (Faza 3-də yalnız Admin)

| Endpoint | Nə edir |
|---|---|
| `GET catalogue` | 59 icazə, Azərbaycanca adları, yalnız-şirkət və meta bayraqları; 6 şablon |
| `GET/POST roles`, `PUT roles/{id}`, `POST roles/{id}/archive · restore` | Şirkətin öz rolları. Kilidli rollar dəyişmir. İstifadədə olan rol arxivə getmir. Struktur və ya filial üzrə verilmiş rola şirkət hüququ əlavə olunmur. |
| `GET assignments` | Kim, hansı rol, harada; hər sətir üçün «bu adam geri ala bilərmi» |
| `GET people`, `GET people/{id}` | Təyinat üçün şəxs axtarışı; şəxsin rolları və nəticədə əldə etdiyi hüquqlar |
| `GET assignable?employeeId=` | Bu adama hansı rolu harada vermək olar — dialoq yalnız bunları göstərir |
| `POST assignments`, `POST assignments/{id}/revoke` | Rol vermək (filial üzrə bir neçə filiala birdən) və geri almaq |

Bütün yazılar haqqında:
- Hamısı audit-ə yazılır: 22 rol verildi, 23 geri alındı, 24 rol dəyişdi.
- Dəstək sessiyasında (impersonation) rol verilmir.
- Hüquqlar hər sorğuda bazadan oxunur, ona görə dəyişiklik növbəti sorğudan qüvvəyə minir. Logout və
  `TokenVersion` artımı yoxdur.

### 16.3 Ekran

- **`/admin/access` — «Səlahiyyətlər».**
  - **Təyinatlar:** axtarış; rol və mənbəyə görə filtr (əl ilə verilən / işçi formasından gələn); geri
    alınanlar; «+ Rol ver».
  - **Rollar:** kartlar, hüquqlar qruplarla; şablondan yeni rol; redaktə, arxiv, bərpa.
  - **Strukturlar:** yaratmaq, adını dəyişmək, filialları seçmək, arxiv və bərpa. Faza 1 API-si üzərində
    qurulub, çünki operatoru təyin etmək üçün əvvəlcə struktur olmalıdır.
- **«Rol ver» paneli** üç addımdır: kim → hansı rol → harada. Yalnız icazə verilən seçimlər göstərilir.
  Operator üçün susmaya görə sahə struktur, tabelçi üçün filiallardır.
- **İşçi profilində «Hüquqlar» kartı:** rollar, sahələr, «nə edə bilər»; buradan rol vermək və geri almaq.
- **Davamiyyət düzəlişi pəncərəsi:** 4 hazır səbəb və sərbəst mətn. Profildə, işçilər siyahısında və açıq
  günlər səhifəsində `window.confirm`-in yerini tutur.

### 16.4 Testlər

| Dəst | Nəticə |
|---|---|
| Backend | 1284/1284. Faza 2-dən sonra 16 yeni test: 13 eskalasiya, 3 düzəliş |
| Frontend | 414/414 |
| Staging-ə birləşmiş kod | backend 1314/1314, frontend 422/422 |

Eskalasiya testləri bunları yoxlayır:
- operator ↛ operator; yalnız aşağıya;
- bugünkü həmkar qaydası və adminlər toxunulmazdır;
- heç kim öz gününə düzəliş etmir;
- qayda 1, 2, 3 və geri alma;
- API: iki filiallı tabelçi növbəti sorğudan qüvvədə, `TokenVersion` dəyişmir;
- API-nin hər rədd yolu: köhnə rol, platforma operatoru, özü, dəstək sessiyası, başqa şirkət, sistem sətri;
- rol redaktəsinin qaydaları;
- dialoq yalnız icazə veriləni təklif edir.

### 16.5 Staging yoxlaması (`test` şirkəti, real PostgreSQL)

Deploy-dan əvvəl ehtiyat nüsxə götürüldü. `Şirkət administratoru` rolu yeni icazəni startup-da özü aldı:
58-dən 59-a.

Ekranda edilənlər:
- «Aeroport (test)» strukturu yaradıldı, 4 filial ona salındı;
- operator struktur üzrə təyin olundu, tabelçi iki filialda;
- profildə «Hüquqlar» kartı yoxlandı;
- tabelçinin bir filialı geri alındı;
- açıq bir gün səbəblə bağlandı.

Bazada hamısı düzgündür:
- 3 əl ilə təyinat;
- audit: 20, 21 ×4, 22 ×3, 23, 7 — sonuncu «əvvəl → sonra» vaxtları və səbəblə;
- Shadow fərqi 0;
- backend xətası 0.

### 16.6 Açıq qalanlar

- Telefonla staging-də real QR skanı — sahibdədir. Prod təsdiqindən əvvəl mütləq edilməlidir.
- Faza 4: vahid panel. Təyinat hüququ olan operator öz ekranını orada alacaq.
- Faza 5: qərar yeni modelə keçir; hesabat endpoint-ləri öz icazə yoxlamasını alır (§14.4).

## 17. Faza 4 — vahid panel (2026-10-15, branch `feat/permissions-phase4`)

### 17.1 Sahibin qərarları

1. Operator başqa operator təyin etmir. Yalnız özündən aşağı rolları — tabelçi və müşahidəçi — öz
   sahəsində verir. Bu, Faza 3-ün `NotBelowYou` və `AssigneeNotBelowYou` qaydalarıdır.
2. `test` kölgə rejimində qalır. Legacy və Shadow-da faktiki icazə qərarları dəyişmir. Yeganə istisna
   sahibin seçdiyi qapıdır: «Səlahiyyətlər» API-si (§17.3).
3. Main merge və prod deploy yoxdur.

### 17.2 Davamiyyət düzəlişi — 11 yolun hamısında səbəb və audit

Faza 3-də öz gününə düzəliş 9 yolda bağlandı, amma səbəb yalnız 5 yolda məcburi idi: saat redaktəsi,
əl ilə qeyd, açıq günləri bağlama, çıxışı ləğv, silmə. Faza 4 qalan 6 yolu da eyni qaydaya salır:

| Yol | Faza 3 | Faza 4 |
|---|---|---|
| «Qayıb yaz» | öz günü bağlı, qeyd istəyə bağlı, audit yox | səbəb məcburi (qeydin özü), audit |
| «Qayıbı geri al» | öz günü bağlı, səbəb yox, audit yox | səbəb gövdədə, audit qoyulanda yazılan qeydlə |
| Unudulmuş çıxışı təsdiq | öz günü bağlı, audit yox | səbəb, audit işçinin öz səbəbi ilə |
| Unudulmuş çıxışı rədd | öz günü bağlı, audit yox | səbəb, audit |
| Saxta girişi ləğv | öz günü bağlı, səbəb boşdursa avtomatik mətn | səbəb məcburi, köhnə mətn ekranda ilk hazır variantdır |
| Ləğvi geri al | öz günü bağlı, səbəb yox | səbəb, audit ləğvin səbəbi ilə |

Hamısı eyni qaydadır: 3–500 hərf, əl ilə yoxlanır (record atributu yox), audit `RecordEditedByAdmin`
ilə «… Səbəb: …» formasında. Ekranlarda bir dialoq var, hər yolun öz hazır variantları ilə.

### 17.3 «Səlahiyyətlər» API-si — modelin açdığı yeganə qapı

- Qapı: bugünkü admin (əvvəlki kimi) **və ya** hüququ yeni modeldə hardasa olan şəxs. Görmək üçün
  `assignments.view`, vermək və geri almaq üçün `assignments.manage` lazımdır. Bugünkü rolu nə olursa olsun.
- Harada edə biləcəyi hər əməliyyatın öz qaydasıdır (`CanGrant`, `CanRevoke`, `CanSeeArea`) və dəyişmir.
- «Təyin edilə bilənlər» cavabı şirkət boyu olmayan şəxsə yalnız öz sahəsinin adlarını qaytarır.
- Bu controller-də `[Permission]` yoxdur. Kölgə bugünkü qapını modellə müqayisə edir, burada isə qapının
  özü modeldir.
- Şablon «Struktur operatoru»nda `assignments.manage` standart olaraq **yoxdur** (§3.4: «istəyə bağlı,
  sahib seçir»). Şirkət onu «Rollar»da bir kliklə əlavə edir. Bundan sonra operator öz strukturunda
  tabelçi və müşahidəçi təyin edir.

### 17.4 Panel

- `GET /api/me/access` qaytarır: `legacyRole`, `panel`, rolların və strukturların adları, şəxsin çatdığı
  filiallar.
- Panel bu cavabı açılanda və tab yenidən görünəndə oxuyur. Rol verilən adam çıxıb-girmədən görür.
- `panelAccess`-də hər səhifə iki sütun daşıyır: bugünkü rollar və yeni modeldə səhifəni açan icazələr.
  - Keçidə qədər qərarı rol verir.
  - İcazə sütunu yalnız «keçiddən sonra nə açılacaq»ı göstərir.
  - Test: admin və bugünkü menecer üçün iki sütun eyni səhifələri açır. Keçid heç kimdən funksiya
    almır.
- Menyudakı rol şərtləri silindi. Sətir yalnız cədvəllə açılır və bağlanır.
- Yeni rolu olan adam bu gün adi işçi ola bilər. Belə adam:
  - panelə girir;
  - «Mənim sahəm»i görür: rollar, filiallar, hüquqlar, indi açıq olanlar və keçiddən sonra açılacaqlar;
  - menyuda «Keçiddən sonra açılacaq» bölməsini boz rəngdə görür;
  - işçi menyusunda «İdarəetmə paneli» sətrini görür. Skan ekranı bu sorğunu gözləmir.

### 17.5 Bir işçi siyahısı

- `/admin/employees` admin və menecer üçün birdir.
  - Menecer üçün məlumat öz endpoint-lərindən gəlir: öz filialları, maaşsız, rolsuz.
  - Hər sətirdə serverin «dəyişə bilərmi» cavabı var. Həmkar sətri yalnız baxış üçündür.
  - Toplu icazə menecer marşrutu ilə gedir: icazəsi olmayan sətirlər ötürülür.
  - Şirkət üzrə PIN masası siyahının üstündədir.
  - Redaktə şəxsin öz kartından oxunur, siyahı sətrindən yox.
- Menecer formasında vəzifə artıq kataloqdan seçilir. Əvvəl sərbəst mətn idi.
- `/admin/my-employees` vahid siyahıya yönləndirilir. Köhnə menecer səhifəsi silindi.
- Vahid `/api/employees` endpoint-i Faza 5-ə saxlanıldı: onun sahəsini yeni model qərar verməlidir.
  Faza 4-də ekran birdir, məlumat isə mövcud iki əkiz endpoint-dən gəlir. Beləliklə kimin nəyi gördüyü
  dəyişmir.

### 17.6 Yolda tapılan xətalar

- **Menecer siyahısı fərdi QR-siz və GPS ayarlarını silirdi.**
  - Siyahı sorğusu bu iki sahəni oxuyurdu, amma cavabda atırdı.
  - Redaktə forması sətri geri göndərdiyi üçün hər saxlama onları sıfırlayırdı.
  - Düzəliş həm bu branch-dədir, həm də main-dən ayrıca hotfix branch-indədir:
    `fix/manager-roster-overrides`. Merge və deploy edilməyib.
- **Toplu «Növbəni ləğv et» köhnə vəziyyəti oxuyurdu.** Ya heç nə etmirdi, ya da seçilmiş növbəni təyin
  edirdi.
- **Profildə menecerin «Redaktə et»i işləmirdi.**
- **Yeniləmə hesabı girişsiz qoya bilirdi.** İndi telefon və ya email tələb olunur (`NeedEmailOrPhone`),
  dəvətdə olduğu kimi.
- **403 ≠ boş.** Yeddi səhifədə gəlməyən siyahı «Qeyd yoxdur» kimi çəkilirdi: Məzuniyyət, Doğum günləri,
  Tapşırıqlar, Növbələr, Filiallar, İşçilər, Elanlar, həmçinin profilin alt siyahıları. İndi boş sətir
  «icazəniz yoxdur» və ya «yüklənmədi» deyir.
  - Menecerin aça bildiyi heç bir əsas siyahı yalnız admin üçün olan endpoint-dən gəlmir (yoxlandı).
  - Normal istifadədə 403 yalnız funksiyası söndürülmüş «Elanlar»da yaranırdı.

### 17.7 Testlər və ölçülər

- Backend 1323 test. Frontend 440 test, build yaşıl.
- İşçi tətbiqinin ilkin yükləməsi: 129,5 → 130,8 KB gzip (+1,3 KB, +1%). Bütün JS: +1,6 KB, çünki köhnə menecer
  səhifəsi silindi.
- Sorğu sayı:
  - menecerin siyahısı açılanda 5 sorğu, əvvəl də 5 idi; vəzifələr artıq yalnız forma açılanda oxunur;
  - filtr və səhifə dəyişəndə 0 sorğu;
  - panel hər açılanda və tab hər qayıdanda +1 (`/api/me/access`).

### 17.8 Açıq qalanlar

- Sayğac və zəng hələ yalnız admindədir.
- Faza 5: qərar yeni modelə keçir, vahid `/api/employees`, əkiz endpoint-lər ləqəbə çevrilir.
