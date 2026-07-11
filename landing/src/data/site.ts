// Single source of truth for site-wide constants and the content that must stay in sync between the
// rendered page and the JSON-LD structured data (features, FAQ). Editing here updates both.

export const SITE = {
  name: 'QRLog',
  domain: 'qrlog.az',
  url: 'https://qrlog.az',
  appUrl: 'https://bax.qrlog.az',
  email: 'info@qrlog.az',
  phone: '+994 __ ___ __ __',
  address: 'Bakı, Azərbaycan',
  description:
    'QRLog — QR kod, GPS və foto təsdiqi ilə işçi giriş-çıxışını qeydə alan müasir davamiyyət sistemi. Telefonla skan, canlı hesabat, saxtakarlığa qarşı qorunma.',
} as const

export const FEATURES = [
  {
    icon: 'qr',
    title: 'QR kod ilə giriş-çıxış',
    text: 'İşçi telefonu ilə iş yerindəki QR kodu skan edir — giriş və çıxış saniyələr içində qeydə alınır. Ayrıca cihaz almağa ehtiyac yoxdur.',
  },
  {
    icon: 'pin',
    title: 'GPS / məkan yoxlaması',
    text: 'Skan yalnız iş yerinin ərazisində qəbul edilir. İşçi başqa yerdən giriş edə bilməz — məkan avtomatik yoxlanılır.',
  },
  {
    icon: 'camera',
    title: 'Foto təsdiqi',
    text: 'Girişdə şəkil çəkilir və referans ilə müqayisə edilir. Bu, bir işçinin başqasının yerinə skan etməsinin qarşısını alır.',
  },
  {
    icon: 'device',
    title: 'Cihaz bağlaması',
    text: 'Hər işçi öz telefonuna bağlıdır. Tanınmayan cihazdan giriş nəzarət altındadır — saxtakarlığa qarşı əlavə qat.',
  },
  {
    icon: 'chart',
    title: 'Admin panel və hesabatlar',
    text: 'Canlı davamiyyət, tarixçə, Excel hesabatları və çoxlu lokasiya dəstəyi. Rəhbərlik bir ekrandan hər şeyi görür.',
  },
  {
    icon: 'phone',
    title: 'PWA — telefon tətbiqi',
    text: 'Sistem telefona tətbiq kimi quraşdırılır. Ayrıca proqram yükləmək və ya mağazadan tapmaq lazım deyil.',
  },
] as const

export const STEPS = [
  { n: 1, title: 'QR-ı skan et', text: 'İşçi telefonunun kamerası ilə iş yerindəki QR kodu skan edir.' },
  { n: 2, title: 'GPS + foto təsdiqlənir', text: 'Sistem məkanı yoxlayır və giriş şəkli çəkilir.' },
  { n: 3, title: 'Davamiyyət qeydə alınır', text: 'Giriş və ya çıxış avtomatik yazılır, rəhbərlik dərhal görür.' },
]

export const AUDIENCE = [
  { icon: 'office', title: 'Ofislər', text: 'Ofis işçilərinin gündəlik giriş-çıxışı.' },
  { icon: 'gov', title: 'Dövlət müəssisələri', text: 'İdarə və qurumlarda dəqiq davamiyyət.' },
  { icon: 'shop', title: 'Mağazalar', text: 'Növbəli satış heyətinin qeydiyyatı.' },
  { icon: 'build', title: 'Tikinti', text: 'Obyektlərdə fəhlə davamiyyətinin izlənməsi.' },
  { icon: 'service', title: 'Xidmət sahələri', text: 'Restoran, klinika və digər xidmət heyəti.' },
]

export const FAQ = [
  {
    q: 'QRLog nədir?',
    a: 'QRLog — QR koda əsaslanan işçi davamiyyəti (giriş-çıxış) sistemidir. İşçi telefonu ilə iş yerindəki QR kodu skan edir və sistem giriş/çıxışı avtomatik qeydə alır. GPS, foto təsdiqi və cihaz bağlaması ilə saxtakarlığa qarşı qorunur.',
  },
  {
    q: 'QRLog necə işləyir?',
    a: 'İş yerində sabit bir QR kod (poster) asılır. İşçi gələndə telefonu ilə onu skan edir; sistem məkanı GPS ilə yoxlayır, giriş şəkli çəkir və giriş vaxtını yazır. Çıxışda eyni QR təkrar skan edilir.',
  },
  {
    q: 'Ayrıca cihaz almaq lazımdırmı?',
    a: 'Xeyr. İşçilər öz telefonlarından istifadə edir. Turniket, barmaq izi cihazı və ya xüsusi terminal almağa ehtiyac yoxdur — yalnız çap edilmiş QR poster kifayətdir.',
  },
  {
    q: 'Başqasının yerinə skan etməyin qarşısını necə alır?',
    a: 'Üç qat qorunma var: GPS ilə işçi iş yerində olmalıdır, girişdə foto çəkilir və referans şəkli ilə müqayisə edilir, hər işçi öz telefonuna (cihazına) bağlıdır. Bunlar birlikdə başqasının yerinə giriş etməyi çətinləşdirir.',
  },
  {
    q: 'Telefona tətbiq yükləmək lazımdırmı?',
    a: 'Mağazadan yükləmək lazım deyil. QRLog PWA-dır — brauzerdən açılır və istəyə görə telefonun ana ekranına tətbiq kimi əlavə edilir.',
  },
  {
    q: 'Neçə lokasiya dəstəkləyir?',
    a: 'QRLog çoxlu lokasiya dəstəkləyir. Hər filial və ya obyekt üçün ayrıca QR və məkan təyin edilir, rəhbərlik hamısını bir admin paneldən idarə edir.',
  },
]
