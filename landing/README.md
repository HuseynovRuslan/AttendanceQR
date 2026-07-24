# QRLog — marketinq saytı

`qrlog.az`-da yayımlanan statik sayt. Astro 5, çoxdilli (AZ / RU / EN), SEO-birinci.

**Saytda işçi girişi yoxdur.** Heç bir səhifədə tətbiqə (`bax.qrlog.az`) link və ya “İşçi girişi”
düyməsi yoxdur — bütün CTA-lar `/elaqe/`-yə gedir. Sonradan geri qaytarmaq lazım olsa,
`src/data/site.ts`-ə `appUrl` sabitini əlavə edib düymələri ona bağlamaq kifayətdir.

## Yerli işə salma

```bash
npm install
npm run dev      # http://localhost:4321
npm run build    # statik sayt -> dist/
npm run preview  # build-i yoxla
```

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
  i18n/ui.ts                 # BÜTÜN tərcümələr (AZ/RU/EN) — mətn dəyişikliyi burada olur
  data/site.ts               # domen, əlaqə, qiymət, sahə siyahısı, rəylər — tərcümə olunmayan hər şey
  layouts/BaseLayout.astro   # SEO <head>, JSON-LD, Header + Footer, qlobal skriptlər
  components/                # hər bölmə ayrıca komponent
  content/blog/              # Markdown məqalələr
  pages/                     # index, qiymet, haqqimizda, elaqe, bloq/ (+ ru/ və en/ variantları)
public/                      # loqolar, og-image, favicon, robots.txt
```

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
- **Loqo / og şəkli:** `public/logo-word.png`, `public/logo-mark.png`, `public/og-image.png`.
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

`ops/deploy-prod.sh` bunu hər prod deploy-da özü çağırır. Ətraflı: `ops/README.md`.
