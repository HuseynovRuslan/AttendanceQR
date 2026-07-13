# Per-tenant logos

Drop a tenant's logo here as `<slug>.png` (or .svg/.webp) and point the tenant's
`LogoKey` column at the public path, e.g. `/brand/ecafe.png`.

Files in `public/` are copied to the site root at build time, so `/brand/<slug>.png`
is served directly by nginx. The frontend picks the logo up via `GET /api/tenant/branding`
(`logoUrl`) and `BrandLogo` renders it. No logo set → the tenant's initial in its accent
colour; no accent either → the default Bakı Abadlıq leaf (bax).

Prefer a square image (rendered in a circle, `object-fit: cover`), ideally ≥ 256×256.
