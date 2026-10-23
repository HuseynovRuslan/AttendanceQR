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
- **CI:** `staging-e2e` GitHub Environment-in secret-i, repository secret yox (aşağıdakı plana baxın);
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

## CI-də avtomatik işə salma — plan (hələ aktiv deyil)

Bu dəst **deploy olunmuş** staging-i yoxlayır, PR-ın öz kodunu yox. Staging-i `staging` branch-dən VM-dəki cron yığır (`ops/staging-autodeploy.sh`). Ona görə plan iki job-dan ibarətdir. Biri staging hesabları ilə işləyir, PR-larda isə heç vaxt işləmir. Digəri PR-ın kodunu runner-in içində qaldırılan stack-də yoxlayır və heç bir secret istifadə etmir.

Repo public-dir: Actions artefaktlarını GitHub-a daxil olan hər kəs yükləyə bilir. Bu, aşağıdakı artefakt qaydalarının səbəbidir.

### 1. Staging job-u — `push` → `staging` və əl ilə

- **Trigger.** Yalnız `push: branches: [staging]` və `workflow_dispatch`. `pull_request` yoxdur. Fork-dan gələn PR bu job-u heç bir yolla işə sala bilmir, deməli staging hesablarına və secret-lərə fork kodu çatmır.
- **Qadağandır:** `pull_request_target` və PR kodunu checkout edən `workflow_run`. İkisi də fork koduna secret verir.
- **Secret-lər environment-dədir.** `E2E_ADMIN_PHONE` / `E2E_ADMIN_PIN` repository secret kimi yox, `staging-e2e` adlı GitHub Environment-in secret-i kimi saxlanır. Environment ayarları:
  - *Deployment branches:* yalnız `staging`. Başqa branch-dən başladılan run secret almır.
  - *Required reviewers:* `staging`-ə sahibdən başqa kimsə push edə bilirsə, mütləqdir. Branch-ə push edə bilən hər kəs workflow faylını dəyişib secret-ə çata bilər.
- **CI üçün ayrıca admin.** Test şirkətində yalnız CI-in istifadə etdiyi ayrıca admin hesabı olsun. Onda insanın öz admin PIN-ini dəyişmək CI-ı sındırmır, CI-ın PIN-ini dəyişmək də insana toxunmur.
- **Fork-un öz kopyası.** `if: github.repository == 'HuseynovRuslan/AttendanceQR'` qoyulur. Fork-da job skip olunur və qırmızı olmur.
- **Bir run eyni anda.** `concurrency: e2e-staging`, `cancel-in-progress: false`. Run-lar eyni şirkətdə eyni E2E hesablarını işlədir. Yarıda kəsilən run teardown-u keçirər, ona görə ləğv olunmur, növbədə gözləyir.
- **Hansı kodu yoxladığı bəlli olmalıdır.** Push-dan sonra staging 1–3 dəqiqəyə yığılır, yığılmaya da bilər (cron qırıq commit-i elə saxlayır). Job staging həmin commit-i göstərənə qədər gözləyir, sonra işə düşür.
  - **Ön şərt (hələ yoxdur):** `scripts/version.mjs` `version.json`-a commit-i də yazmalıdır, `staging-autodeploy.sh` də onu build-ə ötürməlidir. İndi orada yalnız `buildId` (vaxt) var.
- **Artefakt yalnız uğursuz run-da yüklənir.** 3 gün saxlanır. Yükləmədən əvvəl ayrıca addım report-u və trace zip-lərini JWT və admin telefonu üçün yenidən skan edir. Bir şey taparsa, yükləmə olmur. Scrub reporter birinci kilid, bu skan ikincidir.
- **Secret-lər yoxdursa,** run `E2E konfiqurasiya xətası` ilə qırmızı bitir. Bu ancaq `staging` push-unda və əl ilə run-da ola bilər, PR-da yox.
- **Peak saatlar.** Staging production ilə eyni VM-dədir. Gecə planlı run (`schedule`) əlavə olunarsa, 07:30–09:30 və 17:00–19:00 (Bakı vaxtı) xaricində olsun.
- **PIN dəyişəndə** environment secret-i də yenilənir.

