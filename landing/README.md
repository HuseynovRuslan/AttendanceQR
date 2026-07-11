# QRLog — marketinq saytı

QRLog davamiyyət sisteminin landing saytı. Astro + Tailwind CSS v4, SEO-birinci, böyüməyə hazır
(çoxsəhifə + bloq). `qrlog.az`-da Cloudflare Pages üzərində host olunur (tətbiq isə `bax.qrlog.az`).

## Yerli işə salma

```bash
npm install
npm run dev      # http://localhost:4321
npm run build    # statik sayt -> dist/
npm run preview  # build-i yoxla
```

## Struktur

```
src/
  layouts/BaseLayout.astro   # bütün SEO <head> (meta, OG, JSON-LD), Header + Footer
  data/site.ts               # məzmun (xüsusiyyətlər, FAQ, əlaqə) — tək mənbə
  components/                # Header, Footer, Hero, Features, HowItWorks, Audience, FAQ, CTA…
  pages/                     # index, qiymet, haqqimizda, elaqe, bloq/
  content/blog/              # Markdown məqalələr (content collection)
public/                      # logo.png, favicon.png, og.png, robots.txt
```

## Redaktə

- **Mətn / FAQ / xüsusiyyətlər:** `src/data/site.ts` — bir yerdən dəyiş, həm səhifə həm JSON-LD yenilənir.
- **Əlaqə (telefon, email):** `src/data/site.ts` içindəki `SITE`.
- **Loqo / og şəkli:** `public/logo.png`, `public/og.png` fayllarını əvəz et.
- **Yeni bloq yazısı:** `src/content/blog/` içinə `.md` fayl əlavə et (title, description, pubDate).

## Cloudflare Pages deploy

1. Bu qovluğu ayrıca GitHub repo-suna push et (məs. `qrlog-landing`).
2. Cloudflare → Workers & Pages → Create → Pages → GitHub repo-nu bağla.
3. Framework preset: **Astro** · Build: `npm run build` · Output: `dist`.
4. Deploy → sonra **Custom domains** → `qrlog.az` əlavə et.

SEO: `sitemap-index.xml` və `robots.txt` avtomatik yaranır; `site` `astro.config.mjs`-də təyin olunub.
