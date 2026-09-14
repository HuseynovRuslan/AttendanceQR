# HR modulu — məzuniyyət balansı və təsdiq axını

Bu sənəd QRLog-a əlavə olunan HR modulunun spesifikasiyasıdır. **Claude Code ilə işləyəndə
sessiyanın əvvəlində bu faylı oxut** — modulun bütün qərarları burada, təkrar izah etməyə ehtiyac
qalmır.

Mənbə: NovaHR (Next.js + Prisma prototipi). **Kod köçürülmür, model köçürülür** — QRLog .NET 10 +
EF Core + PostgreSQL-dir, orada isə TypeScript + Prisma + SQLite. Köçən şey cədvəl quruluşu,
status axınları və Əmək Məcəlləsi qaydalarıdır.

---

## 1. Nə qururuq

QRLog məzuniyyəti **qeyd edir** (`LeaveRecord` — admin bir tarix aralığı yazır), amma **saymır**:
kimin neçə günü qalıb, sistem bilmir. Modul bu boşluğu doldurur.

İki hissə:

1. **Balans** — hər işçiyə hansı il üçün neçə gün düşür, neçəsi istifadə olunub, neçəsi qalıb.
2. **Təsdiq axını** — işçi sorğu göndərir → menecer təsdiqləyir → mövcud `LeaveRecord` yaranır.

## 2. Nə GƏTİRİLMİR

NovaHR-dəki bu cədvəllərin QRLog-da qarşılığı artıq var. **Təkrar yaratma:**

| NovaHR | QRLog-da onsuz da var |
|---|---|
| `Company` | `Tenant` |
| `Branch` | `Location` |
| `Position` | `JobPosition` |
| `WorkSchedule` | `Schedule` |
| `Holiday` | `NonWorkingDay` |
| `Department`, `Grade` | — (indi lazım deyil, sonra `JobPosition`-a sütun kimi) |
| işçi kataloqu | `Employee` |

NovaHR-dəki `attendance`, `payroll`, `recruitment`, `performance`, `reports` səhifələri boş
şablonlardır — onlar da gətirilmir. Davamiyyət və maaş QRLog-un öz modullarıdır.

---

## 3. POZULMAZ QAYDALAR

Bunlar məsləhət deyil. Hər biri bir dəfə real nasazlığa səbəb olub.

**① Hər yeni cədvəl `ITenantScoped` + qlobal sorğu filtri olmalıdır.**
Çoxşirkətlilik burada *fail-closed*-dur: şirkətə aid edilə bilməyən sorğu **rədd olunur**, defolta
düşmür. NovaHR-dəki nullable `companyId` forması götürülmür. `IgnoreQueryFilters()` yalnız iki
yerdə icazəlidir (super-admin controller və qrup konsolu) — modulda **heç vaxt**.

**② `record` üzərində `[property: Range]` YAZMA.**
Hər sorğunu 500 edir, testlər isə səhvi təsdiqləyir. Bir dəfə skan yolunu dayandırıb. Yoxlamanı
əl ilə controller-də et.

**③ `Employee.LeaveDays`-ə TOXUNMA.**
O, maaş hesablamasının bölənidir. Mənası dəyişsə, 3 şirkətin maaş hesabatı səssizcə səhv olar.
Balans registri ondan **tamamilə ayrı** yaşayır.

**④ `Employee`-yə yeni sahə əlavə edirsənsə, frontend-də HƏR `updateEmployee(` çağırışını yenilə.**
`EmployeeUpdateRequest` buraxılan sahəni `null` edir — yəni qismən yeniləmə yazmadığın sahəni
**silir**.

**⑤ Miqrasiyalar tətbiq başlayanda özü işləyir.**
Səhv miqrasiya = deploy anında sistem qalxmır. Yeni `bool` sütun EF-də defolt `false` gəlir —
`true` lazımdırsa miqrasiyada əl ilə yaz.

**⑥ İstifadəçiyə görünən hər mətn Azərbaycan dilində.**
Kod və şərhlər ingiliscə, interfeys azərbaycanca.

**⑦ Admin panelində Tailwind YOX.**
`frontend/src/theme.css`-dəki semantik sinifləri işlət. Tailwind yalnız işçi ekranlarındadır.

**⑧ Yalnız `hr-modulu` budağına push et.** `main`-ə və `staging`-ə yox — ora keçid əl ilə,
Ruslan tərəfindən edilir.

