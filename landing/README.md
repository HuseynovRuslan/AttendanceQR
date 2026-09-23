# QRLog — marketinq saytı

`qrlog.az`-da yayımlanan statik sayt. Astro 5, çoxdilli (AZ / RU / EN), SEO-birinci.

**Saytda işçi girişi yoxdur.** Heç bir səhifədə tətbiqə (`bax.qrlog.az`) link və ya “İşçi girişi”
düyməsi yoxdur — bütün CTA-lar `/elaqe/`-yə gedir. Sonradan geri qaytarmaq lazım olsa,
`src/data/site.ts`-ə `appUrl` sabitini əlavə edib düymələri ona bağlamaq kifayətdir.

## Yerli işə salma

```bash
npm install
npm run dev      # http://localhost:4321
npm run check    # tip yoxlaması (astro check) — .astro + .ts
npm run build    # statik sayt -> dist/
npm run preview  # build-i yoxla
```

`npm run build` **`check`-i çağırmır** (`ops/build-landing.sh` da) — statik saytın deploy-ını tip
xətasına bağlamaq istənilmədi. Ona görə dəyişiklikdən sonra `npm run check`-i özün işlət; hazırda
46 faylda 0 xəta, 0 xəbərdarlıq, 0 hint verir.

**`package.json`-dakı `overrides` nədir?** `@astrojs/language-server` 2.15.4-ə bağlanıb. Yeni
versiya (2.17) ESM-only `@astrojs/astro2tsx`-i `require()` edir, bu da Node **20.19+ / 22+** tələb
edir — Node 20.11-də `astro check` `ERR_REQUIRE_ESM` ilə düşür. Lokal Node-u Dockerfile-dakı kimi
22-yə qaldırandan sonra bu `overrides` blokunu silmək olar.

## Dillər

| Dil | URL |
|-----|-----|
| Azərbaycan (default) | `/` |
| Rus | `/ru/` |
| İngilis | `/en/` |

Səhifə slug-ları hər üç dildə azərbaycanca qalır (`/ru/qiymet/`, `/en/elaqe/`) — köhnə azərbaycanca
URL-lər artıq indeksdədir və onları dəyişmək qazanc gətirmirdi.

Bloq yalnız azərbaycancadır (`/bloq/`); hər dilin footer-i ora yönləndirir.

## Struktur

```
src/
  assets/images/             # build-in hash-layıb /_astro/-ya yazdığı ikili fayllar
    brand/                   #   logo-word, logo-mark-white
    customers/               #   müştəri loqoları (site.ts → CUSTOMERS)
    product/                 #   poster QR-ı, tətbiq ekranı
    problem/                 #   Problem.astro illüstrasiyaları
  components/                # hər bölmə ayrıca komponent
  content/blog/              # Markdown məqalələr
  data/site.ts               # domen, əlaqə, qiymət, rəylər — tərcümə olunmayan hər şey
  data/brand.ts              # header loqosunun tək derivative-i (preload və <img> eyni faylı istəyir)
  i18n/ui.ts                 # BÜTÜN tərcümələr (AZ/RU/EN) — mətn dəyişikliyi burada olur
  layouts/BaseLayout.astro   # SEO <head>, JSON-LD, Header + Footer, qlobal skriptlər
  pages/                     # index, qiymet, haqqimizda, elaqe, bloq/ (+ ru/ və en/ variantları)
  styles/tailwind.css        # Tailwind v4 + tema (aşağıda: «Stil»)
  styles/global.css          # saytın öz stilləri
  styles/global-layered.css  # global.css-i `base` layer-inə salır
public/                      # YALNIZ sabit adla çağırılan dörd fayl:
  robots.txt                 #   /robots.txt
  favicon.png                #   <link rel="icon">
  logo-mark.png              #   apple-touch-icon + JSON-LD Organization.logo
  og-image.png               #   og:image / twitter:image
```

`src/assets/` yalnız **ikili** fayllar üçündür — build onları hash-layıb `/_astro/`-ya yazır: indi
şəkillər, şrift lisenziyası gələndə `assets/fonts/`. Əl ilə yazdığın hər şey öz rol qovluğundadır
(`components/`, `styles/`, `data/`, `i18n/`, `pages/`). Astro-nun öz konvensiyası da budur.

**`src/assets/` yoxsa `public/`?** Qayda sadədir: **sayt özü çəkirsə → `src/assets/images/`;
kənar bir şey sabit URL ilə çağırırsa → `public/`.**

`src/assets/`-dəki fayl komponentə `import` olunur və `<Image />` (yaxud `getImage()`) ondan keçir —
Astro ölçüləndirir, webp-ə çevirir, adına hash yazır. Ona görə onu mütləq yolla (`/images/...`)
çağırmaq olmaz: build-də o ad qalmır. Əvəzində faylın adını səhv yazsan build düşür — saytda səssiz
404 qalmır. Qazanc real: `public/`-dən köçən səkkiz şəklin mənbəyi 967 KB idi, webp qarşılıqları
102 KB — ən kəskini `qrlog-app.png`, 470 KB → 44 KB.

`public/`-dəki fayl isə olduğu kimi kopyalanır, adı dəyişmir. Orada yalnız həmin dörd fayl var, çünki
onların URL-i bir öhdəlikdir — `og-image.png`-i sosial şəbəkələrin skraperi, `favicon.png` və
`logo-mark.png`-i brauzer, `logo-mark.png`-i həm də Google (`Organization.logo`) yadda saxlayır.
Onları yerindən tərpətmək paylaşılmış linklərin ön-baxışını və indeksdəki loqonu sındırır.

