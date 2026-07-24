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
// `showPlans: false` — the current, deliberate state. The /qiymet/ page still exists and still
// ranks (people search "davamiyyət sistemi qiymət"), but it shows no numbers and no plan limits:
// just a short line saying the price is worked out per organisation, and a way to get in touch.
//
// The plan table below is the Astro template's invented pricing. Publishing it would have promised
// "Pulsuz, 50 işçiyə qədər" — and two of the three companies already paying for QRLog are under
// that line. A published price is very hard to walk back, so it stays off until the real plans
// exist. Set showPlans: true once they do, and correct `amount` + the price.p* keys in i18n/ui.ts.
//
// `amount: null` renders the translated `price.pNa` string instead.
// ---------------------------------------------------------------------------------------------
export const PRICING = {
  showPlans: false,
  // `featureCount` is how many price.p<id>f<n> bullet keys exist for that plan in src/i18n/ui.ts.
  plans: [
    { id: 1, amount: null, featured: false, featureCount: 3 },
    { id: 2, amount: '₼19', featured: true, featureCount: 4 },
    { id: 3, amount: null, featured: false, featureCount: 3 },
  ],
} as const

// ---------------------------------------------------------------------------------------------
// CUSTOMERS — the companies actually running QRLog, shown by name once each has agreed to it.
//
// `show: false` until that agreement exists. Naming a client on a public sales page is a reference,
// and a reference given without asking is the kind of thing that costs the account — particularly
// for an organisation like Bakı Abadlıq Xidməti. A written "yes" is not needed; a message from the
// person who signs is enough. Flip `show` to true and the strip appears above the sector list.
//
// Spelling is theirs, not ours: it is "EastCaf", not "EastCafe" — they have corrected this before.
// ---------------------------------------------------------------------------------------------
export const CUSTOMERS = {
  show: false,
  names: ['Bakı Abadlıq Xidməti', 'CleanFix', 'EastCaf'],
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
