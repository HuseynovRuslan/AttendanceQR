# Təsdiq sistemi — Faza 1: backend əsası və «Yeni işçi» axını

Bu mərhələ müraciət sisteminin backend əsasını qurur və bir axını başdan sona işlək edir:

**Tabelçi → Gözləyir → Operator → Təsdiq / Rədd → işçi hesabı → audit.**

Qalan 8 əməliyyat bu mərhələdə təsdiq axınına keçmir. Onlar aktiv sahədə də bu günkü kimi birbaşa işləyir
(bölmə 7). Server tətbiq edə bilmədiyi növü qəbul etmir, ona görə onların `ChangeRequestType` dəyəri də hələ yoxdur. Dizayn prototipi (PR #47) bu backend-ə qoşulmayıb. Prototip yalnız dev
build-də açılır və serverə heç nə göndərmir.

## 1. Sahibin qərarları və kodda yeri

| # | Qərar | Kodda |
|---|---|---|
| 1 | Struktursuz filial Legacy axınında qalır, müraciəti Company Admin-ə getmir | `ApprovalScope.InArea` struktursuz filialı heç vaxt sahəyə salmır. `ApprovalAreaDesk` belə filialı aktivləşdirmir (`BranchHasNoStructure`) |
| 2 | Axın yalnız xüsusi aktivləşdirilmiş struktur və ya filialda işləyir | `ApprovalArea` cədvəli. Şirkət səviyyəsində açar yoxdur (`CK_ApprovalAreas_Scope`) |
| 3 | Tabelçinin birbaşa yazması yalnız aktiv sahədə məhdudlaşır, Legacy bunu keçə bilmir | `ApprovalGate.DirectChangeAsync` hüququ token-dəki roldan yox, icazə modelindən oxuyur. Bu, hər `AuthzMode`-da eyni işləyir |
| 4 | Saatlıq icazə üçün `LeaveRecord` genişlənir | Bu mərhələdə yoxdur, icazə növü ilə birlikdə gələcək |
| 5 | Operatora dərhal bildiriş və 24 saatdan çox gözləyənlər üçün gündə bir xatırlatma. Bildirişin xətası əsas əməliyyatı pozmur | `ApprovalNotifyWorker`. Müraciətin sütunlarından işləyir, endpoint push-a toxunmur |
| 6 | Company Admin mövcud bir Operatoru konkret struktur üçün müvəqqəti təsdiqçi təyin edir. Ən çox 31 gün, vaxtı bitəndə öz-özünə sönür. Viewer-ə qərar hüququ verilmir. Yalnız həmin strukturun müraciətlərinə qərar və baxış verir, başqa heç nə | `ApproverSubstitution`. Əvəz edən hər hansı struktur və ya filialda `requests.decide` daşıyan Operator olmalıdır (`SubstituteMustBeOperator`) |
| 7 | Bayram mexanizminə toxunulmur | Toxunulmayıb |

**`legacy_manager` (sahib təsdiqləyib, 11.11).** Bugünkü filial menecerinin rolu `requests.create` və
`requests.view` daşıyır. Bunlar müvəqqəti keçid hüquqlarıdır və yalnız aktiv sahədə işləyir. `requests.decide` bu
rolda heç vaxt yoxdur və bunu test qoruyur.

## 2. İki açar — yerləşdirmə açarı production-da açıqdır, sahələr ayrı-ayrılıqda açılır

1. **Yerləşdirmə açarı** `Approvals__Enabled`:
   - production compose faylında açıq-aşkar yazılıb, `.env`-dən oxunmur, ona görə dəyişmək üçün PR lazımdır;
   - production-da 2026-11-12-dən `"true"`-dur. Açar açıq olanda sahə açılmamış heç bir filialda heç nə dəyişmir;
   - lokal mühitdə default `false`-dur;
   - staging-də `true`-dur.

   Açar bağlı olanda:
   - müraciət endpoint-ləri 404 `ApprovalsDisabled` qaytarır;
   - menecer masası işçini bu günkü kimi birbaşa əlavə edir;
   - bildiriş işçisi heç nə etmir;
   - yeni cədvəllərdə nə olsa da, heç bir təsiri yoxdur.
2. **Sahə açarı** `ApprovalAreas`. Struktur və ya strukturun bir filialı Company Admin tərəfindən ayrı-ayrılıqda açılır.
   Sətir yoxdursa, heç nə dəyişmir.
   - Sahə qərar verən olmadan açılmır (`NoApproverInArea`).
   - Struktur sahəsi üçün həmin strukturda **struktur səviyyəsində** `requests.decide` daşıyan aktiv operator lazımdır.
     Operator yalnız filiallara ayrı-ayrılıqda təyin olunubsa, struktur sahəsi açılmır, filial sahələri isə açıla bilər.

## 3. Məlumat modeli

Bütün cədvəllərdə `TenantId` var və hamısı sorğu filtri ilə şirkətə bağlıdır. Filial və strukturu göstərən xarici
açarlar kompozitdir: `(TenantId, LocationId)` və `(TenantId, StructureId)`. Kod da, əl ilə yazılmış SQL də başqa
şirkətin filialını göstərə bilməz.

| Cədvəl | Nə saxlayır |
|---|---|
| `ChangeRequests` | Müraciət, Gözləyir/Təsdiqləndi/Rədd edildi/Ləğv edildi. Payload `jsonb`-dədir. Şirkət daxilində ardıcıl nömrə var («R-1001»). Sahədə birbaşa əməliyyat `Direct = true` sətri kimi yazılır, nömrəsi olmur |
| `ChangeRequestEvents` | Tarixçə: Yaradıldı, Təsdiqləndi, Rədd edildi, Ləğv edildi, Tətbiq olundu, Tətbiq alınmadı, Birbaşa. Yalnız əlavə olunur: `UPDATE` trigger ilə qadağandır |
| `ApprovalAreas` | Aktiv sahələr. Silinmir, bağlanır |
| `ApproverSubstitutions` | Company Admin-in müddətli əvəzetməsi. Silinmir, ləğv olunur |

Verilənlər bazasının öz qorumları:
- `CK_ChangeRequests_Decision`:
  - gözləyən müraciətdə qərar yoxdur;
  - yekun müraciətdə qərar verən və vaxt var;
  - təsdiqlənmiş müraciətdə tətbiq vaxtı da var.
- `CK_ChangeRequests_RejectReason`: rəddin səbəbi boş ola bilməz.
- `CK_ChangeRequests_Direct`, `CK_ChangeRequests_Number`, `CK_ChangeRequests_Status`.
- `IX_ChangeRequests_OnePending`: eyni şəxs (telefon, telefon yoxdursa e-poçt) üçün yalnız bir gözləyən müraciət ola bilər.
- `IX_ChangeRequests_TenantId_IdempotencyKey`: eyni düymə basışı yalnız bir müraciət yaradır.
- `IX_ChangeRequestEvents_OneDecision`: hər müraciətdə yalnız bir qərar sətri ola bilər.
- `CK_ApprovalAreas_Scope`: şirkət səviyyəsində sahə yaradıla bilməz. Unikal indeks: bir sahə üçün yalnız bir aktiv sətir.
- `CK_ApproverSubstitutions_Period`: əvəzetmə həmişə bitir.

## 4. Vəziyyət maşını

    Gözləyir ──təsdiq──▶ Təsdiqləndi  (eyni tranzaksiyada: işçi hesabı + Tətbiq olundu sətri)
       │ ├─rədd (səbəb məcburi)─▶ Rədd edildi
       │ └─ləğv (yalnız yaradan)─▶ Ləğv edildi
       └─ tətbiq alınmadı ─▶ Gözləyir qalır + «Tətbiq alınmadı» sətri

- **Qərar bir dəfə verilir.**
  - Keçid oxunan `Version`-a qarşı yazılır. İki təsdiqçi eyni anda bassa, biri qalib gəlir, digəri 409
    `AlreadyDecided` alır.
  - Eyni təsdiqçi düyməni təkrar bassa, eyni cavabı alır. İkinci hesab və ya ikinci PIN yaranmır.
- **Təsdiq elə dəyişikliyin özüdür.** Qərar sətri və işçi hesabı bir tranzaksiyadadır.
  - Hesab yazıla bilmirsə (məsələn, nömrə arada başqasına verilib), heç nə qalmır.
  - Müraciət Gözləyir vəziyyətində qalır, səbəb tarixçəyə yazılır, operator müraciəti rədd edə bilər.
- **Hüquq hər addımda yenidən oxunur:** müraciət yaradılanda da, qərar veriləndə də.

## 5. Kim nə edir

Yeni icazələr:
- `requests.create` — müraciət yaratmaq;
- `requests.decide` — təsdiq və rədd;
- `requests.view` — baxmaq.

Bunlar yalnız aktiv sahədə məna daşıyır.

| Kim | Aktiv sahədə |
|---|---|
| Tabelçi (`timekeeper`) | Öz filialı üçün müraciət yaradır. Birbaşa yarada bilmir: 403 `ApprovalRequired`, `canRequest: true` |
| Bugünkü filial meneceri (`legacy_manager`) | Tabelçi kimi: müraciət göndərir, birbaşa yarada bilmir. Legacy şirkətdə filial menecerinə ayrıca rol vermək lazım deyil. Bunlar müvəqqəti keçid hüquqlarıdır, `requests.decide` yoxdur |
| Struktur operatoru (`structure_operator`) | Strukturunun müraciətlərini təsdiq və ya rədd edir. Birbaşa yarada bilir, bu `Direct` kimi yazılır |
| Müşahidəçi (`observer`) | Yalnız görür, qərar vermir. Ona heç vaxt qərar hüququ verilmir |
| Company Admin | Hamısını görür və birbaşa yarada bilir (`Direct`). Gündəlik təsdiqçi deyil. Sahələri açır və əvəzedici Operator təyin edir |
| Əvəzedici Operator | Təyin olunduğu strukturun müraciətlərini görür və onlara qərar verir, vaxtı bitənə qədər. Həmin strukturda birbaşa dəyişiklik edə və müraciət yarada bilmir, öz müraciətinə qərar vermir. Operator rolu alınanda əvəzetmə də etibarsız olur |

- **Qərarı kim verir.** Filialın strukturuna və ya filialın özünə bağlı `requests.decide` sahibi, ya da həmin
  struktur üçün aktiv əvəzetməsi olan Operator.
- **Öz müraciətinə heç kim qərar vermir**, operator da daxil.
- **Rol şablonları:**
  - Kilidli şablonlar startup-da koddan sinxronlaşır: `company_admin` bütün hüquqlar, `legacy_manager` isə
    `requests.create` və `requests.view` alır.
  - Kilidsiz şablonlara (`structure_operator`, `timekeeper`, `observer`) yeni hüquqlar yalnız yeni yaradılan
    şirkətlərdə düşür. Mövcud şirkətdə sahib onları «Rollar» ekranından əlavə edir.
- **Dəstək və yalnız-baxış sessiyaları** (impersonation və view-only) müraciətləri oxuyur, amma yaza bilmir.

### Şəxsi məlumatlar serverdə gizlədilir

Müraciəti görmək hüququ (`requests.view`) şəxsin telefonunu, e-poçtunu və doğum tarixini görmək demək deyil.
Detal cavabında bu üç sahə serverdə silinir və `contactsHidden: true` qaytarılır. Qayda işçi kartındakı ilə eynidir:
filialda `employees.contacts.view` hüququ olmalıdır.

Bu sahələri görənlər:
- müraciəti yaradan (məlumatı özü yazıb);
- gözləyən müraciətə qərar verən operator və ya əvəzedici Operator (təsdiq etdiyi məlumatı görməlidir). Qərardan
  sonra adi qayda tətbiq olunur;
- filialda `employees.contacts.view` hüququ olanlar (operator, bugünkü menecer, Company Admin).

Müşahidəçi ad, filial, status və tarixçəni görür, nömrələri görmür. Müraciətin yaratdığı hesab sonradan menecer və
ya admin edilibsə, bu sahələri yalnız şəxsin özü və Company Admin görür. Siyahı cavabında şəxsi sahə ümumiyyətlə
yoxdur, yalnız ad var.

## 6. API (`/api/approvals`)

| Metod | Yol | Qeyd |
|---|---|---|
| GET | `/me` | Aktiv sahədə hansı filialda nə edə bildiyin və növbədə neçə müraciət olduğu |
| GET | `/options?locationId=` | «Yeni işçi» forması üçün vəzifə kataloqu və filialın növbələri (Faza 2) |
| GET | `/requests?view=queue\|mine\|all&status=&locationId=&limit=` | `queue`: məndən qərar gözləyənlər (ən köhnəsi yuxarıda) |
| GET | `/requests/{id}` | Detal, payload, tarixçə, `canDecide`, `canCancel` |
| POST | `/requests/employee-create` | «Yeni işçi» müraciəti. `Idempotency-Key` başlığı ilə |
| POST | `/requests/{id}/approve` | `{ note? }`. Cavabda müvəqqəti PIN bir dəfə göstərilir |
| POST | `/requests/{id}/reject` | `{ note }`. Səbəb məcburidir, 3–500 hərf |
| POST | `/requests/{id}/cancel` | Yalnız müraciəti yaradan |
| GET, POST, DELETE | `/areas`, `/areas/{id}` | Sahəni açmaq və bağlamaq. Yalnız Company Admin |
| GET, POST, DELETE | `/substitutions`, `/substitutions/{id}` | Əvəzedici Operator: `{ structureId, substituteEmployeeId, startsAtUtc?, endsAtUtc, note? }`. Yalnız Company Admin |

«Yeni işçi» müraciəti işçini birbaşa əlavə etmənin eyni qaydalarından keçir (`NewEmployee`). Menecer masası da
artıq bu qaydalardan istifadə edir:
- ad məcburidir;
- telefon və ya e-poçt olmalıdır;
- telefon və e-poçt təkrar ola bilməz;
- müvəqqəti PIN yaradılır;
- rol yalnız «İşçi» olur;
- növbə yalnız uyğun filialın növbəsi ola bilər.

Müraciət yolunda iki əlavə şərt var:
- vəzifə kataloqdan seçilməlidir;
- giriş istisnaları (səyyar giriş, QR-siz, GPS, foto) müraciətə daxil deyil, yeni işçi filialın qaydaları ilə başlayır.

## 7. Qalan 8 əməliyyat köhnə qaydada işləyir

Faza 1 yalnız «Yeni işçi»ni təsdiq axınına keçirir. Aşağıdakılar **aktiv sahədə də** bu günkü kimi birbaşa işləyir:
lazımi hüququ olan (tabelçi daxil) dəyişikliyi dərhal edir, müraciət yaranmır, operatordan icazə soruşulmur.

| Əməliyyat | Bu gün necə işləyir | Hüquq |
|---|---|---|
| Məlumat dəyişikliyi | İşçi kartında «Redaktə et» | `employees.update` |
| Deaktiv / aktiv etmək | Kart və ya sətir menyusu | `employees.deactivate` |
| İşçini silmək | Yalnız admin | `employees.delete` |
| Saatlıq və ya günlük icazə | «Bugünkü davamiyyət» → «Səbəb seçin» → İcazə. Saatlıq icazə hələ yoxdur | `leaves.manage` |
| Məzuniyyət | Məzuniyyət ekranı və ya «Səbəb seçin» | `leaves.manage` |
| Xəstəlik | Məzuniyyət ekranı və ya «Səbəb seçin» | `leaves.manage` |
| Davamiyyət düzəlişi | Davamiyyət ekranı, «Qayıb yaz» | `attendance.correct` (tabelçi şablonunda yoxdur) |
| Növbə dəyişikliyi | Növbə təyini və günlük növbə dəyişikliyi | `schedules.assign` |

Hər biri öz mərhələsində, öz tətbiq yolu və testləri ilə müraciətə keçəcək. O vaxta qədər aktiv sahədə yalnız yeni
işçinin əlavə edilməsi müraciət tələb edir.

## 8. Miqrasiya təhlili — `AddApprovalRequests`

- **Nə edir:**
  - 4 yeni cədvəl, 20 indeks və 7 yoxlama şərti yaradır;
  - bir trigger funksiyası və bir trigger əlavə edir;
  - mövcud cədvəllərdə heç bir sütun, indeks və ya məlumat dəyişmir;
  - backfill yoxdur.
- **Kilidlər:**
  - yeni cədvəllər boşdur, ona görə əməliyyatların hamısı millisaniyələr çəkir;
  - xarici açarlar yaradılanda `Tenants`, `Employees`, `Locations`, `Structures` üzərində qısa müddətə
    `SHARE ROW EXCLUSIVE` kilidi tutulur;
  - bu kilid həmin cədvəllərə yazmanı tranzaksiya bitənə qədər gözlədir. Oxumanı gözlətmir;
  - ona görə tətbiq skan piklərindən kənarda edilməlidir (bu günkü deploy qaydası).
- **Tətbiq:**
  - EF migrasiyaları startup-da tətbiq edir;
  - hamısı bir tranzaksiyadır: ya hamısı yazılır, ya heç nə.
- **Geri qaytarma:**
  - `Down` trigger-i, funksiyanı və 4 cədvəli silir;
  - mövcud məlumata təsiri yoxdur;
  - kodu geri qaytarmaq üçün miqrasiyanı geri almaq lazım deyil: köhnə kod yeni cədvəlləri sadəcə oxumur.
- **Yoxlanılıb, həm PostgreSQL 16.15-də (production ilə eyni versiya), həm 18-də:**
  - müvəqqəti, ayrıca bazada yoxlanıb, sonra baza silinib. Production və staging bazalarına toxunulmayıb;
  - bütün miqrasiya zənciri (92 miqrasiya) sıfırdan tətbiq olundu;
  - `Down` və təkrar `Up` işlədi;
  - real bazada 30 yoxlamanın hamısı keçdi:
    - iki təsdiqçinin eyni vaxtda basması (iki ayrı bağlantı);
    - unikal pozuntuda tranzaksiyanın geri qaytarılması;
    - eyni açarla və eyni şəxs üçün eyni anda yaradılan müraciətlər;
    - nömrə yarışı;
    - trigger;
    - yoxlama şərtləri;
    - başqa şirkətin filialına və müraciətinə kompozit açar qadağası;
    - əvəzedici Operator: təyinat, üst-üstə düşən təyinatın rəddi, müşahidəçinin rəddi, təsdiq, birbaşa dəyişiklik qadağası;
    - müşahidəçinin detalında şəxsi məlumatın olmaması.

## 9. Staging qəbul planı

Hər addımın gözlənilən nəticəsi yazılıb. Real rol təyinatı və `AuthzMode` dəyişikliyi bu planın hissəsi deyil.
Staging-də test strukturu və test şəxsləri istifadə olunur.

1. **Deploy və miqrasiya.** Branch staging-ə birləşdirilir.
   - Miqrasiya tətbiq olunur, sağlamlıq yoxlaması yaşıldır.
   - `/api/approvals/me` `enabled: true` qaytarır.
2. **Heç nə dəyişməyib.** Sahə açılmayıb.
   - Menecer işçini birbaşa əlavə edir (200).
   - `ChangeRequests` boşdur.
3. **Hüquqların real vəziyyəti.** Rollardakı request hüquqları oxunur (yalnız oxuma):
   - `company_admin` hamısını, `legacy_manager` isə `requests.create` və `requests.view`-u startup-da avtomatik alır;
   - `structure_operator`, `timekeeper` və `observer` kilidsiz rollardır və avtomatik yenilənmir. Bu hüquqlar
     PR #48-dən əvvəl mövcud deyildi, ona görə mövcud şirkətlərdə bu rollarda heç biri yoxdur;
   - nəticə: sahə açılmazdan əvvəl bu üç rola hüquqlar əl ilə əlavə edilməlidir.
4. **Çatışmayan hüquqların təhlükəsiz təyini (yalnız staging).**
   - Yalnız rol redaktoru ilə (`PUT /api/admin/access/roles/{id}`): Company Admin, dəstək sessiyası olmadan.
     Kilidli rollar orada dəyişmir, hər dəyişiklik auditə yazılır (`AccessRoleChanged`). SQL ilə dəyişiklik edilmir.
   - «Struktur operatoru»: `requests.decide`, `requests.view`, `requests.create`.
   - «Tabelçi»: `requests.create`, `requests.view`.
   - «Müşahidəçi»: yalnız `requests.view`. `requests.decide` heç vaxt verilmir.
   - Dəyişiklik rolu daşıyan hamıya şamil olunur, amma aktiv sahədən kənarda heç nə etmir.
   - Sonra rollar test şəxslərinə verilir: operatora struktur səviyyəsində, tabelçiyə bir filialda.
   - Yoxlama: hər test şəxsi üçün `/api/approvals/me` gözlənilən imkanları göstərir. Sahə açılmayıbsa, siyahı boşdur.
   - Geri qaytarma: eyni redaktorla hüquqlar silinir, bu da auditə yazılır.
   - Sıra vacibdir: əvvəl hüquqlar, sonra sahə. Aktivləşdirmə qərar verən olmadan sahəni açmır.
5. **Aktivləşdirmə qorumları.**
   - Operator rolu olmayan struktur: 409 `NoApproverInArea`.
   - Struktursuz filial: 409 `BranchHasNoStructure`.
   - Menecer aktivləşdirməyə çalışanda: 403.
   - Company Admin aktivləşdirəndə: 201, auditdə qeyd yaranır.
6. **Tam axın.**
   - Tabelçi müraciət yaradır (201, R-nömrə).
   - Operatorun növbəsində görünür.
   - Operator təsdiqləyir: işçi yaranır, müvəqqəti PIN göstərilir, tarixçə Yaradıldı → Təsdiqləndi → Tətbiq olundu.
7. **Qadağalar:**
   - tabelçi birbaşa yarada bilmir (403 `ApprovalRequired`);
   - tabelçi başqa filiala müraciət yarada bilmir (403);
   - müşahidəçi təsdiq edə bilmir (403);
   - öz müraciətinə qərar vermək olmur (403);
   - Company Admin təsdiq edə bilmir (403).
8. **Şəxsi məlumatlar.** Müşahidəçinin detalında telefon, e-poçt və doğum tarixi yoxdur (`contactsHidden: true`).
   Operatorda və müraciəti yaradanda var.
9. **Rədd.** Səbəbsiz rədd: 400. Səbəblə rədd: Rədd edildi, işçi yaranmır.
10. **Təkrar.**
   - Eyni `Idempotency-Key` ilə ikinci sorğu: 200 təkrar cavab, sətir bir dənədir.
   - Eyni telefon üçün ikinci müraciət: 409 `DuplicatePendingRequest`.
11. **Geri qaytarma.**
   - Müraciət gözləyərkən eyni nömrə ilə işçi birbaşa yaradılır (sahədən kənar filialda).
   - Təsdiq 409 `ApplyRefused` qaytarır, müraciət Gözləyir qalır, ikinci hesab yaranmır.
12. **Əvəzetmə.**
    - Company Admin başqa strukturun operatorunu 1 günlüyə test strukturu üçün təyin edir: o, təsdiq edə bilir.
    - Müşahidəçini və ya Company Admin-in özünü təyin etmək: 409 `SubstituteMustBeOperator`.
    - Əvəzedici həmin strukturda birbaşa işçi yarada bilmir (403).
    - Əvəzetmə ləğv olunanda təsdiq edə bilmir.
13. **Bağlamaq.**
    - Sahə bağlananda birbaşa yaratma geri qayıdır.
    - Gözləyən müraciət hələ də təsdiq oluna bilir.
14. **Real PostgreSQL zondu.** Staging bazasında, geri qaytarılan tranzaksiya ilə:
    - trigger;
    - yoxlama şərtləri;
    - kompozit açarlar.
15. **Təmizlik.** Test sahəsi bağlanır, test rolları geri alınır, 4-cü addımda əlavə edilən hüquqlar silinir.

## 10. Geri qaytarma planı

Ən təhlükəsizdən başlayaraq:
1. Sahəni bağlamaq. Həmin struktur dərhal bu günkü axına qayıdır.
2. Yerləşdirmə açarını bağlamaq. Bütün sistem bu günkü kimi işləyir, cədvəllərə toxunulmur.
3. Kodu geri qaytarmaq. Miqrasiya qala bilər.
4. Miqrasiyanı geri almaq (`Down`). Müraciət tarixçəsi silinir, ona görə yalnız sonuncu çarədir.

## 11. Bu mərhələdə olmayanlar

- **Panel ekranları** (Təsdiq mərkəzi, Operator paneli). Faza 2-də qoşuldu: `docs/tesdiq-sistemi-faza2.md`.
- **Qalan 8 əməliyyat** (bölmə 7), o cümlədən saatlıq icazə (4-cü qərar).
- **Toplu idxal və dəvət.** Bu yollara yalnız Company Admin daxil ola bilir, ona görə sahədə qadağa tələb etmir.
  Bu mərhələdə onlar `Direct` qeydi yazmır.
- **Filialı strukturdan çıxarmaq.** Belə filialın gözləyən müraciətinə qərar verən qalmır. Yaradan müraciəti ləğv
  edə bilər.
