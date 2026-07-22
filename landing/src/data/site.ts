// Single source of truth for site-wide constants and the content that must stay in sync between the
// rendered page and the JSON-LD structured data (features, FAQ). Editing here updates both.
//
// NOTE: face/photo verification is intentionally NOT marketed here — the biometric-data legal review
// is still open. The anti-fraud story is told entirely through GPS geofencing + device binding, which
// are the product's real primary controls and carry no such exposure.

export const SITE = {
  name: 'QRLog',
  domain: 'qrlog.az',
  url: 'https://qrlog.az',
  appUrl: 'https://bax.qrlog.az',
  email: 'info@qrlog.az',
  // TODO: real number before launch — used in the header/contact CTAs.
  phone: '+994 __ ___ __ __',
  whatsapp: '', // e.g. https://wa.me/994XXXXXXXXX — leave empty to hide the WhatsApp button
  address: 'Bakı, Azərbaycan',
  description:
    'QRLog — işçi giriş-çıxışını telefonla QR skanı, GPS məkan yoxlaması və cihaz bağlaması ilə qeydə alan müasir davamiyyət sistemi. Bahalı avadanlıq yox, canlı hesabat, saxtakarlığa qarşı qorunma.',
} as const

// Value-framed, honest figures for the trust band — no fabricated adoption numbers.
export const STATS = [
  { value: '~10 san', label: 'Bir girişin qeydə alınması' },
  { value: '0 ₼', label: 'Avadanlıq xərci — telefon kifayətdir' },
  { value: 'Canlı', label: 'Kim işdədir — anlıq görünür' },
  { value: 'Limitsiz', label: 'Filial və işçi sayı' },
] as const

export const FEATURES = [
  {
    icon: 'qr',
    title: 'QR ilə giriş-çıxış',
    text: 'İşçi telefonu ilə iş yerindəki QR kodu skan edir — giriş və çıxış saniyələr içində yazılır. Turniket və ya barmaq izi cihazı almağa ehtiyac yoxdur.',
  },
  {
    icon: 'pin',
    title: 'GPS məkan yoxlaması',
    text: 'Skan yalnız iş yerinin ərazisində (təyin olunmuş radiusda) qəbul edilir. İşçi evdən və ya başqa yerdən “gəldim” deyə bilməz — məkan avtomatik yoxlanılır.',
  },
  {
    icon: 'shield',
    title: 'Cihaz bağlaması',
    text: 'Hər işçi öz telefonuna bağlanır. Tanınmayan cihazdan giriş nəzarət altındadır — bir işçi başqasının yerinə skan edə bilməz.',
  },
  {
    icon: 'chart',
    title: 'Canlı panel və hesabatlar',
    text: 'Kim işdədir, kim gəlməyib, kim gecikib — hamısı canlı. Tarix üzrə hesabatlar, Excel ixracı və problemli skanların ayrıca ekranı.',
  },
  {
    icon: 'users',
    title: 'Sürətli başlanğıc',
    text: 'İşçiləri Excel-dən toplu əlavə edin — hər kəsə müvəqqəti PIN yaranır, ilk girişdə özü təyin edir. Yüzlərlə işçi dəqiqələr içində sistemdə.',
  },
  {
    icon: 'branch',
    title: 'Çoxfilial idarəetmə',
    text: 'Bütün filialları bir paneldən idarə edin. Menecerlər yalnız öz filiallarını görür; hər filialın öz iş qrafiki və radiusu olur.',
  },
  {
    icon: 'phone',
    title: 'Quraşdırma yoxdur (PWA)',
    text: 'Sistem telefona tətbiq kimi əlavə olunur, amma App Store və ya Play Market lazım deyil — brauzerdən açılır və işləyir.',
  },
  {
    icon: 'lock',
    title: 'Sadə giriş',
    text: 'İşçi telefon nömrəsi və 4 rəqəmli PIN ilə daxil olur. Email tələb olunmur — hər kəs asanlıqla istifadə edir.',
  },
] as const

export const STEPS = [
  { n: 1, title: 'Filiala QR asın', text: 'Hər iş yeri üçün bir dəfə sabit QR poster çap edib divara asırsınız.' },
  { n: 2, title: 'İşçi telefonla skan edir', text: 'Gələndə və gedəndə işçi telefonunun kamerası ilə QR-ı skan edir.' },
  { n: 3, title: 'Sistem yoxlayır', text: 'GPS ilə məkan və işçinin cihazı avtomatik təsdiqlənir — saxta giriş keçmir.' },
  { n: 4, title: 'Rəhbərlik canlı görür', text: 'Giriş-çıxış, iş saatları və gecikmələr dərhal admin paneldə əks olunur.' },
]

export const AUDIENCE = [
  { icon: 'clean', title: 'Təmizlik & abadlıq', text: 'İşçiləri müxtəlif obyektlərdə olan xidmət şirkətləri.' },
  { icon: 'cafe', title: 'Kafe & restoran', text: 'Növbəli heyətin dəqiq giriş-çıxışı.' },
  { icon: 'shop', title: 'Mağaza şəbəkələri', text: 'Bir neçə filial üzrə satış heyəti.' },
  { icon: 'build', title: 'Tikinti', text: 'Obyektlərdə fəhlə davamiyyətinin izlənməsi.' },
  { icon: 'gov', title: 'İdarə & qurumlar', text: 'Çoxişçili müəssisələrdə dəqiq uçot.' },
  { icon: 'service', title: 'Xidmət sahələri', text: 'Klinika, logistika və digər heyət.' },
]

export const FAQ = [
  {
    q: 'QRLog nədir?',
    a: 'QRLog — QR koda əsaslanan işçi davamiyyəti (giriş-çıxış) sistemidir. İşçi telefonu ilə iş yerindəki QR kodu skan edir və sistem giriş/çıxışı avtomatik qeydə alır. GPS məkan yoxlaması və cihaz bağlaması ilə saxta girişin qarşısını alır.',
  },
  {
    q: 'Ayrıca cihaz almaq lazımdırmı?',
    a: 'Xeyr. İşçilər öz telefonlarından istifadə edir. Turniket, barmaq izi cihazı və ya xüsusi terminal almağa ehtiyac yoxdur — yalnız çap edilmiş QR poster kifayətdir.',
  },
  {
    q: 'İşçi evdən və ya başqa yerdən skan edə bilər?',
    a: 'Yox. Hər iş yerinin GPS koordinatı və radiusu təyin olunur; skan yalnız o ərazidə qəbul edilir. Əlavə olaraq hər işçi öz cihazına bağlıdır, ona görə başqasının yerinə giriş də mümkün deyil.',
  },
  {
    q: 'Telefona tətbiq yükləmək lazımdırmı?',
    a: 'Mağazadan yükləmək lazım deyil. QRLog PWA-dır — brauzerdən açılır və istəyə görə telefonun ana ekranına tətbiq kimi əlavə edilir.',
  },
  {
    q: 'Neçə filial və işçi dəstəkləyir?',
    a: 'Filial və işçi sayında limit yoxdur. Hər filial üçün ayrıca QR, məkan və iş qrafiki təyin edilir; rəhbərlik hamısını bir admin paneldən idarə edir, menecerlər isə yalnız öz filiallarını görür.',
  },
  {
    q: 'Başlamaq nə qədər çəkir?',
    a: 'Çox qısa. Filialı və işçiləri əlavə edir (işçiləri Excel-dən toplu da olar), QR posteri çap edib asırsınız — həmin gün işə düşür. Bizimlə əlaqə saxlayın, quraşdırmada kömək edək.',
  },
]
