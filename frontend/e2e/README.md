# QRLog E2E — rol əsaslı testlər

Playwright dəsti **staging**-ə (https://test.qrlog.az) qarşı işləyir. Hər şey real API ilə gedir, saxta
cavab yoxdur. Production-a, `main`-ə və staging deploy-a toxunmur.

```bash
cd frontend
npm run e2e            # bütün dəst: setup → testlər → teardown → təmizlənmiş report
npm run e2e:report     # HTML report-u açır (screenshot-lar, trace-lər)
npx playwright test e2e/03-scope.spec.ts    # bir fayl
```

## Giriş məlumatları

Dəstə yalnız **şirkət administratorunun** girişi verilir:

| Dəyişən | Nə |
|---|---|
| `E2E_ADMIN_PHONE` | admin-in telefon nömrəsi |
| `E2E_ADMIN_PIN` | admin-in PIN-i |

Bu dəyişənlər iki yerdən oxunur:
- **mühit dəyişənləri** (cloud mühitinin ayarları, CI secret);
- **`frontend/.env.e2e.local`** — `.env.*.local` qaydası ilə git-ə düşmür.

Kodda, git-də və loglarda heç bir PIN yoxdur. Dəyişən çatışmırsa, hər test səbəbi ilə birlikdə *skip* olunur.

Digər rolların hesablarını dəst admin vasitəsilə özü hazırlayır. Bu, adminin öz ekranındakı yoldur:
1. «İşçi əlavə et → Müvəqqəti PIN» ilə hesab yaradılır.
2. Hər run-da admin hesabın PIN-ini sıfırlayır.
3. Run müvəqqəti PIN ilə daxil olur və `/set-pin`-in endpoint-i ilə yeni təsadüfi PIN təyin edir. Bu, hər yeni işçinin keçdiyi yoldur.

Bu PIN-lər yalnız run-ın yaddaşında yaşayır. Teardown onları yenidən sıfırlayır. Bununla run-ın açdığı bütün sessiyalar (trace-lərdəki token-lər də) etibarsız olur və run-lar arasında heç kimdə işlək E2E PIN-i qalmır.

**Cihaz bağlama bypass olunmur.** Web login cihaz bağlamır: bağlama yalnız dəvət linki ilə aktivləşdirmədə və skanda olur, dəst isə heç birini etmir.

## Staging-də nə yaradılır

Hamısı `test` şirkətindədir, adı `E2E` ilə başlayır və növbəti run-da adı ilə yenidən tapılır (iki dəfə yaradılmır).

| Nə | Ad |
|---|---|
| Filiallar | E2E Filial A1, E2E Filial A2 (hər ikisi «E2E Struktur»da), E2E Filial B (strukturdan kənar) |
| Növbələr | E2E Növbə Səhər (08–17), E2E Növbə Axşam (14–23), hər ikisi A1-ə bağlı |
| Rol hesabları | E2E Operator (struktur operatoru, E2E Struktur), E2E Tabelçi (A1), E2E Müşahidəçi (E2E Struktur), E2E Menecer (köhnə qayda, B) |
| Hədəf işçilər | E2E İşçi A1 / A2 / B — heç vaxt daxil olmurlar |
| Testlərin əlavə etdiyi işçilər | `E2E Yeni …` — test bitəndə admin silir |

Login-lər `…@e2e.qrlog.invalid` ünvanlarıdır. `.invalid` rezerv olunmuş domendir, heç kimə aid ola bilməz.

Mövcud işçilərə, filiallara və rollara heç bir yazı edilmir.

`e2e/support/env.ts` `test.qrlog.az`-dan başqa host-u rədd edir. İstisna yalnız `E2E_ALLOW_NON_STAGING=1` ilədir, məsələn lokal `docker compose`.

## Report-larda gizli məlumat

- **PIN daxil edilməsi.** PIN-i ancaq `01-login.spec.ts` yazır. O, `evaluate()` ilə yazır, çünki `fill()` dəyəri report-un addım adına yazır. Bu fayl trace-siz işləyir.
- **Ekran görüntüləri.** Sidebar-dakı istifadəçi login-i və müvəqqəti PIN qutusu bütün screenshot-larda bulanıqlaşdırılır.
- **Təmizləyici reporter.** `support/scrub-reporter.ts` report siyahısında sonuncudur. O, bütün trace və report fayllarından bunları silir:
  - JWT-lər;
  - credential sahələri (`password`, `newPin`, `tempPin`);
  - admin-in telefonu və e-poçtu.

  İkinci yoxlamada nəsə qalsa, run uğursuz sayılır.

## Testlər

| Fayl | Nə yoxlayır |
|---|---|
| `00-smoke` | sayt açılır, login səhifəsi görünür |
| `01-login` | 5 rolun hər biri login ekranından real giriş edir; yanlış PIN rədd olunur |
| `02-menu` | hər rolun menyusu dəqiq gözlənilən siyahıdır; menyuda olmayan səhifə URL ilə də açılmır; server sessiyaya rolun icazələrini verir |
| `03-scope` | struktur və filial sərhədləri UI və API-də; sahədən kənara yazı rədd olunur |
| `04-tabelci` | Tabelçi işçi əlavə edir, redaktə edir, növbəsini dəyişir (API və UI) |
| `05-musahideci` | Müşahidəçi heç nəyi dəyişə bilmir — UI-da düymə yoxdur, API 403 verir, data dəyişmir |
| `06-canli-deyisiklik` | rol və filial verilib-alınanda açıq sessiya yenidən girişsiz yenilənir, token eyni qalır |
| `07-admin` | admin bütün şirkəti görür; «Səlahiyyətlər» ekranı |

## Məlum qüsurlar (`test.fail`)

Bu testlər staging-də real qüsur tapıb. Onlar «gözlənilən uğursuzluq» kimi işarələnib ki, dəst reqressiya siqnalı olaraq qalsın. Qüsur düzəldiləndə test «gözlənilmədən keçir» və işarəni götürmək lazım gəlir.

1. **Tabelçi işçini deaktiv edə bilir.** `employees.deactivate` icazəsi serverdə heç yerdə yoxlanmır. Tabelçidə bu icazə yoxdur, amma `PUT /api/manager/employees/{id}` ilə `isActive:false` keçir.
2. **Tabelçidə «İşçi əlavə et» düyməsi deaktivdir.** Tabelçidə `locations.view` yoxdur. Ona görə `GET /api/manager/locations` 403 qaytarır, filial siyahısı boş qalır və düymə deaktiv olur. API ilə əlavə etmək isə işləyir.
3. **Admin axtarışı böyük «İ» ilə başlayan adı tapmır.** Məsələn, «İsmayılov» və ya «E2E İşçi A1» 0 nəticə verir.
