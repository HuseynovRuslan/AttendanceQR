// Tərcümə olunmayan hər şeyin yeganə mənbəyi: domen, əlaqə məlumatları, qiymətlər və müştəri /
// sektor siyahıları. Tərcümə olunan mətnlər src/i18n/ui.ts-dədir.
//
// Proqramçı olmayan birinin dəyişmək istəyəcəyi hər şey qəsdən buradadır — bir düzəliş, bir build.
//
// Müştəri loqoları URL kimi yazılmır, IMPORT olunur: fayllar src/assets/-dadır və astro:assets-dən
// keçir, ona görə səhv yazılmış fayl adı saytı 404 ilə buraxmır, build-i dayandırır. CUSTOMERS-ə bax.
import bakiAbadliq from '../assets/images/customers/bakiabadliq.png'
import cleanFix from '../assets/images/customers/cleanfix.png'
import eastCaf from '../assets/images/customers/eastcaf.png'
import greenGarden from '../assets/images/customers/greengarden.png'

// `as const` (bu faylda dörd dəfə) TypeScript-ə deyir ki, bu dəyərlər sabitdir (readonly): kod
// təsadüfən SITE.phone-u dəyişməyə çalışsa, redaktor və `npm run check` xəta göstərir. Yalnız tip
// yoxlamasıdır — brauzerə gedən koda heç nə əlavə etmir.
// https://www.typescriptlang.org/docs/handbook/release-notes/typescript-3-4.html#const-assertions
export const SITE = {
  name: 'QRLog',
  domain: 'qrlog.az',
  url: 'https://qrlog.az',
  // appUrl qəsdən yoxdur: marketinq saytında işçilərin sistemə girişi üçün heç bir keçid yoxdur.
  // Bütün CTA düymələri /elaqe/-yə aparır; QRLog-u artıq işlədənlər tətbiqə (bax.qrlog.az və ya
  // şirkətlərinin öz subdomeni) birbaşa girirlər. Giriş düyməsini qaytarsan, bu sabiti də qaytar.
  email: 'info@qrlog.az',
  // Beynəlxalq formada yazılıb, çünki sayt rus və ingilis dillərində də açılır. tel: linki
  // (Footer, ContactBody) rəqəmlərdən və +-dan başqa hər şeyi silir, ona görə buradakı boşluqlar
  // yalnız oxumaq üçündür.
  phone: '+994 50 600 16 55',
  // `phone`-dan FƏRQLİ nömrədir, qəsdən: WhatsApp-da cavab verən budur (sahibkar təsdiqləyib,
  // 2026-09). `phone` kimi oxumaq üçün yazılıb; wa.me linkini aşağıdakı whatsappUrl() bunun
  // rəqəmlərindən qurur. /elaqe/-də, footer-də və üzən WhatsApp düyməsində görünür.
  whatsapp: '+994 51 240 97 67',
  address: 'Bakı, Azərbaycan',
  // Android tətbiqi (frontend/android, paket az.qrlog.app). iPhone üçün mağaza tətbiqi yoxdur —
  // orada brauzerdən PWA kimi işləyir. GooglePlayBadge.astro-ya bax.
  playStore: 'https://play.google.com/store/apps/details?id=az.qrlog.app',
} as const

/**
 * SITE.whatsapp nömrəsinə wa.me linki qurur. `text` verilsə, çat açılanda həmin mesaj artıq yazılmış
 * olur. Nümunə: whatsappUrl() → 'https://wa.me/994512409767'.
 */
export function whatsappUrl(text?: string): string {
  // wa.me nömrəni yalnız rəqəmlərlə qəbul edir — nə +, nə boşluq. /\D/g — "rəqəm olmayan hər simvol"
  // (g — hamısı), yəni '+994 51 240 97 67' → '994512409767'.
  const digitsOnly = SITE.whatsapp.replace(/\D/g, '')
  const chatUrl = `https://wa.me/${digitsOnly}`

  if (!text) {
    return chatUrl
  }
  // encodeURIComponent: mesajdakı boşluq, "ə", "ı" kimi simvollar URL-də kodlaşdırılmalıdır.
  return `${chatUrl}?text=${encodeURIComponent(text)}`
}

// ---------------------------------------------------------------------------------------------
// QİYMƏTLƏR (PRICING)
//
// REAL, dərc olunmuş qiymətlər — sahibkar 2026-08-08-də təyin edib (əvvəlki uydurma nümunə cədvəli
// bunlar gələnə qədər gizli qalırdı). Qiymət işçi başına və aylıqdır, paketi işçi sayı seçir. Hər
// paket bundan əlavə hər lokasiya (filial) üçün ayda 5 ₼ ödəyir — bu haqq i18n/ui.ts-dəki
// price.p*f2 bəndlərində və price.note sətrindədir; dəyişsə, ORADA dəyişdir.
//
//   Start       1–10 işçi     4 ₼ / işçi / ay
//   Biznes      11–50 işçi    3.5 ₼ / işçi / ay   (featured — seçilmiş kart)
//   Korporativ  51–100 işçi   3 ₼ / işçi / ay
//   Enterprise  101+ işçi     fərdi (amount: null → ui.ts-dəki tərcümə olunan price.p4a mətni)
//
// `enabled` ana səhifədəki bölməni və footer-dəki linki göstərir; `showPlans` isə plan cədvəlinin
// özünü (false = cədvəlin yerində price.quoteTitle kartı — "Qiymət fərdi hesablanır"). İkisi də
// true = qiymətlər açıqdır.
// DİQQƏT: tətbiqin daxili hesab sistemi (Domain/Pricing.cs) hələ köhnə pilləli tarifləri işlədir və
// lokasiya haqqı orada yoxdur — bu dərc olunmuş rəqəmlərlə kiməsə hesab kəsməzdən əvvəl onu
// uyğunlaşdır.
// ---------------------------------------------------------------------------------------------
export const PRICING = {
  enabled: true,
  showPlans: true,
  // `featureCount` — i18n/ui.ts-də həmin plan üçün neçə price.p<id>f<n> bənd açarı olduğu.
  // Pricing.astro f1-dən bu rəqəmə qədər açarları oxuyur: bənd əlavə etsən, bu rəqəmi də artır.
  plans: [
    { id: 1, amount: '4 ₼', featured: false, featureCount: 3 },
    { id: 2, amount: '3.5 ₼', featured: true, featureCount: 3 },
    { id: 3, amount: '3 ₼', featured: false, featureCount: 3 },
    { id: 4, amount: null, featured: false, featureCount: 3 },
  ],
} as const