**⑨ `ops/`, `docker-compose*`, `Caddyfile`, `.github/` fayllarına TOXUNMA.**
hr.test.qrlog.az onları onsuz da oxumur — mühit serverdə sabitlənib. Amma bu fayllar prod-a da
aiddir: səhv dəyişiklik oradan prod-a keçə bilər.

**⑩ Real işçi məlumatını heç yerə köçürmə** — nə test bazasına, nə də süni intellektə (Claude,
ChatGPT). Test üçün uydurma adlar və nömrələr yaz.

---

## 4. Cədvəllər

### Faza 1 — balans

```
LeaveEntitlement          kimə, hansı il üçün neçə gün düşür
  Id, TenantId, EmployeeId, Year
  BaseDays          Maddə 114.2 — 21 (və ya 114.3 — 30)
  ServiceDays       Maddə 116 — staja görə əlavə
  ExtraDays         Maddə 115/117 — zərərli iş, uşaqlı qadın
  TotalDays         hesablanmış cəm
  IsManualOverride  admin əl ilə dəyişibsə, təkrar hesablama üzərinə yazmır
  CalculatedAtUtc

LeaveLedgerEntry          balansın hər hərəkəti — ƏLAVƏ-YALNIZ
  Id, TenantId, EmployeeId, Year
  EntryType         OpeningBalance | Accrual | CarryForward | LeaveTaken
                    | LeaveCancelled | ManualAdjustment | Expiry
  Days              işarəli: istifadə mənfi, əlavə müsbət
  EffectiveDate
  ReferenceType     LeaveRecord | Import | Manual
  ReferenceId
  Note, CreatedByEmployeeId, CreatedAtUtc
```

**Registr əlavə-yalnızdır.** Düzəliş sətri silmir — **tərs sətir** yazır. Beləcə tarixçə həmişə
bərpa oluna bilir. Bu, QRLog-un `AuditLogs` fəlsəfəsi ilə eynidir.

**Balans = sətirlərin cəmi.** Ayrıca «qalan gün» sütunu saxlanmır — saxlanılan rəqəm həmişə
registrdən sürüşür.

### Faza 2 — təsdiq

```
LeaveRequest
  Id, TenantId, EmployeeId, Type, FromDate, ToDate, Days
  Status            Pending | Approved | Rejected | Cancelled
  Note, CreatedAtUtc, DecidedAtUtc
  LeaveRecordId     təsdiqlənəndə yaranan LeaveRecord-a istinad

LeaveApprovalStep
  Id, TenantId, LeaveRequestId, StepOrder
  ApproverEmployeeId, Decision, DecidedAtUtc, Comment
```

Təsdiqlənən sorğu **mövcud `LeaveRecord` yaradır**. Tabel, davamiyyət lövhəsi və maaş onsuz da
onu oxuyur — heç nə dəyişmir, heç nə sınmır.

NovaHR-in tam təsdiq mühərriki (kvorum, səlahiyyət ötürmə, şərtli pillələr) **birinci versiyada
lazım deyil**. Müştərilərdə zəncir 1–2 pillədir: işçi → birbaşa rəhbər → HR.

### Əmək Məcəlləsi qaydaları

`AzLabourCodeRules` — statik sinif, **rəqəmlərin yeganə yeri**. Başqa heç bir fayl bu rəqəmləri
təkrar yazmır.

| Maddə | Qayda |
|---|---|
| 114.2 | Əsas məzuniyyət — 21 gün |
| 114.3 | Ayrı-ayrı peşələr üçün uzadılmış əsas — 30 gün |
| 115 | Zərərli iş şəraiti — minimum +6 gün |
| 116 | Staja görə: 5–10 il +2, 10–15 il +4, 15+ il +6 (staj **bütün iş yerləri üzrə** toplanır — Konstitusiya Məhkəməsi, 29.11.2000) |
| 117 | 14 yaşadək 2 uşaq +3; 3 və daha çox, yaxud sağlamlıq imkanı məhdud uşaq +6 |

Mənbə: NovaHR `src/lib/leave/leave-labour-code-rules.ts`. **Orada bəzi rəqəmlər "medium/low
confidence" işarələnib — hüquqşünas təsdiqi olmadan istifadə etmə.**

---

## 5. Sidebar-da yeri

