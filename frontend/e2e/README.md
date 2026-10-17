# QRLog E2E — rol əsaslı testlər

Playwright dəsti **staging**-ə (https://test.qrlog.az) qarşı işləyir. Hər şey real API ilə gedir, saxta
cavab yoxdur. Production-a, `main`-ə və staging deploy-a toxunmur.

```bash
cd frontend
npm run e2e            # bütün dəst: setup → testlər → teardown (+ yoxlama) → təmizlənmiş report
npm run e2e:report     # HTML report-u açır (screenshot-lar, trace-lər)
npx playwright test e2e/03-scope.spec.ts    # bir fayl
```

## Giriş məlumatları — yalnız secret

Dəstə yalnız staging test şirkətinin **şirkət administratorunun** girişi verilir. O, yalnız mühit dəyişənlərindən oxunur:

| Dəyişən | Nə |
|---|---|
| `E2E_ADMIN_PHONE` | admin-in telefon nömrəsi |
| `E2E_ADMIN_PIN` | admin-in PIN-i |

Harada saxlanır:
- **cloud sessiyası:** mühit ayarları (sessiyanın başlığı → Environment → Edit) → secret / mühit dəyişəni;
- **CI:** repository secret;
- **laptop:** yalnız bir əmr üçün, məsələn `E2E_ADMIN_PHONE=… E2E_ADMIN_PIN=… npm run e2e`.

**Fayl oxunmur**, ona görə commit olunacaq fayl da yoxdur. Dəyişən yoxdursa, dəst testləri səssizcə skip etmir: setup aydın `E2E konfiqurasiya xətası` verir və run uğursuz bitir (CI üçün).

Digər rolların hesablarını dəst admin vasitəsilə özü hazırlayır. Bu, adminin öz ekranındakı yoldur:
1. «İşçi əlavə et → Müvəqqəti PIN» ilə hesab yaradılır.
2. Hər run-da admin hesabın PIN-ini sıfırlayır.
3. Run müvəqqəti PIN ilə daxil olur və `/set-pin`-in endpoint-i ilə yeni təsadüfi PIN təyin edir.

Bu PIN-lər yalnız run-ın yaddaşında yaşayır. Teardown onları yenidən sıfırlayır.

**Cihaz bağlama bypass olunmur.** Web login cihaz bağlamır: bağlama yalnız dəvət linki ilə aktivləşdirmədə və skanda olur, dəst isə heç birini etmir.

## Production qadağası

Bu qadağanın istisnası və ya override-ı yoxdur:
- **Host siyahısı.** `support/env.ts` yalnız bu host-ları qəbul edir: `test.qrlog.az`, `api-test.qrlog.az` və lokal stack (`localhost`, `127.0.0.1`). `*.qrlog.az` altındakı hər başqa host production sayılır və rədd olunur.
- **API köməkçiləri.** Hər API sorğusu göndərilməzdən əvvəl eyni yoxlamadan keçir.
- **Brauzer.** Hər brauzer kontekstində production host-una gedən sorğu bloklanır və həmin test uğursuz olur.
- **Şirkət yoxlaması.** Setup heç nə yazmazdan əvvəl API-dən şirkətin adını soruşur. Cavab «Test şirkəti» deyilsə, dayanır. Gözlənilən adı `E2E_EXPECT_COMPANY` ilə dəyişmək olar.

## Həssas məlumat trace-ə düşmür — mənbədə

- **API çağırışları.** Login, setup, teardown və testlərdəki rol API çağırışları adi `fetch` ilə gedir, Playwright trace-inə düşmür. Report-da addım kimi yalnız `API tabelci: PUT /api/manager/employees/…` görünür.
- **Brauzer sessiyası.** Brauzerin `localStorage`-ında imzasız «nümayiş token» olur: real token-in claim-ləri, e-poçt çıxarılmış halda. Server bu token-i qəbul etmir.
  - Səhifənin autentifikasiyalı API sorğusunu dəstin prosesi real token ilə real API-yə ötürür. Serverin cavabı səhifəyə dəyişmədən qaytarılır (status, başlıqlar, gövdə). Bu, saxta API deyil, şəffaf ötürmədir.
  - Trace sorğunu brauzerin göndərdiyi kimi yazır, yəni orada yalnız nümayiş token olur.
  - Header-i brauzerin öz sorğusunda dəyişmək işləmir: trace dəyişdirilmiş header-i yazır. Bu, yoxlanılıb.
- **PIN daxil edilməsi.** PIN-i ancaq `01-login.spec.ts` yazır. O, `evaluate()` ilə yazır, çünki `fill()` dəyəri report-un addım adına yazır. Bu fayl trace-siz işləyir.
- **Ekran görüntüləri.** Müvəqqəti PIN qutusu və sidebar-dakı login bütün screenshot-larda bulanıqlaşdırılır.
- **İkinci kilid.** `support/scrub-reporter.ts` run-ın sonunda bütün artefaktları yoxlayır. Tapdıqlarını silir:
  - JWT-lər;
  - credential sahələri;
  - admin-in telefonu və e-poçtu.

  Ikinci keçiddə nəsə qalsa, run uğursuz sayılır. Normal run-da JWT sayı **0** olmalıdır.

## Run-dan sonra nə bağlanır — və yoxlanır

`global-teardown.ts` bunları edir:
1. Testlərin əlavə etdiyi `E2E Yeni …` işçiləri silir.
2. Hər E2E hesabını ilkin roluna qaytarır.
3. Rol hesablarının PIN-ini sıfırlayır. Bu, run-ın bütün sessiyalarını bağlayır.
4. Token fayllarını silir.

Sonra bunları serverdə **yoxlayır**: işçi qalmayıb, rollar ilkin vəziyyətdədir, run-ın sessiyaları `401` alır, işlək PIN qalmayıb, fayllar silinib. Hər hansı yoxlama uğursuz olarsa, run uğursuz olur. Nəticə `test-results/e2e-teardown.json` faylına yazılır.

Admin-in öz sessiyası bağlanmır, çünki bu, admin-in PIN-ini dəyişmək deməkdir. Onun token-i heç bir artefakta düşmür.

## Staging-də nə yaradılır

Hamısı `test` şirkətindədir, adı `E2E` ilə başlayır və növbəti run-da adı ilə yenidən tapılır.

| Nə | Ad |
|---|---|
| Filiallar | E2E Filial A1, E2E Filial A2 (hər ikisi «E2E Struktur»da), E2E Filial B (strukturdan kənar) |
| Növbələr | E2E Növbə Səhər (08–17), E2E Növbə Axşam (14–23), hər ikisi A1-ə bağlı |
| Rol hesabları | E2E Operator (struktur operatoru, E2E Struktur), E2E Tabelçi (A1), E2E Müşahidəçi (E2E Struktur), E2E Menecer (köhnə qayda, B) |
| Hədəf işçilər | E2E İşçi A1 / A2 / B — heç vaxt daxil olmurlar |
| Testlərin əlavə etdiyi işçilər | `E2E Yeni …` — teardown silir və yoxlayır |

Login-lər `…@e2e.qrlog.invalid` ünvanlarıdır. `.invalid` rezerv olunmuş domendir.

Mövcud işçilərə, filiallara və rollara heç bir yazı edilmir.

## Testlər

| Fayl | Nə yoxlayır |
|---|---|
| `00-smoke` | sayt açılır, login səhifəsi görünür |
| `01-login` | 5 rolun hər biri login ekranından real giriş edir; yanlış PIN rədd olunur |
| `02-menu` | hər rolun menyusu dəqiq gözlənilən siyahıdır; menyuda olmayan səhifə URL ilə də açılmır; server sessiyaya rolun icazələrini verir |
| `03-scope` | struktur və filial sərhədləri UI və API-də; sahədən kənara yazı rədd olunur |
| `04-tabelci` | Tabelçi işçi əlavə edir, redaktə edir, növbəsini dəyişir (API və UI). Deaktiv/aktiv edə bilmir. Filial seçimləri yalnız öz sahəsindəndir |
| `05-musahideci` | Müşahidəçi heç nəyi dəyişə bilmir — UI-da düymə yoxdur, API 403 verir, data dəyişmir |
| `06-canli-deyisiklik` | rol və filial verilib-alınanda açıq sessiya yenidən girişsiz yenilənir |
| `07-admin` | admin bütün şirkəti görür; «Səlahiyyətlər» ekranı; Azərbaycan dili axtarışı (İ/i/I/ı) |

## Regression testləri

Bu 3 qüsuru dəst staging-də tapıb. Düzəlişlər `fix/e2e-findings` branch-indədir. Testlər staging həmin düzəlişlərlə deploy olunanda keçir:

1. **Tabelçi işçini deaktiv edə bilirdi.** İndi `employees.deactivate` icazəsi işçinin filialı üzrə yoxlanır; icazə olmayanda server 403 `DeactivateNotAllowed` qaytarır.
2. **Tabelçidə «İşçi əlavə et» deaktiv idi.** İndi filiallar `GET /api/manager/employee-branches`-dən gəlir: yalnız öz sahəsi, `locations.view` vermədən.
3. **Admin axtarışı böyük «İ» ilə ad tapmırdı.** İndi `SearchText.Fold` server, SQL və brauzerdə eyni qaydadır.
