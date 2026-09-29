// Saytdakı bütün tərcümə olunan mətnlər. Üç dil, bir düz açar siyahısı — komponent yalnız
// t('some.key') çağırır, ona görə yeni dilin mətnləri üçün komponentlərə toxunmaq lazım deyil.
// Yeni dil üçün: bu faylda languages, localeMap, htmlLang, localePrefix-ə və ui-yə yeni blok əlavə
// et, astro.config.mjs-dəki iki locales siyahısına da yaz, səhifə fayllarını isə src/pages/<dil>/
// qovluğunda yarat (src/pages/ru/ kimi).
// Axın: ui.ts → useTranslations(lang) → komponentdə t('açar').
// Yeni açarı əvvəl az blokuna yaz (UIKey tipi oradan yaranır), sonra ru və en-ə. RU/EN-də
// unudulan açar səhifədə AZ mətni kimi görünür (aşağıda useTranslations-a bax).
//
// Mətnlər məhsulu olduğu kimi təsvir edir: hər filial üçün çap olunmuş, dəyişməyən QR poster; skan
// zamanı dörd yoxlama (QR imzası, GPS, bağlı cihaz, üz); Excel hesabatlı, azərbaycanca admin panel;
// Android-də Google Play tətbiqi, iPhone-da PWA.
// Belə də qalsın — bu səhifədəki hər iddia tətbiqin yerinə yetirməli olduğu bir vəddir.

// `as const` massivi dəyişməz edir, ona görə TypeScript dəyərləri ('az', 'ru', 'en') dəqiq bilir.
export const languages = ['az', 'ru', 'en'] as const
// Lang = 'az' | 'ru' | 'en' — yuxarıdakı massivdən avtomatik yaranır, ayrıca yazmağa ehtiyac yoxdur.
// [number] "massivin istənilən elementinin tipi" deməkdir:
// https://www.typescriptlang.org/docs/handbook/2/indexed-access-types.html
export type Lang = (typeof languages)[number]
export const defaultLang: Lang = 'az'

// Record<Lang, string> — hər dilə bir mətn. Aşağıdakı üç cədvəldə bir dili yazmağı unutsan,
// TypeScript dərhal xəta verir: https://www.typescriptlang.org/docs/handbook/utility-types.html#recordkeys-type
//
// og:locale meta teqi üçün (BaseLayout.astro). Bu teq alt xəttli formatı gözləyir: az_AZ.
export const localeMap: Record<Lang, string> = {
  az: 'az_AZ',
  ru: 'ru_RU',
  en: 'en_US',
}

// <html lang="..."> atributu üçün (BaseLayout.astro). Burada format defislidir: az-AZ —
// og:locale-dan fərqlidir, ona görə ayrıca cədvəldir.
export const htmlLang: Record<Lang, string> = {
  az: 'az-AZ',
  ru: 'ru-RU',
  en: 'en-US',
}

// Hər dilin URL prefiksi. AZ əsas dildir, ona görə prefiksi boşdur: /haqqimizda/ və /ru/haqqimizda/.
export const localePrefix: Record<Lang, string> = {
  az: '',
  ru: '/ru',
  en: '/en',
}

/**
 * «Rəhbərin bir günü» bölməsindəki məhsul ekranlarının içindəki mətn (AdminPanel*.astro).
 * Bu ekranlar QRLog admin panelinin özüdür, panel isə yalnız azərbaycancadır — ona görə bu mətnlər
 * üç dildə eynidir: burada bir dəfə yazılır və aşağıdakı hər dil blokuna `...panelUi` ilə köçürülür.
 * /ru/ və /en/ səhifələrində ekranlar lang="az" daşıyır (AdminPanel.astro-ya bax), panel.note isə
 * interfeysin azərbaycanca olduğunu sözlə deyir.
 * Bunları yalnız məhsulun özü tərcümə olunanda tərcümə et.
 *
 * Etiketlər məhsulun öz etiketləridir (frontend/src/pages/admin) — onunla uyğun saxla.
 * {n} {b} {t} {r} yer tutucularını fmt() doldurur: say, filial, saat, səbəb.
 */
const panelUi = {
  'panel.ui.total': 'Ümumi işçi',
  'panel.ui.in': 'İşdə',
  'panel.ui.absent': 'Qayıb',
  'panel.ui.done': 'Tamamlayıb',
  'panel.ui.rest': 'İstirahət',
  'panel.ui.dayOff': 'Həftəlik istirahət',
  'panel.ui.live': 'CANLI',
  // 08:15 — İdarəetmə paneli
  'panel.ui.m1.title': 'İdarəetmə paneli',
  'panel.ui.m1.sub': 'Bütün filiallar · canlı davamiyyət',
  'panel.ui.m1.now': 'İndi iş başında',
  'panel.ui.m1.people': 'nəfər',
  'panel.ui.m1.count': 'Cəmi {n} işçi · {b} filial',
  'panel.ui.m1.rate': 'bugünkü iştirak',
  'panel.ui.m1.feed': 'Son fəaliyyət',
  'panel.ui.m1.today': 'bu gün',
  'panel.ui.m1.checkIn': 'giriş',
  // 12:40 — Problemlər
  'panel.ui.m2.title': 'Problemlər',
  'panel.ui.m2.sub': 'Rədd edilmiş skanlar — kim, nə vaxt, niyə',
  'panel.ui.m2.failed': 'Problemli skan',
  'panel.ui.m2.ok': 'Uğurlu skan',
  'panel.ui.m2.alert': 'Bu filialda təkrarlanan problem var',
  'panel.ui.m2.alertTag': '{b} · {n} problem',
  'panel.ui.m2.alertText':
    'Bir filialda təkrarlanan «{r}» adətən geofence radiusunun və ya poster yerinin problemidir — işçinin deyil.',
  'panel.ui.m2.list': 'Bu günkü problemlər',
  'panel.ui.m2.last': 'son {t}',
  'panel.ui.m2.times': '{n} dəfə',
  'panel.ui.m2.outside': 'İş yerindən kənarda',
  'panel.ui.m2.noLocation': 'Məkan icazəsi verilməyib',
  // 18:30 — Davamiyyət
  'panel.ui.m3.title': 'Davamiyyət',
  'panel.ui.m3.sub': 'Bugün · canlı',
  'panel.ui.m3.export': 'Excel-ə çıxar',
  'panel.ui.m3.all': 'Bütün işçilər',
  'panel.ui.m3.faceMismatch': 'Üzü uyğun gəlməyənlər',
  'panel.ui.m3.noPhoto': 'Şəkilsizlər',
  'panel.ui.m3.finished': 'Tamamlandı',
  'panel.ui.m3.thName': 'İşçi',
  'panel.ui.m3.thRole': 'Vəzifə',
  'panel.ui.m3.thStatus': 'Status',
  'panel.ui.m3.thIn': 'Giriş',
  'panel.ui.m3.thOut': 'Çıxış',
  'panel.ui.m3.thFace': 'Üz',
} as const

