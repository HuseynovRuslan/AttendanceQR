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
  // TODO: real number before launch. Empty string hides every phone link on the site rather than
  // shipping a placeholder like "+994 12 000 00 00" that nobody answers.
  phone: '',
  // e.g. 'https://wa.me/994XXXXXXXXX' — empty hides the WhatsApp button.
  whatsapp: '',
  address: 'Bakı, Azərbaycan',
} as const

// ---------------------------------------------------------------------------------------------
// PRICING — PLACEHOLDER. These are the template's demo numbers, kept deliberately until the real
// plans are decided. Change `amount` (and the feature keys in src/i18n/ui.ts) here only; nothing
// else in the site hardcodes a price.
//
// `amount: null` renders the translated `price.pNa` string instead ("Pulsuz" / "Fərdi").
// ---------------------------------------------------------------------------------------------
export const PRICING = {
  // Set to false to drop the whole pricing section and its nav entry in one move.
  enabled: true,
  // `featureCount` is how many price.p<id>f<n> bullet keys exist for that plan in src/i18n/ui.ts.
  plans: [
    { id: 1, amount: null, featured: false, featureCount: 3 },
    { id: 2, amount: '₼19', featured: true, featureCount: 4 },
    { id: 3, amount: null, featured: false, featureCount: 3 },
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
