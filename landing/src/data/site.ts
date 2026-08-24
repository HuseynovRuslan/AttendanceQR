// Single source of truth for everything that is *not* translated text: domains, contact details,
// prices and the social-proof lists. Translated strings live in src/i18n/ui.ts.
//
// Anything a non-developer is likely to want to change is here on purpose — one edit, one rebuild.

export const SITE = {
  name: 'QRLog',
  domain: 'qrlog.az',
  url: 'https://qrlog.az',
  // No appUrl on purpose: the marketing site has no staff-login entry point at all. Every CTA goes
  // to /elaqe/, and people who already use QRLog reach the app (bax.qrlog.az, or their own tenant
  // subdomain) directly. Adding a login button back means adding this constant back with it.
  email: 'info@qrlog.az',
  // Written in international form because the site is also served in Russian and English. The tel:
  // link strips everything but digits and the +, so the spacing here is purely for reading.
  phone: '+994 50 600 16 55',
  // e.g. 'https://wa.me/994506001655' — empty hides the WhatsApp button. Left empty until someone
  // has confirmed the number actually answers on WhatsApp: a dead button costs more than no button.
  whatsapp: '',
  address: 'Bakı, Azərbaycan',
} as const

// ---------------------------------------------------------------------------------------------
// PRICING
//
// The REAL published prices, set by the owner on 2026-08-08 (the earlier invented template table
// stayed hidden until these existed). Per-employee, monthly, headcount decides the package; every
// package also pays 5 ₼/month per location — that fee lives in the price.p*f2 bullets and the
// price.note line in i18n/ui.ts, so change it THERE if it moves.
//
//   Start       1–10 işçi     4 ₼ / işçi / ay
//   Biznes      11–50 işçi    3.5 ₼ / işçi / ay   (featured)
//   Korporativ  51–100 işçi   3 ₼ / işçi / ay
//   Enterprise  101+ işçi     fərdi (amount: null → the translated price.p4a string)
//
// `enabled` shows the homepage section + the footer link; `showPlans` shows the plan table itself
// (false = the "we quote per organisation" card). Both on = pricing is public.
// NOTE: the in-app billing engine (Domain/Pricing.cs) still uses the older graduated brackets and
// no location fee — align it before invoicing anyone on these published numbers.
// ---------------------------------------------------------------------------------------------
export const PRICING = {
  enabled: true,
  showPlans: true,
  // `featureCount` is how many price.p<id>f<n> bullet keys exist for that plan in src/i18n/ui.ts.
  plans: [
    { id: 1, amount: '4 ₼', featured: false, featureCount: 3 },
    { id: 2, amount: '3.5 ₼', featured: true, featureCount: 3 },
    { id: 3, amount: '3 ₼', featured: false, featureCount: 3 },
    { id: 4, amount: null, featured: false, featureCount: 3 },
  ],
} as const

// ---------------------------------------------------------------------------------------------
// CUSTOMERS — the companies actually running QRLog, shown by name once each has agreed to it.
//
// All three belong to the same group as QRLog itself, and the owner confirmed on 2026-07-24 that
// they may be named. That confirmation is the bar: naming a client on a public sales page is a
// reference, and a reference taken rather than given costs the account, not just the credibility.
// Anyone adding a fourth name here needs the same "yes" first.
//
// Shown INSTEAD OF the sector marquee, not above it — see Trust.astro.
//
// Spelling is theirs, not ours: it is "EastCaf", not "EastCafe" — they have corrected this before.
// ---------------------------------------------------------------------------------------------
// `logo` is a path under landing/public/. The two we have came in as JPEGs on their own opaque
// squares — CleanFix blue-on-white, EastCaf a navy roundel on black — so both were cut out to
// transparent PNGs (see landing/public/customers/). Each still carries its own background inside the
// artwork, which is why the card puts every logo on a WHITE tile rather than straight onto the dark
// band: EastCaf's navy roundel is within a few shades of the band itself and would simply vanish.
//
// `logoH` is the rendered height in px, set per logo rather than shared. Equal heights would NOT
// look equal: CleanFix is a wordmark 3.6× wider than it is tall, so at the same height it carries
// far more ink than a circular badge and dominates the row. These are balanced by eye, not by
// formula — a wide lockup sits lower, a square or round mark sits taller.
//
// `accent` tints the hover glow and any monogram fallback, sampled from each company's own artwork.
export const CUSTOMERS = {
  show: true,
  items: [
    { key: 'c1', name: 'Bakı Abadlıq Xidməti', mark: 'BA', accent: '#78C048',
      logo: '/customers/bakiabadliq.png', logoH: 70 },
    { key: 'c2', name: 'CleanFix', mark: 'CF', accent: '#3A9BDE',
      logo: '/customers/cleanfix.png', logoH: 44 },
    { key: 'c3', name: 'EastCaf', mark: 'EC', accent: '#D8C486',
      logo: '/customers/eastcaf.png', logoH: 74 },
    // Green Garden came in as a round badge like EastCaf's, so it takes the same taller height —
    // a circular mark carries less ink than a wordmark and looks small at a wordmark's height.
    { key: 'c4', name: 'Green Garden', mark: 'GG', accent: '#549C30',
      logo: '/customers/greengarden.png', logoH: 72 },
  ],
} as const

// Sectors QRLog is actually used in. Deliberately NOT customer logos: naming a client publicly
// needs their written consent, and invented company names are worse than none.
export const TRUST_SECTORS = [
  'Təmizlik & abadlıq',
  'Kafe & restoran',
  'Mağaza şəbəkələri',
  'Tikinti',
  'İdarə & qurumlar',
  'Xidmət sahələri',
] as const

// Real, attributed customer quotes only — with the person's permission to be named. The section
// renders nothing while this list is empty, which is the correct state until such quotes exist.
//
//   { quote: '…', name: 'Ad Soyad', role: 'Vəzifə, şirkət', initial: 'A', color: '#1E63E9' }
export const TESTIMONIALS: {
  quote: string
  name: string
  role: string
  initial: string
  color: string
}[] = []