export const ui = {
  az: {
    'meta.title': 'QRLog — QR ilə işçi davamiyyəti sistemi | Azərbaycan',
    'meta.description':
      'QRLog — işçilərin telefonu ilə QR kodu skan edərək giriş-çıxışını qeydə alan davamiyyət sistemi. Hər giriş imzalı QR, GPS məkanı, cihaz və üz yoxlaması ilə təsdiqlənir.',
    'meta.keywords':
      'işçi davamiyyəti, QR davamiyyət, giriş çıxış sistemi, davamiyyət proqramı, GPS davamiyyət, iş vaxtı uçotu, QRLog, Azərbaycan',

    'nav.how': 'Necə işləyir',
    'nav.features': 'İmkanlar',
    'nav.pricing': 'Qiymət',
    'nav.faq': 'Suallar',
    'nav.contact': 'Əlaqə',
    'nav.blog': 'Bloq',
    'nav.about': 'Haqqımızda',
    'nav.menu': 'Menyu',

    'hero.badge': 'Ayrıca cihaz yoxdur — telefonla işləyir',
    'hero.title.a': 'İşçi davamiyyətini bir ',
    'hero.title.hl': 'skan',
    'hero.title.b': ' ilə idarə edin',
    'hero.sub':
      'İşçi iş yerindəki QR posteri telefonu ilə skan edir; sistem QR imzasını, məkanı, cihazı və üzü yoxlayıb giriş-çıxışı qeydə alır. Turniket, barmaq izi cihazı və ya kağız jurnal lazım deyil.',
    'hero.cta2': 'Necə işləyir →',
    'hero.s1n': '~10 san',
    'hero.s1l': 'bir girişin qeydə alınması',
    'hero.s2n': '0 ₼',
    'hero.s2l': 'əlavə avadanlıq xərci',
    'hero.s3n': 'Canlı',
    'hero.s3l': 'kim işdədir — anlıq',
    'hero.assure.a': 'Ayrıca cihaz almadan',
    'hero.assure.b': 'Həmin gün işə düşür',
    'hero.assure.c': 'Telefondan işləyir',
    // Birinci ekrandakı nümunə səhnə: divarda poster, telefonda tətbiq və onların üstündə üzən dörd
    // kart. Məlumat nümunədir və etiket bunu açıq deyir — rəqəmlər heç bir müştərinin deyil.
    'hero.d.label': 'Nümunə məlumat',
    'hero.d.site': 'Baş ofis',
    'hero.d.poster': 'Giriş və çıxış üçün skan edin',
    'hero.d.posterNote': 'Telefonun kamerasını koda tutun. Giriş təxminən 10 saniyəyə qeydə alınır.',
    'hero.d.scanned': 'Skan olundu',
    'hero.d.offlineT': 'İnternet yoxdursa da',
    'hero.d.offlineD': 'Qeyd telefonda saxlanılır, bağlantı gələndə göndərilir',
    'hero.d.recorded': 'Giriş qeydə alındı',
    'hero.d.c1': 'QR imzası',
    'hero.d.c2': 'Cihaz',
    'hero.d.c3': 'Məkan (GPS)',
    'hero.d.c4': 'Üz uyğunluğu',
    'hero.d.liveT': 'Canlı status',
    'hero.d.liveOn': 'Aktiv',
    'hero.d.liveD': '184 nəfər hazırda işdədir',
    'hero.d.shotAlt': 'QRLog tətbiqinin ekranı: işçinin növbəsi və çıxış düyməsi',

    'prob.eyebrow': 'Problem',
    'prob.title.a': 'Köhnə üsulla davamiyyət vaxt aparır və ',
    'prob.title.hl': 'saxtaya',
    'prob.title.b': ' açıqdır',
    'prob.sub':
      'Kağız jurnal, bahalı terminal və “söz”lə uçot — hər filialda ayrıca dərd, hər ay yenidən.',
    'prob.p1t': 'Başqasının yerinə giriş',
    'prob.p1d':
      'Bir nəfər bütün briqadanı “gəldi” yaza bilir. Kim həqiqətən gəldi — bilinmir, maaş isə bu qeydə bağlıdır.',
    'prob.p2t': 'Bahalı avadanlıq, hər filialda',
    'prob.p2d':
      'Turniket və barmaq izi terminalı hər obyekt üçün ayrıca alınır, quraşdırılır və sıradan çıxır.',
    'prob.p3t': 'Əl ilə uçot, gec görünüş',
    'prob.p3d':
      'Kağız jurnal və Excel tabel saatlar aparır; kim işdə, kim yox — yalnız günün sonunda bilinir.',
    'prob.m1t': 'Zaman itkisi',
    'prob.m1d': 'gündə saatlarla',
    'prob.m2t': 'Əlavə xərc',
    'prob.m2d': 'avadanlıq, servis',
    'prob.m3t': 'Zəif nəzarət',
    'prob.m3d': 'saxtaya açıq',
    'prob.next': 'Problem aydındır. Bəs həlli?',
    'scan.live': 'CANLI',
    'scan.feed': 'Son qeydiyyatlar',
    'scan.status': 'Qeyd olundu',
    'scan.demo': 'Nümunə ekran',

    'trust.title': 'QRLog bu sahələrdə istifadə olunur',

    'demo.eyebrow': 'Skan anı',
    'demo.title': 'Skandan qeydiyyata qədər',
    'demo.sub':
      'Telefon QR-ı oxuyur, cihaz və məkan yoxlanılır, şəkil referansla müqayisə edilir — və giriş yazılır. Kamera və ya şəkil uğursuz olsa belə giriş bloklanmır: sistem onu işarələyir, amma işçinin qeydiyyatını dayandırmır.',
    'demo.scanning': 'QR oxunur…',
    'demo.detected': 'QR tapıldı',
    'demo.choose': 'Giriş növünü seçin',
    'demo.checkin': 'Giriş',
    'demo.checkout': 'Çıxış',
    'demo.done': 'Hazır!',
    'demo.recorded': 'Davamiyyət qeydə alındı',
    'demo.step1': 'Skan et',
    'demo.step2': 'Cihaz və məkan',
    'demo.step3': 'Foto təsdiqi',
    'demo.step4': 'Hazır',
    'demo.verify.title': 'Cihaz və məkan yoxlanılır',
    'demo.verify.device': 'Cihaz tanındı',
    'demo.verify.location': 'İş yeri ərazisindədir',
    'demo.face.title': 'Şəkil təsdiqlənir',
    'demo.face.hint': 'Telefon kamerasına baxın',

    'stats.title': 'Rəqəmlərlə',
    'stats.sub': 'Bahalı avadanlıq yox, mürəkkəb quraşdırma yox.',
    'stats.s1': 'saniyəyə bir giriş qeydə alınır',
    'stats.s2': 'manat avadanlıq xərci — telefon kifayətdir',
    'stats.s3': 'qat yoxlama: QR imzası, məkan, cihaz, üz',
    'stats.s4': 'filial və işçi sayı',
    'stats.s4v': 'Limitsiz',

    'how.eyebrow': 'İş prinsipi',
    'how.title': 'Üç addımda hazırdır',
    'how.sub': 'Bir dəfə qurulur — sonra hər gün özü işləyir.',
    'how.s1t': 'QR posteri asın',
    'how.s1d':
      'Hər filial üçün bir dəfə sabit QR poster çap edib divara asırsınız. Kod dəyişmir — posteri təzələmək lazım gəlmir.',
    'how.s2t': 'İşçi telefonla skan edir',
    'how.s2d':
      'Gələndə və gedəndə işçi öz telefonu ilə QR-ı skan edir. QR imzası, məkan, cihaz və üz eyni anda yoxlanılır.',
    'how.s3t': 'Rəhbərlik canlı görür',
    'how.s3d':
      'Giriş-çıxış dərhal admin panelə düşür. Tarix üzrə filtrləyin, Excel hesabatı yükləyin, filial üzrə bölün.',
    // Addımların yanındakı metrik çipləri. Bunlar Hero bölməsindəki eyni üç iddiadır
    // (hero.s2n, hero.s1n, hero.s3l) — birini dəyişsən, o birini də dəyiş.
    'how.s1m': '0 ₼ avadanlıq',
    'how.s2m': '~10 saniyə',
    'how.s3m': 'anlıq',
    // 02-ci səhnədə skan anında eyni anda yoxlanan dörd şey — Hero-dakı hero.d.c1–c4 ilə eyni
    // yoxlamalar, eyni terminlə ("Üz", "Şəkil" yox). Birini dəyişsən, o birini də dəyiş.
    'how.c1': 'QR imzası',
    'how.c2': 'Məkan',
    'how.c3': 'Cihaz',
    'how.c4': 'Üz',
    // Üç addımın tablist-inin adı — ekran oxuyucusu üçün, gözlə görünmür.
    'how.tabs': 'Necə işləyir',

    'feat.eyebrow': 'İmkanlar',
    'feat.title': 'Davamiyyət üçün lazım olan hər şey',
    'feat.sub': 'Gündəlik uçotdan aylıq hesabata qədər bir paneldə.',
    'feat.f1t': 'Canlı davamiyyət lövhəsi',
    'feat.f1d': 'Kim gəlib, kim çıxıb, kim yoxdur — anlıq görünür, əl ilə heç nə sayılmır.',
    'feat.f2t': 'Excel hesabatları',
    'feat.f2d': 'Tarix və filial üzrə hesabatı bir kliklə Excel faylı kimi yükləyin.',
    'feat.f3t': 'GPS məkan yoxlaması',
    'feat.f3d': 'Hər skanın yeri filialın təyin olunmuş radiusu ilə yoxlanılır; kənardan gələn giriş dərhal işarələnir.',
    'feat.f4t': 'Cihaz bağlaması',
    'feat.f4d': 'Hər işçi öz telefonuna bağlanır; tanınmayan cihazdan giriş nəzarət altındadır.',
    'feat.f5t': 'Rollar və filial əhatəsi',
    'feat.f5d': 'İşçi, menecer, admin. Menecer yalnız öz filiallarını görür — artıq bir sətir də yox.',
    'feat.f6t': 'Android və iPhone',
    'feat.f6d': 'Android-də Google Play-dən yüklənir. iPhone-da brauzerdən açılır və ana ekrana tətbiq kimi əlavə olunur.',

    // «Rəhbərin bir günü» (AdminPanel.astro). Tabların saatları — 08:15, 12:40, 18:30 — dil mətni
    // deyil, komponentdə data kimi durur. Panel ekranlarının içindəki mətn panelUi-dədir (yuxarıda).
    'panel.eyebrow': 'Admin panel',
    'panel.title': 'Rəhbərin bir günü',
    'panel.sub':
      'Səhər kim gəlib, günorta nə diqqət istəyir, axşam gün necə bağlanıb. QRLog panelində hər biri bir baxışdır.',
    'panel.m1.label': 'Kim gəlib',
    'panel.m1.head': 'Qapıdan keçən hər kəs artıq burdadır.',
    'panel.m1.text':
      'Zəng etməyə, jurnal açmağa ehtiyac yoxdur — panel özü sayır və filiallar üzrə bölür.',
    'panel.m1.meta1': 'Canlı yenilənir',
    'panel.m1.meta2': 'Bütün filiallar bir yerdə',
    'panel.m2.label': 'Nə diqqət istəyir',
    'panel.m2.head': 'Problem gələndə səbəbi də gəlir.',
    'panel.m2.text':
      'Bir filialda təkrarlanırsa, sistem onu işçiyə yox, yerə bağlayır — geofence radiusu, ya da posterin yeri.',
    'panel.m2.meta1': 'Rədd edilən skan · səbəbi ilə',
    'panel.m2.meta2': 'Filial üzrə xəbərdarlıq',
    'panel.m3.label': 'Gün bağlandı',
    'panel.m3.head': 'Gün bir cədvəldə bağlanır.',
    'panel.m3.text':
      'Kim neçədə gəlib-gedib, kimin üzü uyğun gəlməyib — hamısı bir yerdə. Mühasibə göndərmək üçün bir klik.',
    'panel.m3.meta1': 'Excel-ə çıxarış',
    'panel.m3.meta2': 'İstənilən gün üçün',
    // Ekran oxuyucusu üçün — gözlə görünmür.
    'panel.tablist': 'Günün anları',
    'panel.prev': 'Əvvəlki an',
    'panel.next': 'Növbəti an',
    'panel.carousel': 'karusel',
    'panel.slide': 'slayd',
    'panel.note': 'Ekranlar QRLog panelindəndir · məlumat nümunədir',
    ...panelUi,

    'aud.eyebrow': 'Kimlər üçün',
    'aud.title': 'İşçisi olan hər təşkilat üçün',
    'aud.sub': 'Xüsusilə heyəti bir neçə obyektə səpələnmiş şirkətlər üçün.',
    'aud.a1t': 'Təmizlik & abadlıq',
    'aud.a1d': 'İşçiləri müxtəlif obyektlərdə olan xidmət şirkətləri.',
    'aud.a2t': 'Kafe & restoran',
    'aud.a2d': 'Növbəli heyətin dəqiq giriş-çıxışı.',
    'aud.a3t': 'Mağaza şəbəkələri',
    'aud.a3d': 'Bir neçə filial üzrə satış heyəti.',
    'aud.a4t': 'Tikinti',
    'aud.a4d': 'Obyektlərdə fəhlə davamiyyətinin izlənməsi.',
    'aud.a5t': 'İdarə & qurumlar',
    'aud.a5d': 'Çoxişçili müəssisələrdə dəqiq uçot.',
    'aud.a6t': 'Xidmət sahələri',
    'aud.a6d': 'Klinika, logistika və digər heyət.',

    'sec.eyebrow': 'Təhlükəsizlik',
    'sec.title': 'Məlumatlarınız qorunur',
    'sec.sub': 'Hər şirkətin məlumatı ayrıdır və yalnız görməli olan görür.',
    'sec.i1t': 'Şifrələnmiş bağlantı',
    'sec.i1d': 'Bütün trafik HTTPS üzərindən gedir; sertifikatlar avtomatik yenilənir.',
    'sec.i2t': 'Şirkətlər arasında izolyasiya',
    'sec.i2d':
      'Sorğunun hansı şirkətə aid olduğu müəyyən edilmirsə, sorğu rədd edilir — “standart” şirkət yoxdur.',
    'sec.i3t': 'Gündəlik ehtiyat nüsxə',
    'sec.i3d': 'Baza hər gün avtomatik yedəklənir və bərpa mütəmadi yoxlanılır.',
    'sec.i4t': 'Rol əsaslı giriş',
    'sec.i4d': 'İşçi, menecer və admin fərqli şey görür; menecerin əhatəsi öz filialı ilə məhdudur.',

    'mod.eyebrow': 'Modullar',
    'mod.title': 'Davamiyyətdən sonrası da var',
    'mod.sub': 'Hamısı eyni paneldə, əlavə proqram olmadan.',
    'mod.m1': 'Davamiyyət lövhəsi',
    'mod.m2': 'Excel hesabatı',
    'mod.m3': 'Maaş hesablaması',
    'mod.m4': 'Məzuniyyət & icazə',
    'mod.m5': 'Növbə qrafiki',
    'mod.m6': 'Elanlar',
    'mod.m7': 'Tapşırıqlar',
    'mod.m8': 'Push bildirişləri',
    'mod.m9': 'Kiosk rejimi',
    'mod.m10': 'Çoxfilial idarəetmə',

    'test.eyebrow': 'Rəylər',
    'test.title': 'Müştərilər nə deyir',
    'test.sub': 'Adı və vəzifəsi ilə paylaşılmasına icazə verilmiş rəylər.',

    'price.eyebrow': 'Qiymət',
    'price.title': 'Sadə və şəffaf qiymət',
    'price.sub': 'İşçi sayına görə ödəyin — bütün funksiyalar hər paketdə daxildir.',
    'price.subQuote': 'Ehtiyacınıza uyğun təklif hazırlayırıq. Gizli ödəniş yoxdur.',
    'price.popular': 'POPULYAR',
    'price.mo': ' / işçi / ay',
    'price.note': 'Bütün qiymətlər aylıqdır. Hər lokasiya (filial) üçün əlavə 5 ₼/ay tutulur. Gizli ödəniş yoxdur.',
    // price.p1a–p4a yalnız src/data/site.ts-dəki PRICING həmin planın amount-unu null edəndə
    // görünür (indi yalnız Enterprise-da — p4a). Qalan planlarda məbləğ site.ts-dən gəlir.
    'price.p1n': 'Start',
    'price.p1d': 'Kiçik komandalar üçün.',
    'price.p1a': '4 ₼',
    'price.p1f1': '1–10 işçi',
    'price.p1f2': 'Hər lokasiya: 5 ₼/ay',
    'price.p1f3': 'Bütün funksiyalar daxil',
    'price.p1c': 'Əlaqə saxlayın',
    'price.p2n': 'Biznes',
    'price.p2d': 'Böyüyən şirkətlər üçün.',
    'price.p2a': '3.5 ₼',
    'price.p2f1': '11–50 işçi',
    'price.p2f2': 'Hər lokasiya: 5 ₼/ay',
    'price.p2f3': 'Bütün funksiyalar daxil',
    'price.p2c': 'Əlaqə saxlayın',
    'price.p3n': 'Korporativ',
    'price.p3d': 'Böyük komandalar üçün.',
    'price.p3a': '3 ₼',
    'price.p3f1': '51–100 işçi',
    'price.p3f2': 'Hər lokasiya: 5 ₼/ay',
    'price.p3f3': 'Bütün funksiyalar daxil',
    'price.p3c': 'Əlaqə saxlayın',
    'price.p4n': 'Enterprise',
    'price.p4d': 'Böyük təşkilatlar üçün fərdi həll.',
    'price.p4a': 'Fərdi',
    'price.p4f1': '101+ işçi',
    'price.p4f2': 'Fərdi qiymət və şərtlər',
    'price.p4f3': 'Quraşdırma və keçid dəstəyi',
    'price.p4c': 'Əlaqə saxlayın',
    'price.quoteTitle': 'Qiymət fərdi hesablanır',
    'price.quoteText':
      'Məbləğ işçi sayına, filial sayına və ehtiyac duyduğunuz imkanlara görə dəyişir. Bir neçə sual verib dəqiq təklif göndəririk — gizli ödəniş yoxdur.',
    'price.quoteBtn': 'Təklif alın',
    'cust.title': 'QRLog-dan istifadə edən şirkətlər',
    'cust.eyebrow': 'ETİBARLI TƏRƏFDAŞLAR',
    'cust.c1s': 'Abadlıq və şəhər təsərrüfatı',
    'cust.c2s': 'Peşəkar təmizlik xidmətləri',
    'cust.c3s': 'Kafe və restoran',
    'cust.c4s': 'Landşaft və istirahət məkanı',

    'faq.eyebrow': 'Suallar',
    'faq.title': 'Tez-tez verilən suallar',
    'faq.sub': 'Cavabını tapmadınız? Bizə yazın — kömək edək.',
    'faq.q1': 'Ayrıca cihaz almaq lazımdırmı?',
    'faq.a1':
      'Xeyr. İşçilər öz telefonlarından istifadə edir. Turniket, barmaq izi cihazı və ya terminal almağa ehtiyac yoxdur — divara asılmış çap olunmuş QR poster kifayətdir.',
    'faq.q2': 'İşçi evdən və ya başqasının yerinə skan edə bilər?',
    'faq.a2':
      'Hər filialın GPS koordinatı və radiusu təyin olunur; skan yalnız o ərazidə qəbul edilir. Bundan əlavə hər işçi öz cihazına bağlıdır və girişdə şəkil çəkilib referansla müqayisə edilir.',
    'faq.q3': 'Telefona tətbiq yükləmək lazımdırmı?',
    'faq.a3':
      'Android-də QRLog tətbiqini Google Play-dən yükləyə bilərsiniz. iPhone-da mağaza lazım deyil — sistem brauzerdən açılır və istəyə görə ana ekrana tətbiq kimi əlavə edilir.',
    'faq.q4': 'İşçi sistemə necə daxil olur?',
    'faq.a4':
      'Telefon nömrəsi və 4 rəqəmli PIN ilə. E-poçt tələb olunmur. İşçiləri Excel-dən toplu əlavə edə bilərsiniz — hər kəsə müvəqqəti PIN yaranır, ilk girişdə özü dəyişir.',
    'faq.q5': 'Neçə filial və işçi dəstəklənir?',
    'faq.a5':
      'Limit yoxdur. Hər filialın öz QR-ı, məkanı və iş qrafiki olur; rəhbərlik hamısını bir paneldən idarə edir, menecerlər isə yalnız öz filiallarını görür.',
    'faq.q6': 'Hesabatları Excel-ə çıxara bilərəmmi?',
    'faq.a6':
      'Bəli. Tarix aralığı və filial üzrə hesabatlar bir kliklə Excel faylı kimi yüklənir. Maaş hesablaması da eyni paneldədir.',

    'pwa.eyebrow': 'Telefonda',
    'pwa.title': 'Telefona quraşdırın',
    'pwa.sub':
      'Android-də Google Play-dən yükləyin. iPhone-da brauzerdə açın və paylaş menyusundan «Ana ekrana əlavə et» seçin — tətbiq kimi işləyir, yeniləmə də özü gəlir.',
    // Aşağıdakı üç addım yalnız iPhone üçündür — Android-də tətbiq Google Play-dən gəlir.
    'pwa.steps': 'iPhone üçün',
    'pwa.b1': 'Brauzerdə açın',
    'pwa.b2': 'Ana ekrana əlavə edin',
    'pwa.b3': 'Tətbiq kimi işlədin',
    // Google Play nişanının alt mətni (GooglePlayBadge.astro) — linkin yeganə adıdır.
    'gplay.alt': 'Google Play-də yükləyin',

    'cta.title': 'Davamiyyəti bu gün rəqəmsallaşdırın',
    'cta.sub': 'Filialı və işçiləri əlavə edin, QR posteri asın — həmin gün işə düşür.',
    'cta.assure': 'Mövcud telefonlarla işləyir — ayrıca avadanlıq almadan. İlk quraşdırmada kömək edirik.',
    'cta.btn1': 'Əlaqə saxlayın',
    'cta.login': 'Daxil ol',

    'foot.tag': 'QR əsaslı işçi davamiyyəti sistemi. Telefonla işləyir, avadanlıq tələb etmir.',
    'foot.product': 'Məhsul',
    'foot.company': 'Şirkət',
    'foot.legal': 'Hüquqi',
    'foot.contact': 'Bizimlə əlaqə',
    'foot.about': 'Haqqımızda',
    'foot.blog': 'Bloq',
    'foot.support': 'Dəstək',
    'foot.rights': 'Bütün hüquqlar qorunur.',
    'foot.privacy': 'Məxfilik',
    'foot.deletion': 'Hesabın silinməsi',
    'foot.terms': 'Şərtlər',

    'about.title': 'Haqqımızda',
    'about.sub': 'QR əsaslı işçi davamiyyəti sistemi.',
    'about.metaTitle': 'Haqqımızda — QRLog',
    'about.metaDesc':
      'QRLog — Azərbaycanda QR əsaslı işçi davamiyyəti sistemi. Davamiyyət uçotunu telefonla sadələşdiririk.',
    'about.p1':
      'QRLog işçi davamiyyətinin uçotunu sadə, sürətli və etibarlı etmək üçün yaradılıb. Turniket və bahalı terminallar əvəzinə işçilər öz telefonları ilə iş yerindəki QR posteri skan edir.',
    'about.p2':
      'Sistem hər girişdə dörd şeyi yoxlayır: posterdəki QR kodun imzasını, işçinin filial ərazisində olduğunu (GPS), tanınmış cihazdan skan etdiyini və girişdəki şəklin referansla uyğunluğunu. Radiusdan kənar skan qəbul edilmir. Şəkil alınmasa və ya üz uyğun gəlməsə isə giriş bloklanmır — yazılır və rəhbərin panelində işarələnir, çünki əmək haqqı həmin qeydə bağlıdır.',
    'about.p3':
      'Məhsul Azərbaycanda hazırlanır və istifadə olunur; tətbiqin interfeysi tam Azərbaycan dilindədir. Təmizlik, ictimai iaşə və ticarət sahələrində real şirkətlərin gündəlik davamiyyəti QRLog ilə aparılır.',
    'about.h2': 'Necə qurulur',
    'about.p4':
      'Filialları və işçiləri əlavə edirsiniz (işçiləri Excel-dən toplu da olar), hər filial üçün QR posteri çap edib asırsınız. Quraşdırma dəqiqələr çəkir və həmin gün işə düşür. Lazım olsa, ilk qurğuda kömək edirik.',

    'contact.title': 'Əlaqə',
    'contact.sub': 'Suallarınız var? Bizimlə əlaqə saxlayın.',
    'contact.metaTitle': 'Əlaqə — QRLog',
    'contact.metaDesc':
      'QRLog ilə əlaqə saxlayın. Davamiyyət sistemi, qiymət və quraşdırma haqqında suallarınızı cavablandıraq.',
    'contact.infoTitle': 'Əlaqə məlumatları',
    'contact.email': 'E-poçt',
    'contact.phone': 'Telefon',
    'contact.whatsapp': 'WhatsApp',
    'contact.writeTitle': 'Bizə yazın',
    'contact.writeText':
      'Şirkətin adını, filial sayını və təxmini işçi sayını yazsanız, sizə uyğun təklifi bir cavabda göndərərik.',
    'contact.whatsappCta': 'WhatsApp-da yazın',
    'contact.writeBtn': 'E-poçt göndər',

    'pricing.metaTitle': 'Qiymət — QRLog',
    'pricing.metaDesc':
      'QRLog davamiyyət sisteminin planları. Təşkilatınızın ölçüsünə uyğun təklif üçün əlaqə saxlayın.',

    'blog.title': 'Bloq',
    'blog.sub': 'Davamiyyət və QR sistemləri haqqında məqalələr.',
    'blog.metaTitle': 'Bloq — QRLog',
    'blog.metaDesc':
      'İşçi davamiyyəti, QR sistemləri və uçotun rəqəmsallaşdırılması haqqında məqalələr.',
    'blog.empty': 'Tezliklə ilk məqalələr burada olacaq.',
    'blog.back': '← Bütün məqalələr',

    'nf.title': 'Səhifə tapılmadı',
    'nf.sub': 'Axtardığınız səhifə köçürülüb və ya heç vaxt olmayıb.',
    'nf.btn': 'Ana səhifəyə qayıt',

    'a11y.skip': 'Keçid: əsas məzmun',
    'a11y.lang': 'Dil',
    'a11y.nav': 'Əsas menyu',

    // PageLoader — səhifənin ilk kadrında görünən vizör. `aria` yalnız ekran oxuyucu üçündür,
    // `scanning` və `done` isə alt yazının iki halıdır: yüklənərkən və "oxundu" anında.
    // Yazı CSS-də uppercase-ə çevrilir, ona görə mətnlər normal registrdə qalır.
    'loader.aria': 'Səhifə yüklənir',
    'loader.scanning': 'Skan edilir',
    'loader.done': 'Qeydə alındı',

    // WhatsAppButton — sağ aşağı küncdəki üzən düymə. `message` WhatsApp-da hazır yazılmış gəlir.
    'wa.aria': 'WhatsApp-da yazın',
    'wa.tooltip': 'WhatsApp-da yazın',
    'wa.message': 'Salam! QRLog haqqında məlumat almaq istəyirəm.',
  },

  ru: {
    'meta.title': 'QRLog — учёт посещаемости сотрудников по QR | Азербайджан',
    'meta.description':
      'QRLog — система учёта посещаемости: сотрудник сканирует QR-код своим телефоном, и вход-выход фиксируется. Каждая отметка подтверждается подписью QR, GPS, устройством и проверкой лица.',
    'meta.keywords':
      'учёт посещаемости, посещаемость сотрудников, QR учёт рабочего времени, приход уход сотрудников, GPS контроль, QRLog, Азербайджан',

    'nav.how': 'Как это работает',
    'nav.features': 'Возможности',
    'nav.pricing': 'Цены',
    'nav.faq': 'Вопросы',
    'nav.contact': 'Контакты',
    'nav.blog': 'Блог',
    'nav.about': 'О нас',
    'nav.menu': 'Меню',

    'hero.badge': 'Без отдельных устройств — работает с телефона',
    'hero.title.a': 'Учёт посещаемости одним ',
    'hero.title.hl': 'сканом',
    'hero.title.b': '',
    'hero.sub':
      'Сотрудник сканирует QR-постер на рабочем месте своим телефоном; система проверяет подпись QR, локацию, устройство и лицо — и записывает приход или уход. Турникеты, сканеры отпечатков и бумажные журналы не нужны.',
    'hero.cta2': 'Как это работает →',
    'hero.s1n': '~10 сек',
    'hero.s1l': 'на одну отметку',
    'hero.s2n': '0 ₼',
    'hero.s2l': 'затрат на оборудование',
    'hero.s3n': 'Онлайн',
    'hero.s3l': 'кто на месте — сразу',
    'hero.assure.a': 'Без отдельных устройств',
    'hero.assure.b': 'Заработает в тот же день',
    'hero.assure.c': 'Работает с телефона',
    // Birinci ekrandakı nümunə səhnə (az blokundakı şərhə bax): rəqəmlər heç bir müştərinin deyil.
    'hero.d.label': 'Демонстрационные данные',
    'hero.d.site': 'Главный офис',
    'hero.d.poster': 'Сканируйте для входа и выхода',
    'hero.d.posterNote': 'Наведите камеру телефона на код. Отметка занимает около 10 секунд.',
    'hero.d.scanned': 'Отсканировано',
    'hero.d.offlineT': 'Даже без интернета',
    'hero.d.offlineD': 'Отметка сохраняется в телефоне и уходит, когда появится связь',
    'hero.d.recorded': 'Вход записан',
    'hero.d.c1': 'Подпись QR',
    'hero.d.c2': 'Устройство',
    'hero.d.c3': 'Место (GPS)',
    'hero.d.c4': 'Совпадение лица',
    'hero.d.liveT': 'Статус',
    'hero.d.liveOn': 'активно',
    'hero.d.liveD': 'Сейчас на работе 184 человека',
    'hero.d.shotAlt': 'Экран приложения QRLog: смена сотрудника и кнопка выхода',

    'prob.eyebrow': 'Проблема',
    'prob.title.a': 'Старый способ учёта отнимает время и открыт для ',
    'prob.title.hl': 'подлога',
    'prob.title.b': '',
    'prob.sub':
      'Бумажный журнал, дорогие терминалы и учёт «со слов» — отдельная головная боль на каждом филиале, каждый месяц.',
    'prob.p1t': 'Отметка за другого',
    'prob.p1d':
      'Один человек может отметить всю бригаду. Кто пришёл на самом деле — неизвестно, а от этой записи зависит зарплата.',
    'prob.p2t': 'Дорогое оборудование, на каждом филиале',
    'prob.p2d':
      'Турникеты и сканеры отпечатков покупаются, монтируются и ломаются отдельно для каждого объекта.',
    'prob.p3t': 'Ручной учёт, запоздалая картина',
    'prob.p3d':
      'Бумажный журнал и Excel-табель отнимают часы; кто на месте, а кого нет — видно только к концу дня.',
    'prob.m1t': 'Потеря времени',
    'prob.m1d': 'часы каждый день',
    'prob.m2t': 'Лишние расходы',
    'prob.m2d': 'оборудование и сервис',
    'prob.m3t': 'Слабый контроль',
    'prob.m3d': 'открыт для подлога',
    'prob.next': 'С проблемой ясно. А решение?',
    'scan.live': 'В ЭФИРЕ',
    'scan.feed': 'Последние отметки',
    'scan.status': 'Отмечен',
    'scan.demo': 'Пример экрана',

    'trust.title': 'QRLog используют в этих сферах',

    'demo.eyebrow': 'Момент скана',
    'demo.title': 'От скана до отметки',
    'demo.sub':
      'Телефон читает QR, проверяются устройство и локация, фото сверяется с эталоном — и отметка сохранена. Даже если камера или фото не сработали, отметка не блокируется: система её помечает, но не останавливает.',
    'demo.scanning': 'Сканирование…',
    'demo.detected': 'QR найден',
    'demo.choose': 'Выберите тип',
    'demo.checkin': 'Приход',
    'demo.checkout': 'Уход',
    'demo.done': 'Готово!',
    'demo.recorded': 'Отметка сохранена',
    'demo.step1': 'Скан',
    'demo.step2': 'Устройство и локация',
    'demo.step3': 'Фотоподтверждение',
    'demo.step4': 'Готово',
    'demo.verify.title': 'Проверка устройства и локации',
    'demo.verify.device': 'Устройство распознано',
    'demo.verify.location': 'Находится на территории',
    'demo.face.title': 'Фото подтверждается',
    'demo.face.hint': 'Смотрите в камеру телефона',

    'stats.title': 'В цифрах',
    'stats.sub': 'Без дорогого оборудования и сложного внедрения.',
    'stats.s1': 'секунд на одну отметку',
    'stats.s2': 'манатов на оборудование — хватает телефона',
    'stats.s3': 'уровня проверки: подпись QR, место, устройство, лицо',
    'stats.s4': 'на число филиалов и сотрудников',
    'stats.s4v': 'Без лимита',

    'how.eyebrow': 'Принцип работы',
    'how.title': 'Готово за три шага',
    'how.sub': 'Настраивается один раз — дальше работает само.',
    'how.s1t': 'Повесьте QR-постер',
    'how.s1d':
      'Для каждого филиала один раз печатается постоянный QR-постер. Код не меняется — перепечатывать не нужно.',
    'how.s2t': 'Сотрудник сканирует',
    'how.s2d':
      'Приходя и уходя, сотрудник сканирует QR своим телефоном. Подпись QR, место, устройство и лицо проверяются одновременно.',
    'how.s3t': 'Руководство видит онлайн',
    'how.s3d':
      'Отметки сразу попадают в админ-панель. Фильтруйте по датам, выгружайте отчёт в Excel, разбивайте по филиалам.',
    'how.s1m': '0 ₼ оборудование',
    'how.s2m': '~10 секунд',
    'how.s3m': 'сразу',
    'how.c1': 'Подпись QR',
    'how.c2': 'Место',
    'how.c3': 'Устройство',
    'how.c4': 'Лицо',
    'how.tabs': 'Как это работает',

    'feat.eyebrow': 'Возможности',
    'feat.title': 'Всё, что нужно для учёта',
    'feat.sub': 'От ежедневных отметок до месячного отчёта — в одной панели.',
    'feat.f1t': 'Живая доска посещаемости',
    'feat.f1d': 'Кто пришёл, кто ушёл, кого нет — видно сразу, без ручного подсчёта.',
    'feat.f2t': 'Отчёты в Excel',
    'feat.f2d': 'Отчёт по датам и филиалам выгружается в Excel одним кликом.',
    'feat.f3t': 'Проверка геолокации',
    'feat.f3d': 'Место каждого скана сверяется с заданным радиусом филиала; отметка извне сразу помечается.',
    'feat.f4t': 'Привязка устройства',
    'feat.f4d':
      'Каждый сотрудник привязан к своему телефону; вход с чужого устройства контролируется.',
    'feat.f5t': 'Роли и охват филиалов',
    'feat.f5d': 'Сотрудник, менеджер, админ. Менеджер видит только свои филиалы — ни строкой больше.',
    'feat.f6t': 'Android и iPhone',
    'feat.f6d': 'На Android устанавливается из Google Play. На iPhone открывается в браузере и добавляется на главный экран как приложение.',

    'panel.eyebrow': 'Панель управления',
    'panel.title': 'Один день руководителя',
    'panel.sub':
      'Утром — кто пришёл, днём — что требует внимания, вечером — как закрылся день. В панели QRLog всё это видно с одного взгляда.',
    'panel.m1.label': 'Кто пришёл',
    'panel.m1.head': 'Каждый, кто прошёл через дверь, уже здесь.',
    'panel.m1.text':
      'Не нужно звонить и открывать журнал — панель сама считает и делит по филиалам.',
    'panel.m1.meta1': 'Обновляется в реальном времени',
    'panel.m1.meta2': 'Все филиалы в одном месте',
    'panel.m2.label': 'Что требует внимания',
    'panel.m2.head': 'Вместе с проблемой приходит и причина.',
    'panel.m2.text':
      'Если проблема повторяется в одном филиале, система связывает её не с сотрудником, а с местом — радиусом геозоны или расположением постера.',
    'panel.m2.meta1': 'Отклонённый скан · с причиной',
    'panel.m2.meta2': 'Оповещение по филиалу',
    'panel.m3.label': 'День закрыт',
    'panel.m3.head': 'День закрывается одной таблицей.',
    'panel.m3.text':
      'Кто во сколько пришёл и ушёл, у кого не совпало лицо — всё в одном месте. Отправить бухгалтеру — один клик.',
    'panel.m3.meta1': 'Выгрузка в Excel',
    'panel.m3.meta2': 'За любой день',
    'panel.tablist': 'Моменты дня',
    'panel.prev': 'Предыдущий момент',
    'panel.next': 'Следующий момент',
    'panel.carousel': 'карусель',
    'panel.slide': 'слайд',
    // Ekranlar məhsulun özüdür, məhsul isə yalnız azərbaycancadır (panelUi-yə bax). Alt yazı bunu
    // açıq deyir ki, /ru/ səhifəsindəki azərbaycanca mətn səhv kimi görünməsin.
    'panel.note': 'Экраны из панели QRLog (интерфейс на азербайджанском) · данные условные',
    ...panelUi,

    'aud.eyebrow': 'Для кого',
    'aud.title': 'Для любой организации с сотрудниками',
    'aud.sub': 'Особенно там, где персонал распределён по нескольким объектам.',
    'aud.a1t': 'Клининг и благоустройство',
    'aud.a1d': 'Сервисные компании с персоналом на разных объектах.',
    'aud.a2t': 'Кафе и рестораны',
    'aud.a2d': 'Точный приход-уход сменного персонала.',
    'aud.a3t': 'Сети магазинов',
    'aud.a3d': 'Торговый персонал по нескольким филиалам.',
    'aud.a4t': 'Строительство',
    'aud.a4d': 'Учёт рабочих на объектах.',
    'aud.a5t': 'Учреждения и госструктуры',
    'aud.a5d': 'Точный учёт на предприятиях с большим штатом.',
    'aud.a6t': 'Сфера услуг',
    'aud.a6d': 'Клиники, логистика и другой персонал.',

    'sec.eyebrow': 'Безопасность',
    'sec.title': 'Ваши данные защищены',
    'sec.sub': 'Данные каждой компании изолированы, и каждый видит только своё.',
    'sec.i1t': 'Шифрованное соединение',
    'sec.i1d': 'Весь трафик идёт по HTTPS; сертификаты обновляются автоматически.',
    'sec.i2t': 'Изоляция между компаниями',
    'sec.i2d':
      'Если запрос нельзя отнести к конкретной компании, он отклоняется — компании «по умолчанию» не существует.',
    'sec.i3t': 'Ежедневные резервные копии',
    'sec.i3d': 'База копируется каждый день, восстановление регулярно проверяется.',
    'sec.i4t': 'Доступ по ролям',
    'sec.i4d': 'Сотрудник, менеджер и админ видят разное; охват менеджера ограничен его филиалом.',

    'mod.eyebrow': 'Модули',
    'mod.title': 'Не только посещаемость',
    'mod.sub': 'Всё в той же панели, без дополнительных программ.',
    'mod.m1': 'Доска посещаемости',
    'mod.m2': 'Отчёт в Excel',
    'mod.m3': 'Расчёт зарплаты',
    'mod.m4': 'Отпуска и отгулы',
    'mod.m5': 'График смен',
    'mod.m6': 'Объявления',
    'mod.m7': 'Задачи',
    'mod.m8': 'Push-уведомления',
    'mod.m9': 'Режим киоска',
    'mod.m10': 'Много филиалов',

    'test.eyebrow': 'Отзывы',
    'test.title': 'Что говорят клиенты',
    'test.sub': 'Только отзывы, которые разрешено публиковать с именем и должностью.',

    'price.eyebrow': 'Цены',
    'price.title': 'Простые и прозрачные цены',
    'price.sub': 'Платите за сотрудника — все функции включены в каждый пакет.',
    'price.subQuote': 'Готовим предложение под ваши задачи. Без скрытых платежей.',
    'price.popular': 'ПОПУЛЯРНЫЙ',
    'price.mo': ' / сотрудник / мес',
    'price.note': 'Все цены — в месяц. За каждую локацию (филиал) дополнительно 5 ₼/мес. Без скрытых платежей.',
    'price.p1n': 'Start',
    'price.p1d': 'Для небольших команд.',
    'price.p1a': '4 ₼',
    'price.p1f1': '1–10 сотрудников',
    'price.p1f2': 'Каждая локация: 5 ₼/мес',
    'price.p1f3': 'Все функции включены',
    'price.p1c': 'Связаться',
    'price.p2n': 'Бизнес',
    'price.p2d': 'Для растущих компаний.',
    'price.p2a': '3.5 ₼',
    'price.p2f1': '11–50 сотрудников',
    'price.p2f2': 'Каждая локация: 5 ₼/мес',
    'price.p2f3': 'Все функции включены',
    'price.p2c': 'Связаться',
    'price.p3n': 'Корпоратив',
    'price.p3d': 'Для больших команд.',
    'price.p3a': '3 ₼',
    'price.p3f1': '51–100 сотрудников',
    'price.p3f2': 'Каждая локация: 5 ₼/мес',
    'price.p3f3': 'Все функции включены',
    'price.p3c': 'Связаться',
    'price.p4n': 'Enterprise',
    'price.p4d': 'Индивидуальное решение для крупных организаций.',
    'price.p4a': 'Индивид.',
    'price.p4f1': '101+ сотрудников',
    'price.p4f2': 'Индивидуальные цена и условия',
    'price.p4f3': 'Помощь при внедрении',
    'price.p4c': 'Связаться',
    'price.quoteTitle': 'Стоимость рассчитывается индивидуально',
    'price.quoteText':
      'Сумма зависит от числа сотрудников, количества филиалов и нужных вам возможностей. Зададим несколько вопросов и пришлём точное предложение — без скрытых платежей.',
    'price.quoteBtn': 'Получить предложение',
    'cust.title': 'Компании, работающие на QRLog',
    'cust.eyebrow': 'НАДЕЖНЫЕ ПАРТНЕРЫ',
    'cust.c1s': 'Городское благоустройство',
    'cust.c2s': 'Профессиональный клининг',
    'cust.c3s': 'Кафе и рестораны',
    'cust.c4s': 'Ландшафт и зона отдыха',

    'faq.eyebrow': 'Вопросы',
    'faq.title': 'Часто задаваемые вопросы',
    'faq.sub': 'Не нашли ответ? Напишите нам — поможем.',
    'faq.q1': 'Нужно ли покупать отдельные устройства?',
    'faq.a1':
      'Нет. Сотрудники используют свои телефоны. Турникеты, сканеры отпечатков и терминалы не нужны — достаточно распечатанного QR-постера на стене.',
    'faq.q2': 'Может ли сотрудник отметиться из дома или за другого?',
    'faq.a2':
      'Для каждого филиала задаются координаты и радиус; скан принимается только там. Кроме того, сотрудник привязан к своему устройству, а при отметке делается фото и сверяется с эталоном.',
    'faq.q3': 'Нужно ли устанавливать приложение?',
    'faq.a3':
      'На Android приложение QRLog можно установить из Google Play. На iPhone магазин не нужен — система открывается в браузере и при желании добавляется на главный экран как приложение.',
    'faq.q4': 'Как сотрудник входит в систему?',
    'faq.a4':
      'По номеру телефона и 4-значному PIN. Email не требуется. Сотрудников можно загрузить списком из Excel — каждому создаётся временный PIN, который он меняет при первом входе.',
    'faq.q5': 'Сколько филиалов и сотрудников поддерживается?',
    'faq.a5':
      'Ограничений нет. У каждого филиала свой QR, локация и график; руководство управляет всем из одной панели, а менеджеры видят только свои филиалы.',
    'faq.q6': 'Можно ли выгрузить отчёты в Excel?',
    'faq.a6':
      'Да. Отчёты по периоду и филиалу выгружаются в Excel одним кликом. Расчёт зарплаты — в той же панели.',

    'pwa.eyebrow': 'На телефоне',
    'pwa.title': 'Установите на телефон',
    'pwa.sub':
      'На Android — скачайте из Google Play. На iPhone откройте в браузере и в меню «Поделиться» выберите «На экран Домой» — работает как приложение, обновления приходят сами.',
    'pwa.steps': 'Для iPhone',
    'pwa.b1': 'Откройте в браузере',
    'pwa.b2': 'Добавьте на главный экран',
    'pwa.b3': 'Пользуйтесь как приложением',
    'gplay.alt': 'Доступно в Google Play',

    'cta.title': 'Оцифруйте посещаемость уже сегодня',
    'cta.sub': 'Добавьте филиалы и сотрудников, повесьте QR-постер — заработает в тот же день.',
    'cta.assure': 'Работает на обычных телефонах — без отдельного оборудования. Поможем с первой настройкой.',
    'cta.btn1': 'Связаться',
    'cta.login': 'Войти',

    'foot.tag': 'Система учёта посещаемости по QR. Работает с телефона, оборудование не нужно.',
    'foot.product': 'Продукт',
    'foot.company': 'Компания',
    'foot.legal': 'Правовая информация',
    'foot.contact': 'Связаться с нами',
    'foot.about': 'О нас',
    'foot.blog': 'Блог',
    'foot.support': 'Поддержка',
    'foot.rights': 'Все права защищены.',
    'foot.privacy': 'Конфиденциальность',
    'foot.deletion': 'Удаление аккаунта',
    'foot.terms': 'Условия',

    'about.title': 'О нас',
    'about.sub': 'Система учёта посещаемости на основе QR.',
    'about.metaTitle': 'О нас — QRLog',
    'about.metaDesc':
      'QRLog — система учёта посещаемости сотрудников по QR в Азербайджане. Упрощаем учёт с помощью телефона.',
    'about.p1':
      'QRLog создан, чтобы сделать учёт посещаемости простым, быстрым и надёжным. Вместо турникетов и дорогих терминалов сотрудники сканируют QR-постер на рабочем месте своим телефоном.',
    'about.p2':
      'При каждом входе система проверяет четыре вещи: подпись QR-кода на постере, нахождение сотрудника на территории филиала (GPS), вход с привязанного устройства и совпадение фото с эталоном. Скан за пределами радиуса не принимается. Если же фото не получилось или лицо не совпало, вход не блокируется — он записывается и отмечается в панели руководителя, потому что от этой записи зависит зарплата.',
    'about.p3':
      'Продукт разрабатывается и используется в Азербайджане; интерфейс приложения полностью на азербайджанском. Реальные компании в клининге, общепите и рознице ведут ежедневный учёт в QRLog.',
    'about.h2': 'Как всё настраивается',
    'about.p4':
      'Вы добавляете филиалы и сотрудников (сотрудников можно загрузить из Excel), печатаете и вешаете QR-постер для каждого филиала. Настройка занимает минуты и работает в тот же день. При необходимости помогаем с первым запуском.',

    'contact.title': 'Контакты',
    'contact.sub': 'Есть вопросы? Свяжитесь с нами.',
    'contact.metaTitle': 'Контакты — QRLog',
    'contact.metaDesc':
      'Свяжитесь с QRLog. Ответим на вопросы о системе учёта посещаемости, ценах и внедрении.',
    'contact.infoTitle': 'Контактные данные',
    'contact.email': 'E-mail',
    'contact.phone': 'Телефон',
    'contact.whatsapp': 'WhatsApp',
    'contact.writeTitle': 'Напишите нам',
    'contact.writeText':
      'Укажите название компании, количество филиалов и примерное число сотрудников — пришлём подходящее предложение одним письмом.',
    'contact.whatsappCta': 'Написать в WhatsApp',
    'contact.writeBtn': 'Написать письмо',

    'pricing.metaTitle': 'Цены — QRLog',
    'pricing.metaDesc':
      'Планы системы учёта посещаемости QRLog. Свяжитесь с нами для предложения под размер вашей организации.',

    'blog.title': 'Блог',
    'blog.sub': 'Статьи о посещаемости и QR-системах.',
    'blog.metaTitle': 'Блог — QRLog',
    'blog.metaDesc': 'Статьи об учёте посещаемости, QR-системах и цифровизации учёта.',
    'blog.empty': 'Скоро здесь появятся первые статьи.',
    'blog.back': '← Все статьи',

    'nf.title': 'Страница не найдена',
    'nf.sub': 'Страница, которую вы ищете, была перемещена или никогда не существовала.',
    'nf.btn': 'На главную',

    'a11y.skip': 'Перейти к содержимому',
    'a11y.lang': 'Язык',
    'a11y.nav': 'Основное меню',
    'loader.aria': 'Страница загружается',
    'loader.scanning': 'Сканирование',
    'loader.done': 'Записано',

    'wa.aria': 'Написать в WhatsApp',
    'wa.tooltip': 'Написать в WhatsApp',
    'wa.message': 'Здравствуйте! Хочу узнать подробнее о QRLog.',
  },

  en: {
    'meta.title': 'QRLog — QR-based staff attendance system | Azerbaijan',
    'meta.description':
      'QRLog is an attendance system: employees scan a QR code with their own phone to log check-in and check-out. Every entry is verified by a signed QR, GPS location, device and face check.',
    'meta.keywords':
      'staff attendance, employee attendance system, QR attendance, time tracking, GPS check-in, workforce attendance, QRLog, Azerbaijan',

    'nav.how': 'How it works',
    'nav.features': 'Features',
    'nav.pricing': 'Pricing',
    'nav.faq': 'FAQ',
    'nav.contact': 'Contact',
    'nav.blog': 'Blog',
    'nav.about': 'About',
    'nav.menu': 'Menu',

    'hero.badge': 'No extra hardware — it runs on a phone',
    'hero.title.a': 'Run staff attendance with a single ',
    'hero.title.hl': 'scan',
    'hero.title.b': '',
    'hero.sub':
      'An employee scans the QR poster at their site with their own phone; the system checks the QR signature, the location, the device and the face, then records the check-in or check-out. No turnstiles, no fingerprint readers, no paper register.',
    'hero.cta2': 'How it works →',
    'hero.s1n': '~10 sec',
    'hero.s1l': 'to record one check-in',
    'hero.s2n': '0 ₼',
    'hero.s2l': 'spent on hardware',
    'hero.s3n': 'Live',
    'hero.s3l': "who's on site — right now",
    'hero.assure.a': 'No extra hardware',
    'hero.assure.b': 'Live the same day',
    'hero.assure.c': 'Works from a phone',
    // Birinci ekrandakı nümunə səhnə (az blokundakı şərhə bax): rəqəmlər heç bir müştərinin deyil.
    'hero.d.label': 'Sample data',
    'hero.d.site': 'Head office',
    'hero.d.poster': 'Scan to check in and out',
    'hero.d.posterNote': 'Point your phone camera at the code. A check-in takes about 10 seconds.',
    'hero.d.scanned': 'Scanned',
    'hero.d.offlineT': 'Even with no internet',
    'hero.d.offlineD': 'The record is kept on the phone and sent once the connection is back',
    'hero.d.recorded': 'Check-in recorded',
    'hero.d.c1': 'QR signature',
    'hero.d.c2': 'Device',
    'hero.d.c3': 'Location (GPS)',
    'hero.d.c4': 'Face match',
    'hero.d.liveT': 'Live status',
    'hero.d.liveOn': 'active',
    'hero.d.liveD': '184 people are at work right now',
    'hero.d.shotAlt': 'The QRLog app screen: an employee shift and the check-out button',

    'prob.eyebrow': 'The problem',
    'prob.title.a': 'The old way to track attendance is slow and easy to ',
    'prob.title.hl': 'fake',
    'prob.title.b': '',
    'prob.sub':
      'Paper logs, expensive terminals and attendance “on trust” — a separate headache at every site, every month.',
    'prob.p1t': 'Checking in for someone else',
    'prob.p1d':
      'One person can mark a whole crew as present. Who actually showed up is anyone’s guess — and pay depends on that record.',
    'prob.p2t': 'Costly hardware, at every site',
    'prob.p2d':
      'Turnstiles and fingerprint terminals are bought, mounted and break down separately for each location.',
    'prob.p3t': 'Manual records, a late picture',
    'prob.p3d':
      'Paper logs and Excel timesheets eat hours; who is in and who is out only becomes clear at the end of the day.',
    'prob.m1t': 'Time lost',
    'prob.m1d': 'hours every day',
    'prob.m2t': 'Extra cost',
    'prob.m2d': 'hardware and service',
    'prob.m3t': 'Weak control',
    'prob.m3d': 'open to fraud',
    'prob.next': 'That is the problem. What is the fix?',
    'scan.live': 'LIVE',
    'scan.feed': 'Recent check-ins',
    'scan.status': 'Recorded',
    'scan.demo': 'Sample screen',

    'trust.title': 'QRLog is used across these sectors',

    'demo.eyebrow': 'The scan',
    'demo.title': 'From scan to check-in',
    'demo.sub':
      'The phone reads the QR, device and location are verified, the photo is compared against the reference — and the check-in is written. If the camera or the photo fails, the check-in is not blocked: the system flags it, it does not stop it.',
    'demo.scanning': 'Scanning…',
    'demo.detected': 'QR detected',
    'demo.choose': 'Choose entry type',
    'demo.checkin': 'Check-in',
    'demo.checkout': 'Check-out',
    'demo.done': 'Done!',
    'demo.recorded': 'Attendance recorded',
    'demo.step1': 'Scan',
    'demo.step2': 'Device & location',
    'demo.step3': 'Photo check',
    'demo.step4': 'Done',
    'demo.verify.title': 'Verifying device & location',
    'demo.verify.device': 'Device recognised',
    'demo.verify.location': 'Inside the site radius',
    'demo.face.title': 'Confirming the photo',
    'demo.face.hint': 'Look at the phone camera',

    'stats.title': 'In numbers',
    'stats.sub': 'No expensive hardware, no complicated rollout.',
    'stats.s1': 'seconds to record a check-in',
    'stats.s2': 'manat of hardware cost — a phone is enough',
    'stats.s3': 'layers of checks: QR signature, location, device, face',
    'stats.s4': 'branches and employees',
    'stats.s4v': 'Unlimited',

    'how.eyebrow': 'How it works',
    'how.title': 'Ready in three steps',
    'how.sub': 'Set it up once — it runs itself after that.',
    'how.s1t': 'Put up the QR poster',
    'how.s1d':
      'Each site gets one permanent printed QR poster on the wall. The code does not rotate, so the poster never needs reprinting.',
    'how.s2t': 'Staff scan with their phone',
    'how.s2d':
      'On arrival and departure, the employee scans the QR with their own phone. The QR signature, location, device and face are checked at once.',
    'how.s3t': 'Managers see it live',
    'how.s3d':
      'Check-ins land in the admin panel immediately. Filter by date, export to Excel, split by site.',
    'how.s1m': '0 ₼ hardware',
    'how.s2m': '~10 seconds',
    'how.s3m': 'instant',
    'how.c1': 'QR signature',
    'how.c2': 'Location',
    'how.c3': 'Device',
    'how.c4': 'Face',
    'how.tabs': 'How it works',

    'feat.eyebrow': 'Features',
    'feat.title': 'Everything attendance needs',
    'feat.sub': 'From the daily board to the monthly report, in one panel.',
    'feat.f1t': 'Live attendance board',
    'feat.f1d':
      'Who arrived, who left, who is missing — visible instantly, with nothing counted by hand.',
    'feat.f2t': 'Excel reports',
    'feat.f2d': 'Download a report by date range and site as an Excel file in one click.',
    'feat.f3t': 'GPS location check',
    'feat.f3d': 'Every scan is checked against the site radius, and one from outside it is flagged immediately.',
    'feat.f4t': 'Device binding',
    'feat.f4d':
      'Each employee is bound to their own phone; a scan from an unknown device is controlled.',
    'feat.f5t': 'Roles and site scope',
    'feat.f5d': 'Employee, manager, admin. A manager sees only their own sites — not one row more.',
    'feat.f6t': 'Android and iPhone',
    'feat.f6d': 'On Android, install it from Google Play. On iPhone, open it in the browser and add it to the home screen like an app.',

    'panel.eyebrow': 'Admin panel',
    'panel.title': "A manager's day",
    'panel.sub':
      'Who arrived in the morning, what needs attention at noon, how the day closed in the evening. In the QRLog panel, each takes a single glance.',
    'panel.m1.label': "Who's in",
    'panel.m1.head': 'Everyone who walked in is already here.',
    'panel.m1.text': 'No calls, no logbook — the panel counts on its own and splits by branch.',
    'panel.m1.meta1': 'Updates live',
    'panel.m1.meta2': 'All branches in one place',
    'panel.m2.label': 'What needs attention',
    'panel.m2.head': 'Every problem comes with its reason.',
    'panel.m2.text':
      'If it keeps happening at one branch, the system ties it to the place, not the person — the geofence radius or where the poster hangs.',
    'panel.m2.meta1': 'Rejected scans · with reasons',
    'panel.m2.meta2': 'Branch-level alerts',
    'panel.m3.label': 'Day closed',
    'panel.m3.head': 'The day closes in one table.',
    'panel.m3.text':
      "Who came and left when, whose face didn't match — all in one place. One click to send it to accounting.",
    'panel.m3.meta1': 'Export to Excel',
    'panel.m3.meta2': 'For any day',
    'panel.tablist': 'Moments of the day',
    'panel.prev': 'Previous moment',
    'panel.next': 'Next moment',
    'panel.carousel': 'carousel',
    'panel.slide': 'slide',
    // Ekranlar məhsulun özüdür, məhsul isə yalnız azərbaycancadır (panelUi-yə bax). Alt yazı bunu
    // açıq deyir ki, /en/ səhifəsindəki azərbaycanca mətn səhv kimi görünməsin.
    'panel.note': 'Screens from the QRLog panel (interface in Azerbaijani) · sample data',
    ...panelUi,

    'aud.eyebrow': "Who it's for",
    'aud.title': 'For any organisation with staff',
    'aud.sub': 'Especially where the team is spread across several sites.',
    'aud.a1t': 'Cleaning & facilities',
    'aud.a1d': 'Service companies with staff on many sites.',
    'aud.a2t': 'Cafés & restaurants',
    'aud.a2d': 'Accurate check-in and check-out for shift staff.',
    'aud.a3t': 'Retail chains',
    'aud.a3d': 'Shop-floor teams across several branches.',
    'aud.a4t': 'Construction',
    'aud.a4d': 'Tracking worker attendance on site.',
    'aud.a5t': 'Public institutions',
    'aud.a5d': 'Precise records in large organisations.',
    'aud.a6t': 'Services',
    'aud.a6d': 'Clinics, logistics and other teams.',

    'sec.eyebrow': 'Security',
    'sec.title': 'Your data stays protected',
    'sec.sub': "Each company's data is isolated, and everyone sees only their own.",
    'sec.i1t': 'Encrypted connection',
    'sec.i1d': 'All traffic goes over HTTPS; certificates renew automatically.',
    'sec.i2t': 'Isolation between companies',
    'sec.i2d':
      'A request that cannot be attributed to a company is rejected — there is no default company to fall through to.',
    'sec.i3t': 'Daily backups',
    'sec.i3d': 'The database is backed up every day and restores are tested regularly.',
    'sec.i4t': 'Role-based access',
    'sec.i4d':
      "Employee, manager and admin see different things; a manager's scope stops at their own site.",

    'mod.eyebrow': 'Modules',
    'mod.title': 'There is more than attendance',
    'mod.sub': 'All in the same panel, with no extra software.',
    'mod.m1': 'Attendance board',
    'mod.m2': 'Excel reports',
    'mod.m3': 'Payroll',
    'mod.m4': 'Leave & time off',
    'mod.m5': 'Shift schedules',
    'mod.m6': 'Announcements',
    'mod.m7': 'Tasks',
    'mod.m8': 'Push notifications',
    'mod.m9': 'Kiosk mode',
    'mod.m10': 'Multi-site management',

    'test.eyebrow': 'Testimonials',
    'test.title': 'What customers say',
    'test.sub': 'Only quotes we have permission to publish with a name and role.',

    'price.eyebrow': 'Pricing',
    'price.title': 'Simple, transparent pricing',
    'price.sub': 'Pay per employee — every feature is included in every plan.',
    'price.subQuote': 'We put together a quote that fits what you need. No hidden fees.',
    'price.popular': 'POPULAR',
    'price.mo': ' / employee / mo',
    'price.note': 'All prices are monthly. Each location (site) adds 5 ₼/month. No hidden fees.',
    'price.p1n': 'Start',
    'price.p1d': 'For small teams.',
    'price.p1a': '4 ₼',
    'price.p1f1': '1–10 employees',
    'price.p1f2': 'Each location: 5 ₼/mo',
    'price.p1f3': 'All features included',
    'price.p1c': 'Contact us',
    'price.p2n': 'Business',
    'price.p2d': 'For growing companies.',
    'price.p2a': '3.5 ₼',
    'price.p2f1': '11–50 employees',
    'price.p2f2': 'Each location: 5 ₼/mo',
    'price.p2f3': 'All features included',
    'price.p2c': 'Contact us',
    'price.p3n': 'Corporate',
    'price.p3d': 'For large teams.',
    'price.p3a': '3 ₼',
    'price.p3f1': '51–100 employees',
    'price.p3f2': 'Each location: 5 ₼/mo',
    'price.p3f3': 'All features included',
    'price.p3c': 'Contact us',
    'price.p4n': 'Enterprise',
    'price.p4d': 'A tailored setup for large organisations.',
    'price.p4a': 'Custom',
    'price.p4f1': '101+ employees',
    'price.p4f2': 'Custom pricing and terms',
    'price.p4f3': 'Onboarding support',
    'price.p4c': 'Contact us',
    'price.quoteTitle': 'Priced per organisation',
    'price.quoteText':
      'The figure depends on how many people you have, how many sites, and which capabilities you need. A few questions and we send an exact quote — no hidden fees.',
    'price.quoteBtn': 'Get a quote',
    'cust.title': 'Companies running QRLog',
    'cust.eyebrow': 'TRUSTED PARTNERS',
    'cust.c1s': 'Municipal grounds and upkeep',
    'cust.c2s': 'Professional cleaning services',
    'cust.c3s': 'Café and restaurant',
    'cust.c4s': 'Landscaping and leisure venue',

    'faq.eyebrow': 'FAQ',
    'faq.title': 'Frequently asked questions',
    'faq.sub': "Didn't find your answer? Write to us and we'll help.",
    'faq.q1': 'Do we need to buy any hardware?',
    'faq.a1':
      'No. Employees use their own phones. No turnstile, fingerprint reader or terminal is needed — a printed QR poster on the wall is enough.',
    'faq.q2': 'Can someone check in from home, or for a colleague?',
    'faq.a2':
      'Every site has its own coordinates and radius; a scan is only accepted there. On top of that each employee is bound to their own device, and a photo is taken at check-in and compared with the reference.',
    'faq.q3': 'Does everyone need to install an app?',
    'faq.a3':
      'On Android, you can install the QRLog app from Google Play. On iPhone, no store is needed — it opens in the browser and can be added to the home screen like an app.',
    'faq.q4': 'How do employees sign in?',
    'faq.a4':
      'With a phone number and a 4-digit PIN. No email required. Employees can be imported from Excel in bulk — each one gets a temporary PIN and sets their own on first sign-in.',
    'faq.q5': 'How many sites and employees are supported?',
    'faq.a5':
      'There is no limit. Each site has its own QR, location and schedule; management runs everything from one panel, while managers see only their own sites.',
    'faq.q6': 'Can I export reports to Excel?',
    'faq.a6':
      'Yes. Reports by date range and site download as an Excel file in one click. Payroll lives in the same panel.',

    'pwa.eyebrow': 'On the phone',
    'pwa.title': 'Install on your phone',
    'pwa.sub':
      'On Android, get it from Google Play. On iPhone, open it in the browser and choose “Add to Home Screen” from the share menu — it works like an app and updates itself.',
    'pwa.steps': 'On iPhone',
    'pwa.b1': 'Open it in the browser',
    'pwa.b2': 'Add to home screen',
    'pwa.b3': 'Use it like an app',
    'gplay.alt': 'Get it on Google Play',

    'cta.title': 'Digitise attendance today',
    'cta.sub': 'Add your sites and employees, put up the QR poster — it works the same day.',
    'cta.assure': 'Runs on the phones people already have — no separate hardware. We help with the first setup.',
    'cta.btn1': 'Contact us',
    'cta.login': 'Log in',

    'foot.tag': 'QR-based staff attendance. Runs on a phone, needs no hardware.',
    'foot.product': 'Product',
    'foot.company': 'Company',
    'foot.legal': 'Legal',
    'foot.contact': 'Contact us',
    'foot.about': 'About',
    'foot.blog': 'Blog',
    'foot.support': 'Support',
    'foot.rights': 'All rights reserved.',
    'foot.privacy': 'Privacy',
    'foot.deletion': 'Account deletion',
    'foot.terms': 'Terms',

    'about.title': 'About us',
    'about.sub': 'QR-based staff attendance.',
    'about.metaTitle': 'About — QRLog',
    'about.metaDesc':
      'QRLog is a QR-based staff attendance system built in Azerbaijan. We make attendance records simple, from a phone.',
    'about.p1':
      'QRLog exists to make attendance records simple, fast and reliable. Instead of turnstiles and expensive terminals, employees scan the QR poster at their workplace with their own phone.',
    'about.p2':
      "On every check-in the system verifies four things: the signature of the QR code on the poster, that the employee is within the branch area (GPS), that the scan comes from their registered device, and that the photo matches the reference. Scans outside the radius are rejected. If the photo fails or the face doesn't match, the check-in isn't blocked — it's recorded and flagged in the manager's panel, because payroll depends on that record.",
    'about.p3':
      'The product is built and used in Azerbaijan; the app interface is entirely in Azerbaijani. Real companies in cleaning, hospitality and retail run their daily attendance on QRLog.',
    'about.h2': 'Getting set up',
    'about.p4':
      'You add your sites and employees (employees can be imported from Excel), then print and hang a QR poster for each site. Setup takes minutes and works the same day. We help with the first rollout if you want it.',

    'contact.title': 'Contact',
    'contact.sub': 'Questions? Get in touch.',
    'contact.metaTitle': 'Contact — QRLog',
    'contact.metaDesc':
      'Contact QRLog. We answer questions about the attendance system, pricing and rollout.',
    'contact.infoTitle': 'Contact details',
    'contact.email': 'Email',
    'contact.phone': 'Phone',
    'contact.whatsapp': 'WhatsApp',
    'contact.writeTitle': 'Write to us',
    'contact.writeText':
      'Tell us the company name, how many sites you have and roughly how many employees, and we will send a matching quote in one reply.',
    'contact.whatsappCta': 'Message on WhatsApp',
    'contact.writeBtn': 'Send an email',

    'pricing.metaTitle': 'Pricing — QRLog',
    'pricing.metaDesc':
      'QRLog attendance system plans. Contact us for a quote that matches the size of your organisation.',

    'blog.title': 'Blog',
    'blog.sub': 'Articles about attendance and QR systems.',
    'blog.metaTitle': 'Blog — QRLog',
    'blog.metaDesc': 'Articles about staff attendance, QR systems and digitising records.',
    'blog.empty': 'The first articles will appear here soon.',
    'blog.back': '← All articles',

    'nf.title': 'Page not found',
    'nf.sub': 'The page you are looking for has moved, or never existed.',
    'nf.btn': 'Back to the homepage',

    'a11y.skip': 'Skip to main content',
    'a11y.lang': 'Language',
    'a11y.nav': 'Main navigation',
    'loader.aria': 'Page is loading',
    'loader.scanning': 'Scanning',
    'loader.done': 'Recorded',

    'wa.aria': 'Message us on WhatsApp',
    'wa.tooltip': 'Message us on WhatsApp',
    'wa.message': "Hello! I'd like to learn more about QRLog.",
  },
} as const

