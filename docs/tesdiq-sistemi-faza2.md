# Təsdiq sistemi — Faza 2: Operator və Tabelçi ekranları

Status: **staging qəbulunu gözləyir** (2026-11-11). Faza 1-in backend-i (`docs/tesdiq-sistemi-faza1.md`) dəyişmir. Bu
mərhələ PR #47 prototipinin dizaynını real `/api/approvals` API-sinə qoşur. Yeni miqrasiya yoxdur.

## 1. Ekranlar

| Ekran | Yol | Kim görür |
|---|---|---|
| Operator paneli | `/admin/approvals/operator` | Aktiv sahədə ən azı bir filialın Operatoru (`isApprover`) |
| Təsdiq mərkəzi | `/admin/approvals` | Sahədə filialı görən hər kəs: siyahı, detal, audit tarixçəsi |
| Mənim filialım | `/admin/approvals/branch` | Tabelçi: müraciət göndərən, amma özü təsdiqləməyən və birbaşa yazmayan |

Üç ekranın hamısı «Müraciətlər» bölməsindədir. Dizayn prototipdən gəlir: `pages/approvals/approvals.css` və
`pages/approvals/shared.tsx` iki tərəfin ortaq hissəsidir. Prototip özü yenə də yalnız development build-dədir.

## 2. Rol serverdən gəlir, URL-dən yox

Rolu iki cavab müəyyən edir: `/api/me/access` (Company Admin) və `/api/approvals/me` (filial-filial imkanlar). Hər ikisi
yalnız aktiv sahədəki filialları qaytarır. Rolun adı ingiliscə yazılır, ekranın qalanı azərbaycancadır.

| Rol | Şərt |
|---|---|
| Company Admin | `/api/me/access` → `companyAdmin` |
| Operator | hər hansı filialda `isApprover` |
| Timekeeper | `canRequest`, amma `isApprover` və `canActDirectly` yoxdur |
| Viewer | yalnız `canView` və Company Admin deyil |

`/api/approvals/me` cavabına filialın strukturu və bu imkanlar əlavə olundu: `structureId`, `structureName`,
`canRequest`, `canActDirectly`, `isApprover`, `standsIn`, `canView`. Yalnız oxuyan şəxs də öz filiallarını görür.

## 3. Avtomatik aktivləşmə yoxdur

Ekran yalnız üç şərt birlikdə ödənəndə açılır:
- serverin açarı açıqdır (production-da bağlıdır);
- şirkətdə aktiv sahə var;
- şəxsin icazəsi var: `requests.view`, `requests.create` və ya `requests.decide`.

Açar bağlıdırsa və ya sahə yoxdursa, menyu bölməsi boş qalır. Birbaşa link isə «Təsdiq axını aktiv deyil» mesajını
göstərir. Panelin `MODEL_GATED` qaydası da qüvvədədir: Legacy rejimində bu səhifələri yalnız uyğun rol açır.

## 4. «Yeni işçi» müraciəti

- Hazırda yalnız bu növ işləyir. Qalan 8 əməliyyat köhnə qaydada, birbaşa edilir. Tabelçi ekranı bunu açıq yazır,
  həmin əməliyyatlar üçün düymə göstərmir.
- Forma vəzifəni kataloqdan, növbəni isə filialın və şirkətin növbələrindən təklif edir. Bunlar yeni
  `GET /api/approvals/options?locationId=` endpoint-indən gəlir. Endpoint yalnız müraciət göndərə bilənə və ya
  Operatora açıqdır.
- Hər göndərişin öz `Idempotency-Key`-i var. Şəbəkə kəsiləndə açar saxlanılır, «yenidən göndər» eyni müraciəti
  qaytarır. Server cavab verəndən sonra açar yenilənir. Düymə gözləmə müddətində bağlıdır, ikinci basış heç nə
  göndərmir.

## 5. Qərar

- «Təsdiqlə» və «Rədd et» detalın sonundadır. Telefonda isə ekranın aşağısına yapışır, baş barmağın altında qalır.
- Rədd etmək üçün səbəb məcburidir, 3 hərfdən az olmamalıdır. Tabelçi səbəbi öz siyahısında görür.
- Təsdiqdən sonra müvəqqəti PIN bir dəfə, dialoqda göstərilir. PIN yalnız React state-indədir: localStorage-a,
  sessionStorage-a və URL-ə yazılmır. Dialoq bağlananda yox olur. Lazım olsa, yeni PIN «PIN sıfırla» ilə verilir.