```yaml
# .github/workflows/e2e-staging.yml — PLAN; version.json-da commit olmayınca əlavə etməyin
name: E2E (staging)
on:
  push:
    branches: [staging]
  workflow_dispatch: {}
permissions:
  contents: read
concurrency:
  group: e2e-staging
  cancel-in-progress: false
jobs:
  e2e:
    if: github.repository == 'HuseynovRuslan/AttendanceQR'
    runs-on: ubuntu-latest
    environment: staging-e2e
    timeout-minutes: 30
    defaults:
      run:
        working-directory: frontend
    steps:
      - uses: actions/checkout@v4
        with:
          persist-credentials: false
      - uses: actions/setup-node@v4
        with:
          node-version: '22'
          cache: npm
          cache-dependency-path: frontend/package-lock.json
      - run: npm ci
      - run: npx playwright install --with-deps chromium
      - name: Staging bu commit-i göstərənə qədər gözlə
        if: github.event_name == 'push'
        run: |
          for i in $(seq 1 90); do
            [ "$(curl -fsS https://test.qrlog.az/version.json | jq -r '.commit // empty')" = "$GITHUB_SHA" ] && exit 0
            sleep 10
          done
          echo "staging 15 dəqiqədə $GITHUB_SHA commit-ini göstərmədi"; exit 1
      - name: E2E
        run: npm run e2e
        env:
          E2E_ADMIN_PHONE: ${{ secrets.E2E_ADMIN_PHONE }}
          E2E_ADMIN_PIN: ${{ secrets.E2E_ADMIN_PIN }}
      - name: Yükləmədən əvvəl müstəqil skan
        id: scan
        if: failure()
        env:
          PHONE: ${{ secrets.E2E_ADMIN_PHONE }}
        # grep -q yox, >/dev/null: Actions bash-ı pipefail ilə işlədir. grep -q tapan kimi çıxır, unzip SIGPIPE
        # alır və boru kəməri məhz tapılanda «uğursuz» olur — tapıntı itərdi. Boş PHONE hər şeyə uyğun gəlir:
        # skan uğursuz olur və yükləmə olmur.
        run: |
          jwt='eyJ[A-Za-z0-9_-]{10,}\.eyJ'
          find playwright-report test-results -name '*.zip' -print0 |
            while IFS= read -r -d '' z; do
              if unzip -p "$z" | grep -aE "$jwt" >/dev/null || unzip -p "$z" | grep -aF "$PHONE" >/dev/null; then
                echo "tapıldı: $z"; exit 1
              fi
            done
          if grep -rIE "$jwt" playwright-report test-results >/dev/null || grep -rIF "$PHONE" playwright-report test-results >/dev/null; then
            echo "tapıldı: zip-dən kənar fayl"; exit 1
          fi
      - uses: actions/upload-artifact@v4
        if: failure() && steps.scan.outcome == 'success'
        with:
          name: e2e-report
          path: |
            frontend/playwright-report
            frontend/test-results/e2e-teardown.json
          retention-days: 3
```

### 2. PR job-u — runner-in içində müvəqqəti stack, secret-siz

PR-ın öz kodunu yoxlamaq üçün staging yox, runner-də qaldırılan stack lazımdır: `docker compose up` (postgres + backend + frontend). Dəst `E2E_BASE_URL` / `E2E_API_URL` ilə `localhost`-a yönəlir; `localhost` icazəli host-dur.

- **Admin seed ilə yaranır.** Admini `TenantSeed__*` yaradır. Telefon və PIN job-un içində təsadüfi yaranır və yalnız həmin job-da yaşayır. Repository secret lazım deyil.
- **Fork PR-larında da təhlükəsizdir.** Secret yoxdur, staging-ə və production-a toxunmur, data runner ilə birlikdə silinir. Trigger `pull_request`, `permissions: contents: read`.
- **Ön şərtlər (hələ yoxdur):**
  - `TenantSeed__*` lokal `docker-compose.yml`-in `environment:` blokunda olmalıdır;
  - seed şirkəti Enforced rejimində yaratmalıdır. Setup bunu yoxlayır;
  - lokal stack-də anonim sorğular seed şirkətinə aid edilməlidir. İndi şirkət Origin-in subdomain-indən təyin olunur, `localhost`-da isə subdomain yoxdur;
  - foto, üz yoxlaması və push staging-dəki kimi söndürülməlidir;
  - `E2E_EXPECT_COMPANY` seed-in adına bərabər olmalıdır.
- **Required check.** Bu job hazır olanda PR-lar üçün required check ola bilər. Staging job-u ola bilməz, çünki PR-da işləmir.
