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
| **Struktur operatoru** | Struktur | panel; davamiyyət (oxu, export); işçi (görmək, əlaqə, yaratmaq, dəyişmək, işdən çıxartmaq, PIN, nömrə); telefon (görmək, təsdiq, ləğv); növbə (görmək, idarə, təyin); məzuniyyət; filial (görmək, QR çapı); sahə ziyarətləri; təyinat (görmək, idarə — yalnız Tabelçi və Müşahidəçi); bildirişlər | `attendance.correct`, `field_checkin.grant`, `checkin_rules.override`, `locations.update` |
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