// Mümkün açarların siyahısı az blokundan götürülür. Səhv açar yazsan (məs. t('nav.faqq')),
// redaktor və `npm run check` xəta göstərir.
// keyof obyektin açarlarının adlarını tip kimi götürür: https://www.typescriptlang.org/docs/handbook/2/keyof-types.html
export type UIKey = keyof (typeof ui)['az']

/**
 * Verilən dil üçün t() funksiyasını qaytarır. Komponentdə belə işlənir:
 *   const t = useTranslations(lang)
 *   t('nav.faq') → 'Suallar'
 * Açar bu dildə yoxdursa, AZ mətni qaytarılır — səhifədə boş yer qalmasın.
 */
export function useTranslations(lang: Lang) {
  // `as Record<string, string>` TypeScript-ə bloka sadə «açar → mətn» cədvəli kimi baxmağı deyir.
  // Beləcə hər dil blokunda eyni açarların olması tələb olunmur: çatışmayan açar sadəcə undefined olur.
  // https://www.typescriptlang.org/docs/handbook/2/everyday-types.html#type-assertions
  const currentTexts = ui[lang] as Record<string, string>
  const defaultTexts = ui[defaultLang] as Record<string, string>

  return function t(key: UIKey): string {
    const text = currentTexts[key]
    // Yalnız undefined yoxlanır: boş mətn ('') də düzgün dəyərdir (məs. ru/en-də 'hero.title.b'),
    // onu AZ mətni ilə əvəz etmək olmaz.
    if (text !== undefined) {
      return text
    }
    return defaultTexts[key]
  }
}