Menyu **funksiyaya görə yox, istifadə tezliyinə görə** qruplaşdırılıb (bax `AdminLayout.tsx`).
Ona görə HR **öz bölməsini açmır** — mövcud bölmələrə 3 sətir əlavə edir:

| Sətir | Bölmə | Kim görür |
|---|---|---|
| Məzuniyyət balansı | **İşçilər** | Admin + Menecer (menecer öz filialı) |
| Məzuniyyət sorğuları | **Müraciətlər** | Admin + Menecer (gözləyən sayı ilə) |
| Məzuniyyət qaydaları | **Tənzimləmələr** | yalnız Admin |

Sidebar-a **düşməyən** hissələr: sənədlər → işçi profilində tab; org-chart → işçilər səhifəsində
görünüş düyməsi; işdən çıxarma → profildə əməliyyat; dərəcə → vəzifələr səhifəsində sütun.

Bütün modul `hr` feature flag-i altındadır — sönülü şirkətdə üç sətrin heç biri görünmür.

---

## 6. Birinci tapşırıq

Bütün modulu bir anda yazma. Birinci PR bu olsun:

> **`LeaveEntitlement` cədvəli + miqrasiya + bir yalnız-oxu endpoint + sadə ekran.**
> Yəni: «Arzunun bu il 21 günü var». Hesablama yox, təsdiq yox — sadəcə zəncirin bütün qatlarına
> bir dəfə toxun.

2–3 günlük işdir. Bu keçəndən sonra balans mühərriki.

---

## 7. İş qaydası

```bash
docker compose up --build     # lokal: frontend 8081, backend 8080, postgres 15432
dotnet test                   # backend testləri — hamısı keçməlidir
cd frontend && npm run build   # tip yoxlaması + build
```

### Öz test saytın — hr.test.qrlog.az

`hr-modulu` budağına push → **https://hr.test.qrlog.az** bir neçə dəqiqəyə özü yayımlanır (ilk
build 5–10 dəqiqə çəkə bilər). Server parolu lazım deyil və verilmir.

- **Öz şirkətin, öz bazan:** şirkət `hr`. Heç kimlə paylaşılmır, real məlumat yoxdur. Foto, üz
  yoxlaması və push bildirişi bu mühitdə söndürülüb.
- **Giriş:** admin nömrəsi və müvəqqəti PIN Ruslandan alınır; ilk girişdə PIN-i dəyişirsən.
- **Build uğursuz olsa**, sayt əvvəlki versiyada qalır. Ona görə `dotnet test` və
  `npm run build` lokal keçməyincə push etmə.

### Əsas koddakı yenilikləri götürmək

QRLog hər gün dəyişir. Həftədə bir dəfə (və ya Ruslan desə) əsas kodu öz budağına birləşdir:

```bash
git fetch origin
git merge origin/staging      # konflikt çıxsa — dayan, həll etməyə çalışma, soruş
```

### İş hazır olanda

Pull Request aç: `hr-modulu` → `staging`. Ruslan baxır, qəbul edir, sonra prod-a özü çıxarır.

---

## 8. Claude Code ilə işləyəndə — kontekst çaşmasın

**① Bir sessiya = bir tapşırıq.** Tapşırıq bitəndə `/clear`. Köhnə tapşırığın konteksti yeni
tapşırığı çaşdırır — ən çox rast gəlinən səhv budur.

**② Sessiyanın əvvəlində bu faylı oxut:** «`docs/hr-modulu.md`-ni oxu, sonra ...». Root
`CLAUDE.md` onsuz da avtomatik yüklənir.

**③ Faylları söhbətə yapışdırma.** «Bu faylı oxu» de — özü oxusun. Yapışdırılan böyük fayl
kontekstin yarısını yeyir və heç nə qazandırmır.

**④ Əvvəlcə plan, sonra kod.** Böyük dəyişiklikdən əvvəl plan rejimində razılaş, sonra yazdır.

**⑤ Tez-tez commit et.** Kontekst dolsa, itən şey iş yox, sadəcə söhbət olsun.

**⑥ Testləri hər dəfə işə sal.** `dotnet test` keçmirsə, push etmə.

**⑦ `CLAUDE.md`-i və bu faylı dəyişmə** — onlar razılaşdırılmış qaydalardır. Qayda səhvdirsə,
əvvəlcə danış.
