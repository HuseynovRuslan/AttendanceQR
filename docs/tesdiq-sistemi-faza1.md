# Təsdiq sistemi — Faza 1: backend əsası və «Yeni işçi» axını

Bu mərhələ müraciət sisteminin backend əsasını qurur və bir axını başdan sona işlək edir:

**Tabelçi → Gözləyir → Operator → Təsdiq / Rədd → işçi hesabı → audit.**

Qalan 8 müraciət növü bu mərhələdə yoxdur. Server tətbiq edə bilmədiyi növü qəbul etmir, ona görə onların
`ChangeRequestType` dəyəri də hələ yoxdur. Dizayn prototipi (PR #47) bu backend-ə qoşulmayıb. Prototip yalnız dev
build-də açılır və serverə heç nə göndərmir.

## 1. Sahibin qərarları və kodda yeri

| # | Qərar | Kodda |
|---|---|---|
| 1 | Struktursuz filial Legacy axınında qalır, müraciəti Company Admin-ə getmir | `ApprovalScope.InArea` struktursuz filialı heç vaxt sahəyə salmır. `ApprovalAreaDesk` belə filialı aktivləşdirmir (`BranchHasNoStructure`) |
| 2 | Axın yalnız xüsusi aktivləşdirilmiş struktur və ya filialda işləyir | `ApprovalArea` cədvəli. Şirkət səviyyəsində açar yoxdur (`CK_ApprovalAreas_Scope`) |
| 3 | Tabelçinin birbaşa yazması yalnız aktiv sahədə məhdudlaşır, Legacy bunu keçə bilmir | `ApprovalGate.DirectChangeAsync` hüququ token-dəki roldan yox, icazə modelindən oxuyur. Bu, hər `AuthzMode`-da eyni işləyir |
| 4 | Saatlıq icazə üçün `LeaveRecord` genişlənir | Bu mərhələdə yoxdur, icazə növü ilə birlikdə gələcək |
| 5 | Operatora dərhal bildiriş və 24 saatdan çox gözləyənlər üçün gündə bir xatırlatma. Bildirişin xətası əsas əməliyyatı pozmur | `ApprovalNotifyWorker`. Müraciətin sütunlarından işləyir, endpoint push-a toxunmur |
| 6 | Operatoru yalnız səlahiyyətli Company Admin müddəti öz-özünə bitən təyinatla əvəz edir. Viewer hüququ heç vaxt avtomatik genişlənmir | `ApproverSubstitution`. Ən çox 31 gün, əvəz edən yalnız Company Admin ola bilər |
| 7 | Bayram mexanizminə toxunulmur | Toxunulmayıb |

**Şərh tələb edən bir yer (6-cı qərar).** «Səlahiyyətli Company Admin» belə oxunub: əvəz edən şəxs özü Company
Admin-dir, təyinatı da Company Admin edir. Əgər nəzərdə tutulan «Company Admin başqa operatoru əvəzedici təyin
edir» idisə, dəyişiklik bir yerdədir: `ApprovalAreaDesk.CreateSubstitutionAsync`-dəki
`SubstituteMustBeCompanyAdmin` yoxlaması.

## 2. İki açar — production-da hər ikisi bağlıdır

1. **Yerləşdirmə açarı** `Approvals__Enabled`:
   - production compose faylında açıq-aşkar `"false"` yazılıb, `.env`-dən oxunmur, ona görə açmaq üçün PR lazımdır;
   - lokal mühitdə default `false`-dur;
   - staging-də `true`-dur.

   Açar bağlı olanda:
   - müraciət endpoint-ləri 404 `ApprovalsDisabled` qaytarır;
   - menecer masası işçini bu günkü kimi birbaşa əlavə edir;
   - bildiriş işçisi heç nə etmir;
   - yeni cədvəllərdə nə olsa da, heç bir təsiri yoxdur.
2. **Sahə açarı** `ApprovalAreas`. Struktur və ya strukturun bir filialı Company Admin tərəfindən ayrı-ayrılıqda açılır.
   Sətir yoxdursa, heç nə dəyişmir.

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
| Bugünkü filial meneceri (`legacy_manager`) | Tabelçi kimi: müraciət göndərir, birbaşa yarada bilmir. Legacy şirkətdə filial menecerinə ayrıca rol vermək lazım deyil |
| Struktur operatoru (`structure_operator`) | Strukturunun müraciətlərini təsdiq və ya rədd edir. Birbaşa yarada bilir, bu `Direct` kimi yazılır |
| Müşahidəçi (`observer`) | Yalnız görür, qərar vermir. Ona heç vaxt qərar hüququ verilmir |
| Company Admin | Hamısını görür və birbaşa yarada bilir (`Direct`). Gündəlik təsdiqçi deyil: yalnız müddətli əvəzetmə ilə qərar verir |

- **Qərarı kim verir.** Filialın strukturuna və ya filialın özünə bağlı `requests.decide` sahibi, ya da həmin
  struktur üçün aktiv əvəzetməsi olan Company Admin.
- **Öz müraciətinə heç kim qərar vermir**, operator da daxil.
- **Rol şablonları:**
  - Kilidli şablonlar startup-da koddan sinxronlaşır: `company_admin` bütün hüquqlar, `legacy_manager` isə
    `requests.create` və `requests.view` alır.
  - Kilidsiz şablonlara (`structure_operator`, `timekeeper`, `observer`) yeni hüquqlar yalnız yeni yaradılan
    şirkətlərdə düşür. Mövcud şirkətdə sahib onları «Rollar» ekranından əlavə edir.
- **Dəstək və yalnız-baxış sessiyaları** (impersonation və view-only) müraciətləri oxuyur, amma yaza bilmir.

## 6. API (`/api/approvals`)

| Metod | Yol | Qeyd |
|---|---|---|
| GET | `/me` | Aktiv sahədə hansı filialda nə edə bildiyin və növbədə neçə müraciət olduğu |
| GET | `/requests?view=queue\|mine\|all&status=&locationId=&limit=` | `queue`: məndən qərar gözləyənlər (ən köhnəsi yuxarıda) |
| GET | `/requests/{id}` | Detal, payload, tarixçə, `canDecide`, `canCancel` |
| POST | `/requests/employee-create` | «Yeni işçi» müraciəti. `Idempotency-Key` başlığı ilə |
| POST | `/requests/{id}/approve` | `{ note? }`. Cavabda müvəqqəti PIN bir dəfə göstərilir |
| POST | `/requests/{id}/reject` | `{ note }`. Səbəb məcburidir, 3–500 hərf |
| POST | `/requests/{id}/cancel` | Yalnız müraciəti yaradan |
| GET, POST, DELETE | `/areas`, `/areas/{id}` | Sahəni açmaq və bağlamaq. Yalnız Company Admin |
| GET, POST, DELETE | `/substitutions`, `/substitutions/{id}` | Müddətli əvəzetmə. Yalnız Company Admin |

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

## 7. Miqrasiya təhlili — `AddApprovalRequests`

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
- **Yoxlanılıb (müvəqqəti lokal PostgreSQL, sonra silindi):**
  - bütün miqrasiya zənciri sıfırdan tətbiq olundu;
  - `Down` və təkrar `Up` işlədi;
  - real bazada 23 yoxlamanın hamısı keçdi:
    - iki təsdiqçinin eyni vaxtda basması (iki ayrı bağlantı);
    - unikal pozuntuda tranzaksiyanın geri qaytarılması;
    - eyni açarla və eyni şəxs üçün eyni anda yaradılan müraciətlər;
    - nömrə yarışı;
    - trigger;
    - yoxlama şərtləri;
    - başqa şirkətin filialına və müraciətinə kompozit açar qadağası.
- **Versiya qeydi:** yoxlama PostgreSQL 18-də aparılıb, production 16-dır. İstifadə olunan imkanların hamısı
  (qismən indeks, `NULLS NOT DISTINCT`, `EXECUTE FUNCTION` trigger-i) 16-da var.

## 8. Staging qəbul planı

Hər addımın gözlənilən nəticəsi yazılıb. Real rol təyinatı və `AuthzMode` dəyişikliyi bu planın hissəsi deyil.
Staging-də test strukturu və test şəxsləri istifadə olunur.

1. **Deploy və miqrasiya.** Branch staging-ə birləşdirilir.
   - Miqrasiya tətbiq olunur, sağlamlıq yoxlaması yaşıldır.
   - `/api/approvals/me` `enabled: true` qaytarır.
2. **Heç nə dəyişməyib.** Sahə açılmayıb.
   - Menecer işçini birbaşa əlavə edir (200).
   - `ChangeRequests` boşdur.
3. **Rollar (yalnız staging):**
   - test strukturunun «Struktur operatoru» roluna `requests.decide` əlavə olunur;
   - test operatoruna struktur səviyyəsində bu rol verilir;
   - bir filiala Tabelçi təyin olunur.
4. **Aktivləşdirmə qorumları.**
   - Operator rolu olmayan struktur: 409 `NoApproverInArea`.
   - Struktursuz filial: 409 `BranchHasNoStructure`.
   - Menecer aktivləşdirməyə çalışanda: 403.
   - Company Admin aktivləşdirəndə: 201, auditdə qeyd yaranır.
5. **Tam axın.**
   - Tabelçi müraciət yaradır (201, R-nömrə).
   - Operatorun növbəsində görünür.
   - Operator təsdiqləyir: işçi yaranır, müvəqqəti PIN göstərilir, tarixçə Yaradıldı → Təsdiqləndi → Tətbiq olundu.
6. **Qadağalar:**
   - tabelçi birbaşa yarada bilmir (403 `ApprovalRequired`);
   - tabelçi başqa filiala müraciət yarada bilmir (403);
   - müşahidəçi təsdiq edə bilmir (403);
   - öz müraciətinə qərar vermək olmur (403);
   - Company Admin əvəzetməsiz təsdiq edə bilmir (403).
7. **Rədd.** Səbəbsiz rədd: 400. Səbəblə rədd: Rədd edildi, işçi yaranmır.
8. **Təkrar.**
   - Eyni `Idempotency-Key` ilə ikinci sorğu: 200 təkrar cavab, sətir bir dənədir.
   - Eyni telefon üçün ikinci müraciət: 409 `DuplicatePendingRequest`.
9. **Geri qaytarma.**
   - Müraciət gözləyərkən eyni nömrə ilə işçi birbaşa yaradılır (sahədən kənar filialda).
   - Təsdiq 409 `ApplyRefused` qaytarır, müraciət Gözləyir qalır, ikinci hesab yaranmır.
10. **Əvəzetmə.**
    - Company Admin-ə 1 günlük əvəzetmə verilir: o, təsdiq edə bilir.
    - Əvəzetmə ləğv olunanda təsdiq edə bilmir.
11. **Bağlamaq.**
    - Sahə bağlananda birbaşa yaratma geri qayıdır.
    - Gözləyən müraciət hələ də təsdiq oluna bilir.
12. **Real PostgreSQL zondu.** Staging bazasında, geri qaytarılan tranzaksiya ilə:
    - trigger;
    - yoxlama şərtləri;
    - kompozit açarlar.
13. **Təmizlik.** Test sahəsi bağlanır, test rolları geri alınır.

## 9. Geri qaytarma planı

Ən təhlükəsizdən başlayaraq:
1. Sahəni bağlamaq. Həmin struktur dərhal bu günkü axına qayıdır.
2. Yerləşdirmə açarını bağlamaq. Bütün sistem bu günkü kimi işləyir, cədvəllərə toxunulmur.
3. Kodu geri qaytarmaq. Miqrasiya qala bilər.
4. Miqrasiyanı geri almaq (`Down`). Müraciət tarixçəsi silinir, ona görə yalnız sonuncu çarədir.

## 10. Bu mərhələdə olmayanlar

- **Panel ekranları** (Təsdiq mərkəzi, Operator paneli). Prototip PR #47-dədir, ayrıca mərhələdə real API-yə qoşulacaq.
- **Qalan 8 müraciət növü**, o cümlədən saatlıq icazə (4-cü qərar).
- **Toplu idxal və dəvət.** Bu yollara yalnız Company Admin daxil ola bilir, ona görə sahədə qadağa tələb etmir.
  Bu mərhələdə onlar `Direct` qeydi yazmır.
- **Filialı strukturdan çıxarmaq.** Belə filialın gözləyən müraciətinə qərar verən qalmır. Yaradan müraciəti ləğv
  edə bilər.