/**
 * Tərcümə mətnindəki {n} kimi yer tutucuları doldurur: fmt(t('panel.ui.m2.times'), { n: 3 }) → '3 dəfə'.
 * Dəyəri verilməyən yer tutucu olduğu kimi qalır — yazı səhvi yox olmasın, səhifədə görünsün.
 *
 * Şablonda rəqəm ayrıca durur ('{n} dəfə', 'son {t}') — heç vaxt şəkilçiyə yapışıq yox ('{n}-si').
 * Azərbaycan dilində şəkilçi rəqəmin son səsinə uyğunlaşır (48-i, 43-ü, 40-ı), ona görə sabit
 * şəkilçi rəqəm dəyişən kimi səhv olur.
 */
export function fmt(template: string, values: Record<string, string | number>): string {
  // Axtarılan nümunə: "{", sonra bir və ya bir neçə latın hərfi / rəqəm / alt xətt, sonra "}".
  // Mötərizədəki (\w+) hissə yer tutucunun adıdır — fillPlaceholder-ə `name` kimi gəlir.
  // Sondakı `g` bütün yer tutucuları tapır, təkcə birincini yox.
  const placeholderPattern = /\{(\w+)\}/g

  // `placeholder` tapılan bütöv mətndir ('{n}'), `name` isə mötərizənin içidir ('n').
  function fillPlaceholder(placeholder: string, name: string): string {
    if (name in values) {
      return String(values[name])
    }
    return placeholder
  }

  // replace hər tapılan yer tutucu üçün fillPlaceholder-i çağırır və onun qaytardığını yerinə qoyur.
  // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/String/replace
  return template.replace(placeholderPattern, fillPlaceholder)
}