- 409 (başqası artıq qərar verib), 403, 404, şəbəkə xətası və bitmiş sessiya üçün azərbaycanca mesaj göstərilir.
  Hər cavabdan sonra siyahı yenilənir.

## 6. Şəxsi məlumatlar

Viewer üçün telefonu, e-poçtu və doğum tarixini server özü gizlədir (Faza 1). Ekran «Gizlədilib» yazır və bunun
səbəbini izah edir. Qərar düymələri Viewer-ə göstərilmir. Göstərilsəydi də, server 403 qaytarardı.

## 7. Operator konsolunda PIN

Bu PR operator konsolundakı «PIN sıfırla» xətasını da düzəldir. Sıfırlama serverdə uğurla keçirdi, amma ardınca
siyahı yenilənəndə yeni PIN ekrandan silinirdi. İndi PIN yenilənmədən sonra da ekranda qalır, növbəti axtarışda
silinir. Bu davranış test ilə qorunur.

## 8. Staging qəbul planı

Hər addım üçün sahibin ayrıca təsdiqi lazımdır. Production-a toxunulmur.

1. **Hazırlıq.** PR `main`-ə merge olunandan sonra `main` staging-ə `--no-ff` ilə birləşdirilir, force-push
   edilmir. Bundan əvvəl staging bazasının backup-ı götürülür. Miqrasiya yoxdur.
2. **Test hesabları.** Üç E2E hesabın PIN-i məlum deyil. PIN-lər yalnız staging-də, rəsmi «PIN sıfırla» axını ilə
   sıfırlanır.
3. **Sahə.** Ayrıca yaradılmış E2E strukturunda sahə açılır. Real strukturlara toxunulmur.
4. **Timekeeper (telefon eni).** Menyuda yalnız «Təsdiq mərkəzi» və «Mənim filialım» görünür. Göndərilən «Yeni işçi»
   müraciəti «Gözləyir» statusunda olur. İkiqat basış bir müraciət yaradır. Şəbəkə kəsilib bərpa olunanda da bir
   müraciət yaranır.
5. **Operator.** Operator paneli növbənin sayını və ən köhnə müraciəti yuxarıda göstərir. Detalda audit tarixçəsi
   görünür.
6. **Rədd.** Səbəbsiz rədd etmək olmur. Səbəblə rədd ediləndə Timekeeper həmin səbəbi görür.
7. **Təsdiq.** PIN bir dəfə görünür, səhifə yenilənəndən sonra artıq görünmür. Brauzer yaddaşında PIN yoxdur.
   Yaradılan işçi «İşçilər» siyahısında düzgün filialdadır. Tarixçə: Created → Approved → Applied.
8. **Eyni anda iki qərar.** İki tabda eyni müraciət açılır. İkinci qərara «artıq qərar verilib» mesajı gəlir (409).
9. **Viewer.** Siyahını və detalı görür, üç sahə «Gizlədilib» yazılır, düymə yoxdur. API ilə birbaşa təsdiq cəhdi
   403 qaytarır.
10. **Başqa struktur.** Başqa strukturun müraciəti nə siyahıda görünür, nə də `?id=` ilə açılır (404 mesajı).
11. **Sessiya.** Token silinəndə istifadəçi giriş səhifəsinə qaytarılır.
12. **Sahə bağlananda.** Bölmə menyudan yox olur, birbaşa link «aktiv deyil» mesajını göstərir.
13. **Company Admin.** Bütün sahə filiallarını görür, rolu «Company Admin» yazılır.
14. **Production izolyasiyası.** Yalnız oxumaqla yoxlanır: production-da `/api/approvals/me` → `enabled: false`.
15. **Təmizlik.** Tarixçəsi olan test işçiləri silinmir, deaktiv edilir. Sahə bağlanır. Ortaq rollara edilən
    dəyişikliklər geri qaytarılır.

## 9. Geri qaytarma

Frontend-də olan dəyişiklik əvvəlki build-ə qayıtmaqla geri alınır. Backend dəyişiklikləri yalnız oxuyur: yeni
`/options` və `/me`-dəki əlavə sahələr. Miqrasiya yoxdur, ona görə baza dəyişmir.