// ---------------------------------------------------------------------------------------------
// MÜŞTƏRİLƏR (CUSTOMERS) — QRLog-u həqiqətən işlədən şirkətlər; hər biri razılıq verdikdən sonra
// adı ilə göstərilir.
//
// İlk üçü (Bakı Abadlıq Xidməti, CleanFix, EastCaf) QRLog ilə eyni qrupa aiddir və sahibkar
// 2026-07-24-də onların adının çəkilə biləcəyini təsdiqləyib. Meyar məhz bu təsdiqdir: satış
// səhifəsində müştərinin adını çəkmək ona istinad etməkdir, icazəsiz götürülmüş istinad isə təkcə
// nüfuza yox, müştərinin özünə başa gəlir — onu itirə bilərsən. Yeni ad əlavə edən hər kəs əvvəlcə
// eyni "hə"ni almalıdır.
//
// Sektorlar lentinin (marquee) ÜSTÜNDƏ yox, ƏVƏZİNƏ göstərilir — Trust.astro-ya bax.
//
// Yazılış onlarındır, bizim yox: "EastCafe" yox, "EastCaf" — bunu əvvəllər də düzəldiblər.
// ---------------------------------------------------------------------------------------------
// `logo` URL deyil, IMPORT-dur (faylın başında): fayl src/assets/images/customers/-dadır, Trust.astro
// onu <Image /> ilə çəkir — build zamanı ölçüsünü kiçildir və webp-yə çevirir.
// Bizə göndərilən iki loqo JPEG idi, hər biri öz qeyri-şəffaf kvadratında — CleanFix ağ üstündə göy,
// EastCaf qara üstündə tünd-göy dairə — və şəffaf PNG kimi kəsildi ki, səhifə hansı fonu versə,
// onun üstündə dursun. Yenisini də şəffaf saxla: sırada loqoların arxasında fonu gizlədəcək kart yoxdur.
//
// `logoH` ölçü deyil, NİSBİ çəkidir. Eyni hündürlük eyni GÖRÜNMƏZDİ: CleanFix hündürlüyündən 3.6 dəfə
// enli yazı-loqodur, eyni hündürlükdə dairəvi nişandan xeyli "ağır" görünür və sıranı basır.
// Trust.astro bu rəqəmləri ən böyüyünə bölür və sıranın yeganə real hündürlüyünü nəticəyə vurur —
// yalnız nisbətlər önəmlidir: enli loqo daha alçaq, kvadrat və ya dairəvi nişan daha hündür durur.
// Rəqəmlər düsturla yox, gözlə seçilib.
//
// `logo`su olmayan şirkət öz adı ilə, yazı kimi göstərilir; bunun üçün burada başqa heç nə lazım deyil.
export const CUSTOMERS = {
  show: true,
  items: [
    { key: 'c1', name: 'Bakı Abadlıq Xidməti', logo: bakiAbadliq, logoH: 70 },
    { key: 'c2', name: 'CleanFix', logo: cleanFix, logoH: 44 },
    { key: 'c3', name: 'EastCaf', logo: eastCaf, logoH: 74 },
    // Green Garden da EastCaf kimi dairəvi nişan olaraq gəldi, ona görə eyni hündür çəkini alır —
    // dairəvi nişan yazı-loqodan az yer tutur və onun hündürlüyündə kiçik görünür.
    { key: 'c4', name: 'Green Garden', logo: greenGarden, logoH: 72 },
  ],
} as const

// QRLog-un həqiqətən işləndiyi sahələr. Qəsdən müştəri loqoları DEYİL: müştərinin adını açıq çəkmək
// onun yazılı razılığını tələb edir, uydurma şirkət adları isə heç nədən pisdir.
export const TRUST_SECTORS = [
  'Təmizlik & abadlıq',
  'Kafe & restoran',
  'Mağaza şəbəkələri',
  'Tikinti',
  'İdarə & qurumlar',
  'Xidmət sahələri',
] as const

// Yalnız real müştəri sözləri, müəllifi göstərilməklə — o şəxsin adının çəkilməsinə icazəsi ilə.
// Siyahı boş olduqca bölmə heç nə göstərmir; belə sitatlar olana qədər düzgün vəziyyət də budur.
//
// Bir elementin nümunəsi:
//   { quote: '…', name: 'Ad Soyad', role: 'Vəzifə, şirkət', initial: 'A', color: '#1E63E9' }
export const TESTIMONIALS: {
  quote: string
  name: string
  role: string
  initial: string
  color: string
}[] = []