/**
 * Yalnız azərbaycanca olan səhifələr.
 *
 * Bloq azərbaycanca yazılır, 404.html isə bir dəfə yaradılır — ona görə /ru/bloq/ və /en/404/ yoxdur.
 * Məxfilik siyasəti və hesabın silinməsi səhifəsi Google Play-in link verdiyi mətnlərdir, onlar da
 * yalnız azərbaycancadır. Bu səhifələr üçün başqa dil təklif etmək kiçik kosmetik səhv deyil: dil
 * seçici oxucunu 404-ə göndərir, hreflang teqləri isə axtarış sistemlərini heç vaxt yaradılmamış
 * ünvanlara yönəldir.
 *
 * Səhifənin tərcüməsi olmayan kimi onu bura əlavə et, tərcüməsi hazır olanda isə buradan sil.
 */
const azOnlyRoutes = ['/bloq/', '/404/', '/mexfilik/', '/hesab-silinmesi/'] as const

/**
 * Səhifənin həqiqətən hansı dillərdə olduğunu qaytarır. Yuxarıdakı siyahıda olmayan hər səhifə üç
 * dildə var — ona görə standart cavab boş siyahı yox, tam siyahıdır.
 * Header.astro (dil seçici) və BaseLayout.astro (hreflang teqləri) bunu işlədir.
 */
export function availableLangs(path = '/'): Lang[] {
  for (const route of azOnlyRoutes) {
    // startsWith: '/bloq/' bloqun hər yazısını da tutur (məs. '/bloq/qrlog-ile-davamiyyet/').
    if (path.startsWith(route)) {
      return ['az']
    }
  }
  // Surət qaytarırıq: `languages` dəyişməzdir (as const), çağıran isə adi massiv alır.
  return [...languages]
}

/**
 * Səhifənin verilən dildəki ünvanını qurur — sadəcə dil prefiksi + yol:
 *   localizedPath('az', '/elaqe/') → '/elaqe/'
 *   localizedPath('ru', '/elaqe/') → '/ru/elaqe/'
 *   localizedPath('ru', '/')       → '/ru/'   (ana səhifə də eyni qaydaya düşür)
 *
 * Slug-lar (elaqe, haqqimizda) hər dildə azərbaycanca qalır — bilərəkdən: /haqqimizda/ artıq axtarış
 * sistemlərində indekslənib, slug-ı tərcümə etmək onu heç bir fayda olmadan sındırardı.
 */
export function localizedPath(lang: Lang, path = '/'): string {
  const prefix = localePrefix[lang]
  return `${prefix}${path}`
}
