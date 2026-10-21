// Tərcümə olunmayan sabitlər: domen, əlaqə, qiymətlər, müştəri və sektor siyahıları.
// Tərcümə olunan mətnlər src/i18n/ui.ts-dədir.
// Müştəri loqoları URL yox, IMPORT olunur: səhv fayl adı build-i dayandırır (CUSTOMERS-ə bax).
import bakiAbadliq from '../assets/images/customers/bakiabadliq.png'
import cleanFix from '../assets/images/customers/cleanfix.png'
import eastCaf from '../assets/images/customers/eastcaf.png'
import greenGarden from '../assets/images/customers/greengarden.png'

export const SITE = {
  name: 'QRLog',
  domain: 'qrlog.az',
  url: 'https://qrlog.az',
  // appUrl qəsdən yoxdur: marketinq saytında giriş keçidi olmur, bütün CTA-lar /elaqe/-yə aparır.
  email: 'info@qrlog.az',
  // Beynəlxalq formada; tel: linki (Footer, ContactBody) rəqəm və +-dan başqa hər şeyi silir.
  phone: '+994 50 600 16 55',
  // phone ilə eyni nömrə, lakin ayrıca saxlanılır; wa.me linkini whatsappUrl() qurur.
  whatsapp: '+994 50 600 16 55',
  address: 'Bakı, Azərbaycan',
  // Android tətbiqi (GooglePlayBadge.astro). iPhone üçün mağaza tətbiqi yoxdur, brauzerdən PWA işləyir.
  playStore: 'https://play.google.com/store/apps/details?id=az.qrlog.app',
  // Footer.astro-dakı Instagram düyməsi; ad linkdən çıxarılmır, ona görə ayrıca saxlanılır.
  instagram: 'https://www.instagram.com/qrlog.az/',
  instagramHandle: '@qrlog.az',
} as const

/** SITE.whatsapp üçün wa.me linki; `text` verilsə, çat açılanda hazır mesaj kimi yazılır. */
export function whatsappUrl(text?: string): string {
  // wa.me nömrəni yalnız rəqəmlərlə qəbul edir (+ və boşluqsuz).
  const digitsOnly = SITE.whatsapp.replace(/\D/g, '')
  const chatUrl = `https://wa.me/${digitsOnly}`

  if (!text) {
    return chatUrl
  }
  return `${chatUrl}?text=${encodeURIComponent(text)}`
}

// Qiymət işçi başına və aylıqdır, paketi işçi sayı seçir. Hər paket əlavə olaraq hər lokasiyaya
// ayda 5 ₼ ödəyir (i18n/ui.ts-də price.p*f2 və price.note — dəyişsə, ORADA da dəyişdir).
//   Start       1–10 işçi     4 ₼ / işçi / ay
//   Biznes      11–50 işçi    3.5 ₼ / işçi / ay   (featured — seçilmiş kart)
//   Korporativ  51–100 işçi   3 ₼ / işçi / ay
//   Enterprise  101+ işçi     fərdi (amount: null → ui.ts-dəki price.p4a mətni)
// `enabled` ana səhifədəki bölməni və footer linkini, `showPlans` plan cədvəlini göstərir
// (false = cədvəlin yerində price.quoteTitle kartı).
// Enterprise-ın razılaşdırılmış qiyməti backend-də Tenant.MonthlyPriceOverride-dadır; o yoxdursa, kod 101+ üçün də 3 ₼ hesablayır.
// DİQQƏT: hesab sistemi src/AttendanceQR.Domain/Pricing.cs eyni rəqəmlərlə hesablayır. Burada birini
// dəyişsən, Pricing.cs və PricingTests.cs-i də dəyiş, yoxsa müştəriyə saytdakından fərqli hesab kəsilər.
export const PRICING = {
  enabled: true,
  showPlans: true,
  // `featureCount` — ui.ts-də həmin plan üçün neçə price.p<id>f<n> açarı olduğu; Pricing.astro bu qədərini oxuyur.
  plans: [
    { id: 1, amount: '4 ₼', featured: false, featureCount: 3 },
    { id: 2, amount: '3.5 ₼', featured: true, featureCount: 3 },
    { id: 3, amount: '3 ₼', featured: false, featureCount: 3 },
    { id: 4, amount: null, featured: false, featureCount: 3 },
  ],
} as const