Yeni şəkil əlavə edəndə demək olar ki, həmişə `src/assets/images/`-in uyğun alt qovluğu.

**Bir tələ:** header-in loqosu həm `<head>`-də preload olunur, həm `Header.astro`-da, həm də hero
posterində çəkilir. `astro:assets` hər fərqli parametr dəsti üçün AYRI fayl yaradır, ona görə hər üçü
eyni derivative-i istəməlidir — yoxsa preload heç kimin çəkmədiyi ikinci şəkli endirir. Parametrlər
tək yerdədir: `src/data/brand.ts` → `LOGO_WORD`.

## Stil

Saytın öz stilləri `src/styles/global.css`-dədir. Yanında Tailwind v4 qoşulub, komponentlərdə
utility class-ları birbaşa yazmaq olar.

- **Utility həmişə `global.css`-dən üstündür.** `global.css` `base` cascade layer-indədir, utility-lər
  ondan yuxarıda: `global.css`-də `a { color: inherit }` olsa da, `<a class="text-blue-600">` mavi olur.
- **Preflight (Tailwind-in reset-i) qoşulmayıb** — `global.css`-in öz reset-i var. Ona görə başlıqlar
  qalın, siyahılar markerli qalır; lazım olanda `font-normal`, `list-none` yazın.
- Saytın rəngləri CSS dəyişənləridir: `text-(--ink)`, `bg-(--paper)`, `border-(--line)`.
  `font-sans` / `font-mono` — IBM Plex.
- `animate-spin`, `animate-ping`, `animate-pulse` işləyir, keyframe adları isə `tw-*`-dır:
  `global.css`-də eyni adlı (`spin`, `ping`, `pulse`) animasiyalar var.
- `global.css`-i `tailwind.css`-ə import etməyin — niyə ayrıca fayldan qoşulduğu
  `src/styles/global-layered.css`-də yazılıb.
- **`tailwind.css`-i yerindən tərpətsən `source()`-u da düzəlt.**
  `@import 'tailwindcss/utilities.css' … source('..')` yolu həmin faylın ÖZÜNƏ nisbətəndir və
  `src/`-i göstərməlidir. Səhv olsa build düşmür — utilities layer-i sadəcə boş çıxır və saytdakı
  bütün Tailwind class-ları səssizcə itir (ölçülüb: 125 659 → 74 879 bayt).
- `vite` `package.json`-a birbaşa yazılıb ki, Astro ilə Tailwind plugin-i eyni Vite-i işlətsin
  (yoxsa npm yanına ayrıca, daha yeni Vite qoyur). Astro-nu yeni Vite major versiyasına keçən
  buraxılışa yeniləyəndə `vite`-i də onunla bir yeniləyin.

## Nəyi harada dəyişmək

- **Mətn (istənilən dildə):** `src/i18n/ui.ts`. Açar hər üç dildə eynidir; tərcümə çatmasa
  azərbaycancaya qayıdır.
- **Qiymətlər:** `src/data/site.ts` → `PRICING`. **Hazırkı rəqəmlər şablon rəqəmləridir** — real
  planlar təyin olunanda `amount` sahəsini və `price.p*f*` açarlarını dəyişin. `enabled: false`
  bütün qiymət bölməsini və naviqasiyadakı yerini birdən söndürür.
- **Əlaqə (e-poçt, telefon, WhatsApp):** `src/data/site.ts` → `SITE`. `phone` boşdursa saytda heç
  bir telefon linki görünmür — işləməyən nömrə göstərməkdənsə heç nə göstərməmək seçildi. Bütün
  CTA düymələri (header, hero, qiymət planları, CTA zolağı) `/elaqe/` səhifəsinə yönəlir.
- **Müştəri rəyləri:** `src/data/site.ts` → `TESTIMONIALS`. Siyahı boş olduğu müddətdə bölmə
  ümumiyyətlə render olunmur. Ora yalnız **adı və vəzifəsi ilə paylaşılmasına icazə verilmiş** real
  sitatlar əlavə edin.
- **Loqo / og şəkli:** saytda görünən loqolar `src/assets/images/brand/`-dədir; `og-image.png`
  və `logo-mark.png` (apple-touch-icon + JSON-LD) `public/` kökündə qalır — URL-ləri sabit
  olmalıdır.
- **Yeni bloq yazısı:** `src/content/blog/` içinə `.md` (title, description, pubDate).

## Məzmun qaydası

Saytdakı hər iddia tətbiqin yerinə yetirməli olduğu vədidir. Ona görə burada **yoxdur**: uydurma
istifadə statistikası, uydurma reytinq (JSON-LD-də `aggregateRating` yoxdur), uydurma müştəri
loqoları və rəyləri, GDPR/“uçtan-uca şifrələmə” iddiaları, App Store / Google Play vədi (məhsul
PWA-dır) və “dinamik QR” (poster sabitdir — kod dəyişmir).

## Deploy

Sayt Cloudflare Pages-də deyil, **bizim öz serverimizdə** yayımlanır: Caddy `landing-dist/`
qovluğunu `/srv/qrlog` kimi mount edib `qrlog.az`-da verir.

```bash
bash ops/build-landing.sh    # docker-da build edir -> landing-dist/
```

**`ops/deploy-prod.sh` bunu çağırmır.** Sayt statikdir — compose onu yenidən qurmayan yeganə
hissədir, ona görə marketinq saytı dəyişəndə bu skripti ayrıca işlətmək lazımdır. Yoxsa `qrlog.az`
sonuncu dəfə kimsə onu işlədəndə yaranmış `landing-dist/`-i verməyə davam edir. Ətraflı:
`ops/README.md` → «build-landing.sh — on deploy, not on a timer».