// «Rəqəmlərlə» zolağı (Stats.astro). Rəqəmlər super admin panelindən götürülüb (2026-10-13), burada
// DƏQİQ yazılır; Stats.astro onları aşağı yuvarlaqlaşdırıb «+» ilə göstərir (1 069 → «1 000+»),
// yəni sayt real rəqəmdən çox göstərmir.
// Yeniləmək: rəqəmləri panelə görə dəyiş, `updatedAt`-i həmin günün tarixi et, saytı yenidən build et.
// `show: false` zolağı gizlədir; rəqəmlərdən biri null/0 olsa, Stats.astro heç nə göstərmir.
// Tip açıq yazılıb (`as const` yox), çünki sahələr `null` ola bilər.
export const STATS: {
  show: boolean
  companies: number | null
  employees: number | null
  locations: number | null
  dailyCheckins: number | null
  updatedAt: string
} = {
  show: true,
  companies: 5, // aktiv şirkətlər
  employees: 1069, // aktiv işçilər, bütün şirkətlər üzrə
  locations: 89, // filiallar
  dailyCheckins: 556, // bir iş günündə gələn işçilər (İcmal → «gəlib»)
  updatedAt: '2026-10-13', // rəqəmlərin götürüldüyü gün (YYYY-MM-DD)
}

// QRLog-u işlədən şirkətlər (Trust.astro). Adı yalnız şirkətin təsdiqi ilə çəkilir: ilk üçü qrupa
// aiddir və sahibkar 2026-07-24-də razılıq verib. Yeni ad əlavə etməzdən əvvəl eyni razılıq alınmalıdır.
// Yazılış şirkətlərin özünündür ("EastCaf", "EastCafe" yox).
// `logo` URL yox, import-dur (Trust.astro onu <Image /> ilə çəkir); şəffaf PNG olmalıdır, çünki loqonun
// arxasında fonu gizlədən kart yoxdur. Loqosu olmayan şirkət adı ilə yazı kimi göstərilir.
// `logoH` ölçü yox, NİSBİ çəkidir: Trust.astro rəqəmləri ən böyüyünə bölür, yalnız nisbətlər önəmlidir
// (enli yazı-loqo alçaq, dairəvi nişan hündür durur). Rəqəmlər gözlə seçilib.
export const CUSTOMERS = {
  show: true,
  items: [
    { key: 'c1', name: 'Bakı Abadlıq Xidməti', logo: bakiAbadliq, logoH: 70 },
    { key: 'c2', name: 'CleanFix', logo: cleanFix, logoH: 44 },
    { key: 'c3', name: 'EastCaf', logo: eastCaf, logoH: 74 },
    { key: 'c4', name: 'Green Garden', logo: greenGarden, logoH: 72 },
  ],
} as const

// QRLog-un işləndiyi sahələr; qəsdən müştəri adları deyil (ad çəkmək razılıq tələb edir).
export const TRUST_SECTORS = [
  'Təmizlik & abadlıq',
  'Kafe & restoran',
  'Mağaza şəbəkələri',
  'Tikinti',
  'İdarə & qurumlar',
  'Xidmət sahələri',
] as const

// Yalnız real müştəri sitatları, müəllifin icazəsi ilə. Siyahı boşdursa, bölmə heç nə göstərmir.
//   { quote: '…', name: 'Ad Soyad', role: 'Vəzifə, şirkət', initial: 'A', color: '#1E63E9' }
export const TESTIMONIALS: {
  quote: string
  name: string
  role: string
  initial: string
  color: string
}[] = []
