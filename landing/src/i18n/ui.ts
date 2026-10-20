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
 * Məhsul ekranlarının içindəki mətn: «Rəhbərin bir günü» (AdminPanel*.astro) və «Saxtaya qarşı
 * qoruma» bölməsindəki panel parçası (AntiFraudPanel.astro).
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
  // Üzü referansla uyğun gəlməyən giriş: məhsulda qırmızı «⚠ Uyğunsuz 41%» nişanı
  // (frontend/src/components/FaceFlagBadge.tsx). AdminPanelToday və AntiFraudPanel işlədir.
  'panel.ui.mismatch': 'Uyğunsuz',
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
  // Giriş də, çıxış da olan gün. Admin lövhəsində «Tamamlayıb» yazılır, «Tamamlandı» yox
  // (frontend/src/components/StatusBadge.tsx). AntiFraudPanel də bu açarı işlədir.
  'panel.ui.m3.finished': 'Tamamlayıb',
  'panel.ui.m3.thName': 'İşçi',
  'panel.ui.m3.thRole': 'Vəzifə',
  'panel.ui.m3.thStatus': 'Status',
  'panel.ui.m3.thIn': 'Giriş',
  'panel.ui.m3.thOut': 'Çıxış',
  'panel.ui.m3.thFace': 'Üz',
  // «Saxtaya qarşı qoruma» — «Bugünkü davamiyyət» ekranından parça (AntiFraudPanel.astro).
  // Sütun başlıqları və «Tamamlayıb» yuxarıdakı m3 açarlarındandır.
  'panel.ui.af.title': 'Bugünkü davamiyyət',
  'panel.ui.af.branch': 'Baş ofis',
  'panel.ui.af.sample': 'Nümunə',
  'panel.ui.af.offline': 'oflayn',
  'panel.ui.af.manual': 'Əl ilə daxil edilib',
  'panel.ui.af.photo': 'Şəklə bax',
} as const

export const ui = {
  az: {
    'meta.title': 'QRLog — QR ilə işçi davamiyyəti sistemi | Azərbaycan',
    'meta.description':
      'QRLog — işçilərin telefonu ilə QR kodu skan edərək giriş-çıxışını qeydə alan davamiyyət sistemi. Hər giriş imzalı QR, GPS məkanı, cihaz və üz yoxlaması ilə təsdiqlənir.',
    'meta.keywords':
      'işçi davamiyyəti, QR davamiyyət, giriş çıxış sistemi, davamiyyət proqramı, GPS davamiyyət, iş vaxtı uçotu, QRLog, Azərbaycan',

    'nav.how': 'Necə işləyir',
    'nav.modules': 'Modullar',
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
    'hero.d.checkIn': 'Giriş',
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

    'stats.title': 'Rəqəmlərlə',
    'stats.sub': 'Real şirkətlərdə, hər gün işləyir',
    // Dörd rəqəmin izahı (Stats.astro). Mətnlər qəsdən qısadır: 1024px-də ən dar sütuna (~190px) bir sətirdə
    // sığmalıdır — uzatsan, hər üç dildə 1024px-də yoxla.
    'stats.s1': 'şirkət QRLog işlədir',
    'stats.s2': 'işçi telefonla giriş edir',
    'stats.s3': 'filialda QR poster asılıb',
    'stats.s4': 'giriş bir iş günündə',
    // Rəqəmlərin ekran oxuyucu üçün qısa adları: ekranda görünmür (<dt class="sr-only">).
    'stats.n1': 'Şirkət',
    'stats.n2': 'İşçi',
    'stats.n3': 'Filial',
    'stats.n4': 'Gündəlik giriş',
    // Sağ-yuxarıdakı nişanın mətni; arxasından ay və il gəlir: «QRLog bazasından · Oktyabr 2026».
    'stats.source': 'QRLog bazasından',
    // 10 000-dən böyük rəqəmin vahidi: «183» + « min» → «183 min+». Başdakı   BÖLÜNMƏYƏN boşluqdur
    // (adi boşluq yox): rəqəm və söz iki sətrə qırılmasın. İngiliscədə boşluq yoxdur: «183K+».
    'stats.thousand': ' min',

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

    // «Modullar» (Modules.astro): özü axan kart slayderi. Kartın mətni `mod.<kart>.name` və
    // `mod.<kart>.desc` açarlarındadır; kartların sırasını və nömrəsini Modules.astro-dakı `cards`
    // massivi müəyyən edir. Kartlarda teq yoxdur (ikon → ad → təsvir). Maaş kartı hələlik yoxdur —
    // maaş hesablaması məhsulda var, amma kart qaytarılanda cümlə yenidən yoxlanmalıdır
    // (bax: landing README «Məzmun qaydası»).
    'mod.eyebrow': 'Modullar', // həm də slayderin ekran oxuyucu adıdır (aria-label)
    'mod.title': 'Davamiyyətdən sonra da var',
    'mod.sub': 'Hamısı eyni paneldə — hər iş üçün ayrıca sistem lazım deyil.',
    // Splide-ın ekran oxuyucu mətnləri (özü ingiliscə yazır): «karusel», «slayd», «3 / 7».
    'mod.carousel': 'karusel',
    'mod.slide': 'slayd',
    'mod.slideLabel': '%s / %s',
    'mod.shift.name': 'Növbə qrafiki',
    'mod.shift.desc': 'Hər işçinin növbəsi əvvəlcədən müəyyən olunur.',
    'mod.leave.name': 'Məzuniyyət və icazə',
    'mod.leave.desc': 'Məzuniyyət, xəstəlik və icazə günləri qeyd olunur və hesabatlarda görünür.',
    // Ad admin paneldəki menyu adı ilə eynidir (frontend AdminLayout.tsx: «Sahə ziyarətləri»).
    'mod.field.name': 'Sahə ziyarətləri',
    'mod.field.desc': 'İşçiyə sahədə iş təyin edin, yoxlama siyahısı ilə nəticəyə baxın.',
    'mod.announce.name': 'Elanlar',
    'mod.announce.desc': 'Bütün komandaya bir mesajla çatın.',
    'mod.push.name': 'Push bildirişləri',
    'mod.push.desc': 'Növbə xatırlatmaları, elanlar və xəbərdarlıqlar işçinin telefonuna düşür.',
    'mod.branches.name': 'Çoxfilial və rollar',
    'mod.branches.desc': 'Bütün filiallar bir paneldə, hər rol yalnız lazım olanı görür.',
    'mod.kiosk.name': 'Kiosk rejimi',
    'mod.kiosk.desc': 'Girişdəki planşetdə QR özü yenilənir, işçi öz telefonu ilə skan edir.',

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

    // «Saxtaya qarşı qoruma» (AntiFraud.astro, AntiFraudPanel.astro). Hər iddia 2026-10-02-də məhsul
    // kodu ilə yoxlanılıb — mətni dəyişəndə kodda da yoxla. Ad, saat və faiz mətn deyil, komponentdə
    // data kimi durur: {t} — saat, {p} — faiz, fmt() doldurur. Panel parçasının mətni panelUi-dədir.
    'af.eyebrow': 'Saxtaya qarşı qoruma',
    'af.title': 'Hər giriş dörd qatda yoxlanılır',
    'af.sub':
      'Başqasının yerinə, evdən və ya posterin şəklindən giriş cəhdi ya rədd olunur, ya da rəhbərin panelində işarə ilə görünür. Yoxlamalar işçi skan edən kimi başlayır, nəticə adətən bir neçə saniyəyə hazır olur.',
    // Giriş qeydi kartı
    'af.rec.meta': 'Giriş · {t} · Baş ofis',
    'af.rec.pending': 'yoxlanılır',
    'af.rec.done': 'qeydə alındı',
    'af.rec.footer': 'Bir giriş · 4 yoxlama',
    'af.sample': 'Nümunə məlumat',
    // Kartın dörd sətri: başlıq (t) və dəyər (v)
    'af.r1.t': 'QR imzası',
    'af.r1.v': 'Baş ofis posteri',
    'af.r2.t': 'Cihaz',
    'af.r2.v': 'İşçiyə bağlı cihaz',
    'af.r3.t': 'Məkan (GPS)',
    'af.r3.v': 'Filial radiusunda',
    'af.r4.t': 'Üz uyğunluğu',
    'af.r4.v': 'Uyğunluq {p}%',
    // Dörd izah: başlıq (t), «Qarşısını alır: …» etiketinin dəyəri (p) və mətn (d)
    'af.prevents': 'Qarşısını alır',
    'af.c1.t': 'İmzalanmış poster',
    'af.c1.p': 'saxta kod',
    'af.c1.d':
      'Posterdəki kod imzalıdır və filiala bağlıdır: saxta kod qəbul edilmir, kod yalnız öz filialının ərazisində işləyir.',
    'af.c2.t': 'Cihaz bağlaması',
    'af.c2.p': 'başqasının yerinə',
    'af.c2.d':
      'İşçinin hesabı onun telefonuna bağlanır. Başqasına bağlı telefondan edilən giriş qəbul edilmir və Problemlər siyahısında telefonun sahibi ilə görünür.',
    'af.c3.t': 'GPS geo-hasar',
    'af.c3.p': 'evdən giriş',
    'af.c3.d':
      'Skan yalnız filialın təyin olunmuş radiusunda qəbul edilir. Evdən və ya yoldan giriş işləmir.',
    'af.c4.t': 'Üz yoxlaması',
    'af.c4.p': 'dost əvəzinə skan',
    'af.c4.d':
      'Girişdə çəkilən selfi referans şəkillə avtomatik müqayisə olunur. Dost əvəzinə edilən skan rəhbərə işarə ilə görünür.',
    // Aşağı blok: «İşçi qapıda ilişib qalmır»
    'af.door.t': 'İşçi qapıda ilişib qalmır',
    'af.door.d':
      'Şəkil alınmasa və ya üz uyğun gəlməsə, işçi bloklanmır — giriş yazılır, rəhbər onu paneldə görür. Radiusdan kənar skan isə qəbul edilmir; göndərilən skan səbəbi ilə Problemlər siyahısına düşür.',
    'af.rule1.t': 'Şəkil və ya üz',
    'af.rule1.d': 'Giriş yazılır; üz uyğun gəlməsə sətir işarələnir',
    'af.rule2.t': 'Radiusdan kənar',
    'af.rule2.d': 'Skan qəbul edilmir; göndərilən skan səbəbi ilə Problemlərə düşür',
    'af.rule3.t': 'İnternet yoxdur',
    'af.rule3.d': 'Skan telefonda saxlanılır, bağlantı gələndə göndərilir',
    'af.rule4.t': 'Əl ilə düzəliş',
    'af.rule4.d': 'Kimin etdiyi ilə birlikdə qeydə alınır',
    'af.privacy': 'Selfi yalnız davamiyyəti və şəxsiyyəti təsdiqləmək üçün işlədilir',

    // «Kimlər üçün» (Audience.astro): üç kart — site (obyektlər), chain (filial şəbəkəsi), office (ofis).
    // Kartlardan sonra vizualların içindəki yazılar gəlir (AudienceMap, AudienceBranches). Poster
    // şəklinin yazıları burada YOXDUR: o, paneldən çıxan real posterin şəklidir, poster isə məhsulda
    // yalnız azərbaycancadır (bax AudiencePoster.astro). `{n}/{m}` yer tutucularını fmt() doldurur.
    'aud.eyebrow': 'Kimlər üçün',
    'aud.title': 'Komandanız harada işləyir?',
    'aud.sub': 'Sahədə, filiallarda və ya bir binada — hər üçü eyni paneldən idarə olunur.',

    'aud.site.pill': 'Sahədə və obyektlərdə',
    'aud.site.title': 'Tikinti, təmizlik və logistika sahələrində',
    'aud.site.desc': 'Hər girişdə işçinin obyektin zonasında olduğu yoxlanılır.',
    'aud.site.b1': 'GPS ilə məkan yoxlaması',
    'aud.site.b2': 'Hər obyektə öz zonası və radiusu',
    'aud.site.b3': 'Zonadan kənar giriş qəbul olunmur',

    'aud.chain.pill': 'Çoxlu filial şəbəkələri',
    'aud.chain.title': 'Mağaza, restoran və aptek şəbəkələrində',
    'aud.chain.desc': 'Bütün filialların bugünkü vəziyyətini bir mərkəzdən izləyin.',
    'aud.chain.b1': 'Bütün filiallar bir paneldə',
    'aud.chain.b2': 'Filial müdiri yalnız ona aid filialların davamiyyətini görür',
    'aud.chain.b3': 'Kim işdədir, kim gəlməyib — canlı lövhədə',

    'aud.office.pill': 'Ofislər və mərkəzi binalar',
    'aud.office.title': 'Plaza, holdinq, klinika və təhsil mərkəzlərində',
    'aud.office.desc':
      'Girişə QRLog posteri asılır. Əməkdaşlar giriş və çıxışda onu öz telefonu ilə skan edir.',
    'aud.office.b1': 'Turniket, kart və barmaq izi lazım deyil',
    'aud.office.b2': 'Poster paneldən PDF kimi yüklənir və çap olunur',
    'aud.office.b3': 'Hər kəs öz telefonu ilə skan edir — növbə yaranmır',

    'aud.map.site': 'Tikinti obyekti №4',
    'aud.map.verified': 'GPS təsdiqləndi',
    'aud.map.radius': '150 m',
    'aud.map.coordsLabel': 'GPS koordinat',

    'aud.branches.n1': 'Gənclik',
    'aud.branches.n2': 'Yasamal',
    'aud.branches.n3': 'Nəsimi',
    'aud.branches.sub': 'Filial · Bakı',
    'aud.branches.status': '{n}/{m} işdə',

    'sec.eyebrow': 'Təhlükəsizlik',
    'sec.title': 'Məlumatlarınız qorunur',
    'sec.sub': 'Hər şirkətin məlumatı ayrıdır və yalnız görməli olan görür.',
    'sec.i1t': 'Şifrələnmiş bağlantı',
    'sec.i1d': 'Bütün trafik HTTPS üzərindən gedir; sertifikatlar avtomatik yenilənir.',
    'sec.i2t': 'Şirkətlər bir-birindən ayrıdır',
    'sec.i2d': 'Hər sorğu yalnız öz şirkətinin məlumatına çıxır. Şirkəti müəyyən olunmayan sorğu rədd edilir.',
    'sec.i3t': 'Hər gecə ehtiyat nüsxə',
    'sec.i3d':
      'Bazanın nüsxəsi hər gecə ayrıca anbara göndərilir və 90 gün saxlanılır. Bərpası hər həftə yoxlanılır.',
    'sec.i4t': 'Rol əsaslı giriş',
    'sec.i4d':
      'İşçi yalnız öz davamiyyətini, menecer ona aid filialların, admin isə bütün şirkətin davamiyyətini görür.',
    // Başlığın altındakı üç qısa fakt (kartlar deyil, sadə siyahı).
    'sec.e1': 'PIN-lər açıq mətnlə saxlanılmır',
    'sec.e2': 'Giriş şəkilləri 90 gündən sonra avtomatik silinir',
    'sec.e3': '8 səhv PIN cəhdindən sonra giriş 5 dəqiqə bağlanır',

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
      'Hər filialın GPS koordinatı və radiusu təyin olunur — ərazidən kənar skan standart olaraq qəbul edilmir. İşçinin hesabı öz telefonuna bağlanır, girişdə isə şəkil çəkilir və işçinin referans şəkli ilə avtomatik müqayisə olunur — uyğunsuzluq olsa, menecer görür.',
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
      'Android-də Google Play-dən yükləyin. App Store versiyası hazırlanır — o vaxta qədər iPhone-da QRLog brauzerdən tətbiq kimi işləyir.',
    // Aşağıdakı üç addım yalnız iPhone üçündür — Android-də tətbiq Google Play-dən gəlir.
    'pwa.steps': 'iPhone üçün — indi',
    'pwa.b1': 'Brauzerdə açın',
    'pwa.b2': 'Ana ekrana əlavə edin',
    'pwa.b3': 'Tətbiq kimi işlədin',
    // "App Store" düyməsinin üstündəki kiçik yazı: tətbiq hələ App Store-da yoxdur (Pwa.astro, Footer.astro).
    'pwa.soon': 'Tezliklə',
    // Telefonlu fotonun alt mətni (ekran oxuyucu üçün).
    'pwa.photoAlt': 'İşçi əlində telefon tutur: ekranda QRLog tətbiqinin «Bu gün» səhifəsi açıqdır',
    // Google Play nişanının alt mətni (GooglePlayBadge.astro) — linkin yeganə adıdır.
    'gplay.alt': 'Google Play-də yükləyin',

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

    'about.metaTitle': 'Haqqımızda — QRLog',
    'about.metaDesc':
      'QRLog — Azərbaycanda QR əsaslı işçi davamiyyəti sistemi. Davamiyyət uçotunu telefonla sadələşdiririk.',
    'about.eyebrow': 'Haqqımızda',
    'about.title.a': 'Davamiyyəti jurnaldan ',
    'about.title.hl': 'telefona',
    'about.title.b': ' köçürürük',
    'about.lead':
      'QRLog Azərbaycanda hazırlanır. Məqsədimiz sadədir: işçinin işə gəlib-getməsi kağızda və ya sözdə yox, etibarlı qeyddə qalsın.',
    'about.story.eyebrow': 'Hekayəmiz',
    'about.story.title': 'Bir şirkətin gündəlik ehtiyacından başladı',
    'about.story.p1':
      'QRLog-u sahədə işləyən böyük bir xidmət şirkəti üçün qurmağa başladıq. Şəhərin müxtəlif yerlərində onlarla ərazi, hər səhər kağız jurnal, ayın sonunda isə əl ilə yığılan tabel.',
    'about.story.p2a':
      'Hər əraziyə turniket qoymaq mümkün deyildi, jurnala da tam güvənmək olmurdu. Biz sadə bir fikirdən çıxış etdik: ',
    'about.story.p2b': 'işçinin onsuz da telefonu var.',
    'about.story.p2c': ' Divara bir QR poster asmaq kifayət etməlidir.',
    'about.story.p3':
      'Sistem ilk gündən real işçilərin real səhərlərində sınandı. İndi eyni üsulla başqa sahələrdən olan şirkətlər də işləyir.',
    'about.v.aria': 'Əvvəl kağız jurnal, indi QRLog paneli',
    'about.v.sample': 'Nümunə',
    'about.v.jTitle': 'DAVAMİYYƏT JURNALI',
    'about.v.jMeta': 'Ərazi № 3 · 14.10',
    'about.v.jName': 'Ad, soyad',
    'about.v.jIn': 'Gəldi',
    'about.v.jSign': 'İmza',
    'about.v.aTitle': 'Bu gün · Ərazi 3',
    'about.v.aDate': '14 oktyabr',
    'about.v.live': 'Canlı',
    'about.v.in': 'Giriş',
    'about.v.offline': 'oflayn',
    'about.v.noScan': 'Skan yoxdur',
    'about.v.sAtWork': 'İşdədir',
    'about.v.sFace': 'Üz uyğun deyil',
    'about.v.sAbsent': 'Gəlməyib',

    'contact.metaTitle': 'Əlaqə — QRLog',
    'contact.metaDesc':
      'QRLog ilə əlaqə saxlayın. Davamiyyət sistemi, qiymət və quraşdırma haqqında suallarınızı cavablandıraq.',
    'contact.phone': 'Telefon',
    'contact.email': 'E-poçt',
    'contact.whatsapp': 'WhatsApp',
    'contact.eyebrow': 'Əlaqə',
    'contact.title.a': 'Bir mesajla',
    'contact.title.hl': 'başlayaq',
    'contact.lead':
      'Şirkətin adını, filial və işçi sayını yazın. Sizə uyğun paketi və quraşdırma yolunu bir cavabda göndərək.',
    'contact.waLabel': 'WhatsApp · ən tez yol',
    'contact.waOpen': 'Çatı aç',
    'contact.instagram': 'Instagram',
    'contact.copyPhone': 'Nömrəni kopyala',
    'contact.copyEmail': 'Ünvanı kopyala',
    'contact.chatAria': 'Nümunə WhatsApp yazışması',
    'contact.chatOnline': 'onlayn',
    'contact.chatDay': 'Bu gün',
    'contact.chatSample': 'Nümunə yazışma',
    'contact.chatReply':
      'Salam! Şirkətin adını, filial və işçi sayını yazın, sizə uyğun təklifi göndərək.',
    'contact.chatMe': 'Bizim şirkət təmizlik xidməti göstərir:',
    'contact.chatL1': '3 filial',
    'contact.chatL2': 'təxminən 120 işçi',
    'contact.chatL3': 'işçilər sahədə də işləyir',
    'contact.chatInput': 'Mesaj',

    'del.metaTitle': 'Hesabın silinməsi — QRLog',
    'del.metaDesc':
      'QRLog davamiyyət tətbiqində hesabınızın və əlaqəli məlumatların silinməsini necə tələb edə bilərsiniz.',
    'del.eyebrow': 'Hesab',
    'del.title': 'Hesabın silinməsi',
    'del.lead':
      'QRLog davamiyyət tətbiqində hesabınızın və onunla bağlı məlumatların silinməsini necə tələb edə biləcəyinizi, nəyin silindiyini və nəyin saxlanıla biləcəyini burada izah edirik.',
    'del.chipApp': 'QRLog davamiyyət tətbiqi',
    'del.chip30': '30 gün ərzində emal olunur',
    'del.cardAria': 'Nümunə: işçinin şəxsi məlumatları silinir',
    'del.cardPhone': 'Telefon',
    'del.cardRole': 'Vəzifə',
    'del.cardRoleV': 'Ofis meneceri',
    'del.cardDevice': 'Cihaz',
    'del.cardDeviceV': 'iPhone 18 Pro · bağlı',
    'del.cardDone': 'Silindi',
    'del.howTitle': 'Silinməni necə tələb etmək olar',
    'del.howSub.a': 'QRLog hesabları işlədiyiniz ',
    'del.howSub.b': 'şirkət (işəgötürən)',
    'del.howSub.c': ' tərəfindən idarə olunur. Tələb üçün iki yol var.',
    'del.emailTitle': 'E-poçt ilə bizə yazın',
    'del.emailText':
      'ünvanına aşağıdakı mövzu ilə məktub göndərin. Hesabı tapmağımız üçün bu məlumatları yazın:',
    'del.tplSubjectL': 'Mövzu',
    'del.tplSubject': 'Hesab silinməsi tələbi',
    'del.tplBodyL': 'Mətndə',
    'del.tplName': 'Ad, soyad',
    'del.tplPhone': 'qeydiyyatdakı telefon nömrəsi',
    'del.tplOr': '(və ya e-poçt)',
    'del.tplCompany': 'işlədiyiniz şirkət',
    'del.emailBtn': 'E-poçt yaz',
    'del.copyBtn': 'Ünvanı kopyala',
    'del.copied': 'Kopyalandı',
    'del.workTitle': 'İş yeriniz vasitəsilə',
    'del.workText1':
      'Şirkətinizdə QRLog-u idarə edən şəxsə (adətən kadrlar şöbəsi və ya rəhbər) müraciət edin. O, hesabınızı admin paneldən deaktiv edə və ya tamamilə silə bilər.',
    'del.workText2':
      'Bu yol daha tezdir, çünki hesab birbaşa şirkətinizin panelindədir.',
    'del.stepsTitle': 'Tələbdən sonra nə olur',
    'del.whoYou': 'Siz',
    'del.whoUs': 'QRLog',
    'del.s1t': 'Tələb göndərirsiniz',
    'del.s1d':
      'E-poçtla bizə yazırsınız və ya şirkətinizin QRLog adminə müraciət edirsiniz.',
    'del.s2t': 'Yoxlayırıq',
    'del.s2d':
      'Hesabın həqiqətən sizə aid olduğunu təsdiqləyirik ki, başqası sizin adınızdan silinmə istəməsin.',
    'del.s3t': 'Silirik',
    'del.s3d.a': 'Təsdiqdən sonra ən gec ',
    'del.s3d.b': '30 gün',
    'del.s3d.c': ' ərzində şəxsi məlumatlarınız və şəkilləriniz silinir.',
    'del.listTitle': 'Nə silinir, nə qala bilər',
    'del.delTitle': 'Silinir',
    'del.d1': 'Ad, soyad, telefon nömrəsi, e-poçt, vəzifə',
    'del.d2': 'Referans şəkli və seçdiyiniz profil şəkli',
    'del.d3': 'Giriş və çıxış anındakı selfilər',
    'del.d3s': 'sahə ziyarətlərində çəkilən şəkillər də daxil',
    'del.d4': 'Cihaz məlumatı',
    'del.d4s': 'telefonunuzun hesaba bağlandığını göstərən "barmaq izi"',
    'del.d5': 'Giriş məlumatı',
    'del.d5s': 'PIN — şifrələnmiş formada saxlanılırdı',
    'del.keepTitle': 'Müəyyən müddət qala bilər',
    'del.keep1.a': 'Davamiyyət və maaş qeydləri',
    'del.keep1.b':
      ' (giriş-çıxış tarixçəsi) işəgötürən tərəfindən qanunun tələb etdiyi müddət ərzində saxlanıla bilər — əmək və mühasibat uçotu üçün.',
    'del.keep2.a': 'Bu müddət bitəndə həmin qeydlər ',
    'del.keep2.b': 'silinir və ya anonimləşdirilir',
    'del.keep2.c': ', yəni artıq konkret şəxslə əlaqələndirilə bilmir.',
    'del.note.a': 'Tətbiqin ',
    'del.note.b': '"Telefondakı hesablar"',
    'del.note.c':
      ' ekranındakı silmə düyməsi hesabı yalnız həmin telefonun yaddaşından çıxarır — hesabınız və qeydləriniz silinmir. Tam silinmə üçün yuxarıdakı yollardan birini seçin.',

    // Məxfilik siyasəti (PrivacyBody.astro, PermissionsCard.astro)
    'priv.metaTitle': 'Məxfilik siyasəti — QRLog',
    'priv.metaDesc': 'QRLog davamiyyət tətbiqinin məxfilik siyasəti: hansı məlumatları toplayır və necə istifadə edir.',
    'priv.eyebrow': 'Hüquqi',
    'priv.title': 'Məxfilik siyasəti',
    'priv.lead': 'QRLog davamiyyət tətbiqinin hansı məlumatları topladığını, nə üçün istifadə etdiyini və kimin görə bildiyini sadə dillə izah edirik. Tətbiq QRLog-dan istifadə edən şirkətlərin işçiləri üçündür.',
    'priv.chipDate': 'Qüvvəyə minmə tarixi: 29 iyul 2026',
    'priv.chipRead': 'Oxuma vaxtı: ~4 dəqiqə',
    'priv.permTitle': 'Tətbiqin istədiyi icazələr',
    'priv.permAria': 'Tətbiqin istədiyi icazələr: kamera və yer yalnız skan anında, bildirişlər istəyə görə',
    'priv.permCam': 'Kamera',
    'priv.permGps': 'Yer (GPS)',
    'priv.permPush': 'Bildirişlər',
    'priv.permOnScan': 'Yalnız skan anında',
    'priv.permOptional': 'İstəyə görə',
    'priv.tldrTitle': 'Qısaca',
    'priv.t1t': 'Gün ərzində izləmirik',
    'priv.t1d': 'Yer yalnız QR skan etdiyiniz an yoxlanılır. Arxa fonda yer məlumatı yığılmır.',
    'priv.t2t': 'Reklam yoxdur',
    'priv.t2d': 'Məlumatlarınız reklam üçün istifadə olunmur və heç kimə satılmır.',
    'priv.t3t': 'Şirkətiniz idarə edir',
    'priv.t3d': 'Məlumatların sahibi işlədiyiniz şirkətdir. Başqa şirkətlər onları görmür.',
    'priv.t4t': 'Silinməni istəyə bilərsiniz',
    'priv.t4d': 'Hesabınızın və məlumatlarınızın silinməsini istənilən vaxt tələb edə bilərsiniz.',
    'priv.toc': 'Mündəricat',
    'priv.tocAria': 'Mündəricat',
    'priv.intro.a': 'Bu siyasət ',
    'priv.intro.b': ' davamiyyət tətbiqinin (bundan sonra "Tətbiq") hansı məlumatları topladığını və onlardan necə istifadə etdiyini izah edir.',
    'priv.s1': 'Məlumatların idarəçisi',
    'priv.ctrlLabel': 'İdarəçi (data controller)',
    'priv.ctrlName': 'İşlədiyiniz şirkət',
    'priv.ctrlText': 'Hansı məlumatın nə üçün toplanacağına şirkətiniz qərar verir.',
    'priv.procLabel': 'Operator (processor)',
    'priv.procText': 'Məlumatları şirkətinizin adından və onun tapşırığı ilə emal edirik.',
    'priv.s1p': 'Məlumatlarınızla bağlı sualları əvvəlcə iş yerinizə (işəgötürənə) ünvanlaya bilərsiniz.',
    'priv.s2': 'Topladığımız məlumatlar',
    'priv.colData': 'Məlumat',
    'priv.colWhy': 'Nə üçün',
    'priv.d1t': 'Şəxsi və iş məlumatı',
    'priv.d1w': 'Ad, soyad, telefon və/və ya e-poçt, vəzifə, filial',
    'priv.d1y': 'Hesabı yaratmaq və davamiyyəti doğru işçiyə yazmaq üçün.',
    'priv.d2t': 'Giriş məlumatı',
    'priv.d2w': 'PIN — yalnız şifrələnmiş (heş) formada',
    'priv.d2y': 'Hesaba yalnız sizin daxil olmağınız üçün. Açıq mətndə heç yerdə saxlanmır.',
    'priv.d3t': 'Yer (GPS)',
    'priv.d3w': 'Yalnız QR skan etdiyiniz an',
    'priv.d3y': 'İş yerinin icazə verilən radiusunda olduğunuzu yoxlamaq üçün.',
    'priv.d4t': 'Giriş şəkli (selfie)',
    'priv.d4w': 'Giriş/çıxış anında çəkilən şəkil; sahə ziyarətində iş şəkli',
    'priv.d4y': 'Girişin sizin tərəfinizdən edildiyini təsdiqləmək üçün.',
    'priv.d5t': 'Profil şəkli',
    'priv.optional': 'istəyə görə',
    'priv.d5w': 'Yalnız özünüz əlavə etsəniz',
    'priv.d5y': 'Profildə göstərmək üçün. Heç nə ilə müqayisə olunmur, istənilən vaxt silə bilərsiniz.',
    'priv.d6t': 'Cihaz məlumatı',
    'priv.d6w': 'Cihazın "barmaq izi" (brauzer identifikatoru)',
    'priv.d6y': 'Başqasının sizin yerinizə giriş etməsinin qarşısını almaq üçün.',
    'priv.d7t': 'İş vaxtı və istifadə',
    'priv.d7w': 'Giriş-çıxış saatları, icazə və məzuniyyət qeydləri, tətbiqi son açma vaxtı',
    'priv.d7y': 'Davamiyyət hesabatı və tabel üçün.',
    'priv.d8t': 'Əməkhaqqı məlumatı',
    'priv.d8w': 'Şirkətiniz daxil edibsə',
    'priv.d8y': 'Davamiyyətə görə əməkhaqqı hesabatı üçün.',
    'priv.photoNote.a': 'Şəkil çəkilməsi sizi narahat edirsə, rəhbərinizlə danışın: şəkil tələbi ayrı-ayrı işçilər üçün söndürülə bilər. Bu halda giriş yenə ',
    'priv.photoNote.b': 'yer və cihaz yoxlaması',
    'priv.photoNote.c': ' ilə qeydə alınır.',
    'priv.s3': 'Necə istifadə olunur',
    'priv.u1': 'Davamiyyətin (işə giriş-çıxış) qeydi və hesabatı',
    'priv.u2': 'Düzgün iş yerində olduğunuzun yoxlanması (geofence)',
    'priv.u3': 'Şəkillə şəxsiyyətin təsdiqi (üz müqayisəsi) və saxtakarlığın qarşısının alınması',
    'priv.u4': 'Növbə və çıxış bildirişlərinin göndərilməsi',
    'priv.s3p': 'Məlumatlarınızı yalnız işlədiyiniz şirkətin rəhbərliyi və filialınıza baxan menecer görür. Başqa şirkətlər sizin məlumatlarınızı görmür.',
    'priv.s4': 'Texniki xidmətlər',
    'priv.s4p': 'Tətbiqin işləməsi üçün məlumatlar aşağıdakı texniki xidmətlər vasitəsilə emal olunur. Onlar məlumatı yalnız QRLog adından emal edir.',
    'priv.v1': 'Şəkillərin saxlanması',
    'priv.v2': 'Üz müqayisəsi',
    'priv.v3': 'Push bildirişləri',
    'priv.v4': '«Süni intellekt köməkçisi» çatının cavabları. Çata yalnız yazdığınız mesaj və öz davamiyyət göstəriciləriniz gedir; şəkillər getmir.',
    'priv.v5t': 'Hostinq provayderi',
    'priv.v5': 'Serverlərin yerləşdiyi məlumat mərkəzi',
    'priv.s5': 'Saxlanma və təhlükəsizlik',
    'priv.k1t': 'Saxlanma',
    'priv.k1d.a': 'Giriş-çıxış selfiləri ',
    'priv.k1d.b': '90 gündən sonra avtomatik silinir',
    'priv.k1d.c': '. Davamiyyət qeydləri, referans, profil və iş şəkilləri əmək münasibətləri davam etdiyi müddətdə və qanunun tələb etdiyi müddət ərzində saxlanılır.',
    'priv.k2t': 'Bağlananda',
    'priv.k2d': 'Şirkət hesabı bağlandıqda məlumatlar müvafiq qaydada silinir və ya anonimləşdirilir.',
    'priv.k3t': 'Qoruma',
    'priv.k3d': 'Bütün ötürülmə şifrələnir (HTTPS/TLS). PIN açıq mətndə saxlanmır. Giriş şirkət daxilində rollara görə məhdudlaşdırılır.',
    'priv.s6': 'Hüquqlarınız',
    'priv.r1': 'Baxmaq',
    'priv.r2': 'Düzəltmək',
    'priv.r3': 'Silinməsini tələb etmək',
    'priv.s6p': 'Bu hüquqlardan istifadə etmək üçün əvvəlcə iş yerinizə, sonra bizə müraciət edin: ',
    'priv.delBtn': 'Hesabın silinməsi qaydası',
    'priv.s7': 'Uşaqlar və dəyişikliklər',
    'priv.kidsL': 'Uşaqlar',
    'priv.kidsT': '16 yaşdan kiçiklər üçün deyil',
    'priv.kidsD': 'Tətbiq uşaqlar üçün nəzərdə tutulmayıb və 16 yaşından kiçiklərdən bilərəkdən məlumat toplamırıq.',
    'priv.chgL': 'Dəyişikliklər',
    'priv.chgT': 'Bu səhifədə dərc olunur',
    'priv.chgD': 'Siyasət yenilənə bilər. Yenilənmə bu səhifədə dərc olunacaq və yuxarıdakı tarix dəyişəcək.',

    'blog.title': 'Bloq',
    'blog.sub': 'Davamiyyət və QR sistemləri haqqında məqalələr.',
    'blog.metaTitle': 'Bloq — QRLog',
    'blog.metaDesc':
      'İşçi davamiyyəti, QR sistemləri və uçotun rəqəmsallaşdırılması haqqında məqalələr.',
    'blog.empty': 'Tezliklə ilk məqalələr burada olacaq.',
    'blog.back': 'Bütün yazılar',
    'blog.hero.title.a': 'Davamiyyət, komanda və ',
    'blog.hero.title.hl': 'iş günü',
    'blog.hero.title.b': ' haqqında',
    'blog.hero.lead': 'Rəhbərlər və HR üçün praktik yazılar: işçi girişini necə izləmək, saxta qeydiyyatın qarşısını almaq və QRLog-dan tam istifadə etmək.',
    'blog.hero.imgAlt': 'Skan xətti keçən QR kod və giriş təsdiqi',
    'blog.cats.aria': 'Kateqoriyalar',
    'blog.cat.all': 'Hamısı',
    'blog.cat.davamiyyet': 'Davamiyyət',
    'blog.cat.hr': 'HR və idarəetmə',
    'blog.cat.telimat': 'Təlimatlar',
    'blog.cat.yenilikler': 'Yeniliklər',
    'blog.featured': 'Seçilmiş',
    'blog.read': 'Oxu',
    'blog.readTime': '{n} dəq oxu',
    'blog.search.placeholder': 'Məqalə axtar...',
    'blog.search.label': 'Məqalə axtar',
    'blog.search.clear': 'Axtarışı təmizlə',
    'blog.search.results': '{n} nəticə',
    'blog.search.query': '«{q}»',
    'blog.search.emptyTitle': '«{q}» üçün məqalə tapılmadı',
    'blog.search.emptyHint': 'Başqa söz yoxlayın və ya axtarışı təmizləyin.',
    'blog.search.reset': 'Axtarışı təmizlə',
    'blog.search.widen': 'Bütün kateqoriyalarda axtar',
    'blog.crumb.aria': 'Naviqasiya yolu',
    'blog.crumb.home': 'Ana səhifə',
    'blog.summary.label': 'Qısaca',
    'blog.share.title': 'Paylaş',
    'blog.share.copy': 'Linki kopyala',
    'blog.share.copied': 'Kopyalandı',
    'blog.share.whatsapp': 'WhatsApp-da paylaş',
    'blog.related.eyebrow': 'Davam edin',
    'blog.related.title': 'Bunları da oxuyun',
    'blog.cta.title': 'QRLog sizin komandada necə işləyəcək?',
    'blog.cta.text': 'Suallarınızı WhatsApp-da yazın, cavab verək.',
    'blog.cta.button': 'WhatsApp-da yazın',

    'nf.title': 'Səhifə tapılmadı',
    'nf.sub':
      'Axtardığınız səhifə köçürülüb və ya heç vaxt olmayıb. Ola bilsin, link köhnəlib və ya ünvanda hərf səhvi var.',
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

    // BackToTop — WhatsApp düyməsinin üstündəki "yuxarı" düyməsi. Görünən yazısı yoxdur, bu mətni
    // yalnız ekran oxuyucusu oxuyur (aria-label).
    'top.aria': 'Səhifənin əvvəlinə qayıt',
  },

  ru: {
    'meta.title': 'QRLog — учёт посещаемости сотрудников по QR | Азербайджан',
    'meta.description':
      'QRLog — система учёта посещаемости: сотрудник сканирует QR-код своим телефоном, и вход-выход фиксируется. Каждая отметка подтверждается подписью QR, GPS, устройством и проверкой лица.',
    'meta.keywords':
      'учёт посещаемости, посещаемость сотрудников, QR учёт рабочего времени, приход уход сотрудников, GPS контроль, QRLog, Азербайджан',

    'nav.how': 'Как это работает',
    'nav.modules': 'Модули',
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
    'hero.d.checkIn': 'Приход',
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

    'stats.title': 'В цифрах',
    'stats.sub': 'Каждый день — в реальных компаниях',
    'stats.s1': 'компаний в QRLog',
    'stats.s2': 'сотрудников отмечаются',
    'stats.s3': 'филиалов с QR-постером',
    'stats.s4': 'отметок за рабочий день',
    'stats.n1': 'Компании',
    'stats.n2': 'Сотрудники',
    'stats.n3': 'Филиалы',
    'stats.n4': 'Отметок за день',
    'stats.source': 'Из базы QRLog',
    // «183» + « тыс.» → «183 тыс.+»;   — неразрывный пробел (см. az).
    'stats.thousand': ' тыс.',

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

    'mod.eyebrow': 'Модули',
    'mod.title': 'Не только посещаемость',
    'mod.sub': 'Всё в одной панели — отдельная система под каждую задачу не нужна.',
    'mod.carousel': 'карусель',
    'mod.slide': 'слайд',
    'mod.slideLabel': '%s / %s',
    'mod.shift.name': 'График смен',
    'mod.shift.desc': 'Смена каждого сотрудника задаётся заранее.',
    'mod.leave.name': 'Отпуска и отгулы',
    'mod.leave.desc': 'Отпуска, больничные и отгулы фиксируются и видны в отчётах.',
    // Admin panel yalnız azərbaycancadır, onun RU/EN menyu adı yoxdur. Ona görə RU və EN adı
    // əvvəlcədən razılaşdırılmış variantdır: «Полевые задания» / “Field tasks”.
    'mod.field.name': 'Полевые задания',
    'mod.field.desc': 'Назначайте сотруднику работу на объекте и проверяйте результат по чек-листу.',
    'mod.announce.name': 'Объявления',
    'mod.announce.desc': 'Одно сообщение — и вся команда в курсе.',
    'mod.push.name': 'Push-уведомления',
    'mod.push.desc': 'Напоминания о сменах, объявления и предупреждения приходят на телефон сотрудника.',
    'mod.branches.name': 'Филиалы и роли',
    'mod.branches.desc': 'Все филиалы в одной панели, каждая роль видит только своё.',
    'mod.kiosk.name': 'Режим киоска',
    'mod.kiosk.desc': 'QR на планшете у входа обновляется сам — сотрудник сканирует его своим телефоном.',

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

    // «Saxtaya qarşı qoruma». «Problemlər» azərbaycanca qalır — panelin menyusunun adıdır.
    'af.eyebrow': 'Защита от подлога',
    'af.title': 'Каждая отметка проходит четыре проверки',
    'af.sub':
      'Попытка отметиться за другого, из дома или по фото постера либо отклоняется, либо появляется в панели руководителя с пометкой. Проверки начинаются в момент сканирования, результат обычно готов через несколько секунд.',
    'af.rec.meta': 'Вход · {t} · Головной офис',
    'af.rec.pending': 'проверка',
    'af.rec.done': 'записано',
    'af.rec.footer': 'Одна отметка · 4 проверки',
    'af.sample': 'Пример данных',
    'af.r1.t': 'Подпись QR',
    'af.r1.v': 'Постер головного офиса',
    'af.r2.t': 'Устройство',
    'af.r2.v': 'Привязанное устройство',
    'af.r3.t': 'Место (GPS)',
    'af.r3.v': 'В радиусе филиала',
    'af.r4.t': 'Совпадение лица',
    'af.r4.v': 'Совпадение {p}%',
    'af.prevents': 'Предотвращает',
    'af.c1.t': 'Подписанный постер',
    'af.c1.p': 'поддельный код',
    'af.c1.d':
      'Код на постере подписан и привязан к филиалу: поддельный код не принимается, а работает код только на территории своего филиала.',
    'af.c2.t': 'Привязка устройства',
    'af.c2.p': 'отметку за другого',
    'af.c2.d':
      'Аккаунт сотрудника привязывается к его телефону. Отметка с телефона, привязанного к другому человеку, отклоняется и появляется в «Problemlər» с именем владельца.',
    'af.c3.t': 'GPS-геозона',
    'af.c3.p': 'отметку из дома',
    'af.c3.d':
      'Скан принимается только в заданном радиусе филиала. Отметиться из дома или по дороге не получится.',
    'af.c4.t': 'Проверка лица',
    'af.c4.p': 'скан «за друга»',
    'af.c4.d':
      'Селфи при входе автоматически сравнивается с эталонным фото. Скан «за друга» руководитель увидит с пометкой.',
    'af.door.t': 'Сотрудник не застрянет у двери',
    'af.door.d':
      'Если фото не получилось или лицо не совпало, сотрудник не блокируется — вход записывается, и руководитель видит его в панели. Скан за пределами радиуса не принимается; отправленный скан попадает в «Problemlər» с причиной.',
    'af.rule1.t': 'Фото или лицо',
    'af.rule1.d': 'Вход записывается; при несовпадении лица строка отмечается',
    'af.rule2.t': 'Вне радиуса',
    'af.rule2.d': 'Скан отклоняется; отправленный скан попадает в «Problemlər» с причиной',
    'af.rule3.t': 'Нет интернета',
    'af.rule3.d': 'Скан сохраняется в телефоне и отправляется, когда появится связь',
    'af.rule4.t': 'Ручная правка',
    'af.rule4.d': 'Фиксируется вместе с именем автора',
    'af.privacy': 'Селфи используется только для подтверждения посещаемости и личности',

    'aud.eyebrow': 'Для кого',
    'aud.title': 'Где работает ваша команда?',
    'aud.sub': 'На объектах, в филиалах или в одном здании — всё управляется из одной панели.',

    'aud.site.pill': 'На объектах',
    'aud.site.title': 'В строительстве, клининге и логистике',
    'aud.site.desc': 'При каждой отметке проверяется, что сотрудник находится в зоне объекта.',
    'aud.site.b1': 'Проверка местоположения по GPS',
    'aud.site.b2': 'У каждого объекта своя зона и радиус',
    'aud.site.b3': 'Отметка вне зоны не принимается',

    'aud.chain.pill': 'Сети с филиалами',
    'aud.chain.title': 'В сетях магазинов, ресторанов и аптек',
    'aud.chain.desc': 'Следите за всеми филиалами из одного центра.',
    'aud.chain.b1': 'Все филиалы в одной панели',
    'aud.chain.b2': 'Управляющий видит посещаемость только своих филиалов',
    'aud.chain.b3': 'Кто на месте, а кто не пришёл — на живом табло',

    'aud.office.pill': 'Офисы и центральные здания',
    'aud.office.title': 'В бизнес-центрах, холдингах, клиниках и учебных центрах',
    'aud.office.desc':
      'У входа висит постер QRLog. Сотрудники сканируют его своим телефоном при входе и выходе.',
    'aud.office.b1': 'Не нужны турникеты, карты и отпечатки пальцев',
    'aud.office.b2': 'Постер скачивается из панели в PDF и печатается',
    'aud.office.b3': 'Каждый сканирует своим телефоном — без очередей',

    'aud.map.site': 'Стройобъект №4',
    'aud.map.verified': 'GPS подтверждён',
    'aud.map.radius': '150 м',
    'aud.map.coordsLabel': 'GPS-координаты',

    'aud.branches.n1': 'Гянджлик',
    'aud.branches.n2': 'Ясамал',
    'aud.branches.n3': 'Насими',
    'aud.branches.sub': 'Филиал · Баку',
    'aud.branches.status': '{n}/{m} на месте',

    'sec.eyebrow': 'Безопасность',
    'sec.title': 'Ваши данные защищены',
    'sec.sub': 'Данные каждой компании изолированы, и каждый видит только своё.',
    'sec.i1t': 'Шифрованное соединение',
    'sec.i1d': 'Весь трафик идёт по HTTPS; сертификаты обновляются автоматически.',
    'sec.i2t': 'Компании изолированы друг от друга',
    'sec.i2d': 'Каждый запрос видит данные только своей компании. Запрос без определённой компании отклоняется.',
    'sec.i3t': 'Резервная копия каждую ночь',
    'sec.i3d':
      'Каждую ночь копия базы отправляется в отдельное хранилище и хранится 90 дней. Восстановление проверяется каждую неделю.',
    'sec.i4t': 'Доступ по ролям',
    'sec.i4d': 'Сотрудник видит только свою посещаемость, менеджер — своих филиалов, админ — всей компании.',
    'sec.e1': 'PIN-коды не хранятся в открытом виде',
    'sec.e2': 'Фото отметок автоматически удаляются через 90 дней',
    'sec.e3': 'После 8 неверных попыток ввода PIN вход блокируется на 5 минут',

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
      'Для каждого филиала задаются GPS-координаты и радиус — скан за пределами зоны по умолчанию не принимается. Аккаунт сотрудника привязывается к его телефону, а при отметке делается фото и автоматически сверяется с эталонным снимком сотрудника — если есть несоответствие, его увидит менеджер.',
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
      'На Android — установите из Google Play. Версия для App Store готовится — а пока на iPhone QRLog работает из браузера как приложение.',
    'pwa.steps': 'Для iPhone — сейчас',
    'pwa.b1': 'Откройте в браузере',
    'pwa.b2': 'Добавьте на главный экран',
    'pwa.b3': 'Пользуйтесь как приложением',
    'pwa.soon': 'Скоро',
    'pwa.photoAlt': 'Сотрудник держит телефон: на экране открыт раздел «Сегодня» приложения QRLog',
    'gplay.alt': 'Доступно в Google Play',

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

    'about.metaTitle': 'О нас — QRLog',
    'about.metaDesc':
      'QRLog — система учёта посещаемости сотрудников по QR в Азербайджане. Упрощаем учёт с помощью телефона.',
    'about.eyebrow': 'О нас',
    'about.title.a': 'Переносим учёт посещаемости из журнала ',
    'about.title.hl': 'в телефон',
    'about.title.b': '',
    'about.lead':
      'QRLog создаётся в Азербайджане. Наша цель проста: приход и уход сотрудника должны оставаться не на бумаге и не на словах, а в надёжной записи.',
    'about.story.eyebrow': 'Наша история',
    'about.story.title': 'Всё началось с ежедневной задачи одной компании',
    'about.story.p1':
      'Мы начали создавать QRLog для крупной сервисной компании, работающей «в поле». Десятки участков по всему городу, бумажный журнал каждое утро и табель, который в конце месяца собирают вручную.',
    'about.story.p2a':
      'Поставить турникет на каждом участке было невозможно, а журналу нельзя было полностью доверять. Мы исходили из простой мысли: ',
    'about.story.p2b': 'у сотрудника уже есть телефон.',
    'about.story.p2c': ' Должно хватать одного QR-постера на стене.',
    'about.story.p3':
      'С первого дня система проверялась на реальных сотрудниках в реальные утра. Сегодня так же работают компании и из других сфер.',
    'about.v.aria': 'Раньше бумажный журнал, теперь панель QRLog',
    'about.v.sample': 'Пример',
    'about.v.jTitle': 'ЖУРНАЛ ПОСЕЩАЕМОСТИ',
    'about.v.jMeta': 'Участок № 3 · 14.10',
    'about.v.jName': 'Ф. И.',
    'about.v.jIn': 'Пришёл',
    'about.v.jSign': 'Подпись',
    'about.v.aTitle': 'Сегодня · Участок 3',
    'about.v.aDate': '14 октября',
    'about.v.live': 'Онлайн',
    'about.v.in': 'Вход',
    'about.v.offline': 'офлайн',
    'about.v.noScan': 'Нет скана',
    'about.v.sAtWork': 'На работе',
    'about.v.sFace': 'Лицо не совпало',
    'about.v.sAbsent': 'Не пришёл',

    'contact.metaTitle': 'Контакты — QRLog',
    'contact.metaDesc':
      'Свяжитесь с QRLog. Ответим на вопросы о системе учёта посещаемости, ценах и внедрении.',
    'contact.phone': 'Телефон',
    'contact.email': 'E-mail',
    'contact.whatsapp': 'WhatsApp',
    'contact.eyebrow': 'Контакты',
    'contact.title.a': 'Начнём с одного',
    'contact.title.hl': 'сообщения',
    'contact.lead':
      'Напишите название компании, число филиалов и сотрудников. Мы пришлём подходящий пакет и план внедрения одним ответом.',
    'contact.waLabel': 'WhatsApp · быстрее всего',
    'contact.waOpen': 'Открыть чат',
    'contact.instagram': 'Instagram',
    'contact.copyPhone': 'Скопировать номер',
    'contact.copyEmail': 'Скопировать адрес',
    'contact.chatAria': 'Пример переписки в WhatsApp',
    'contact.chatOnline': 'в сети',
    'contact.chatDay': 'Сегодня',
    'contact.chatSample': 'Пример переписки',
    'contact.chatReply':
      'Здравствуйте! Напишите название компании, число филиалов и сотрудников — пришлём подходящее предложение.',
    'contact.chatMe': 'Наша компания занимается клинингом:',
    'contact.chatL1': '3 филиала',
    'contact.chatL2': 'около 120 сотрудников',
    'contact.chatL3': 'сотрудники работают и на выезде',
    'contact.chatInput': 'Сообщение',

    'del.metaTitle': 'Удаление аккаунта — QRLog',
    'del.metaDesc':
      'Как запросить удаление аккаунта и связанных данных в приложении учёта посещаемости QRLog.',
    'del.eyebrow': 'Аккаунт',
    'del.title': 'Удаление аккаунта',
    'del.lead':
      'Здесь мы объясняем, как запросить удаление аккаунта и связанных с ним данных в приложении QRLog, что удаляется и что может храниться.',
    'del.chipApp': 'Приложение учёта посещаемости QRLog',
    'del.chip30': 'Обрабатывается в течение 30 дней',
    'del.cardAria': 'Пример: личные данные сотрудника удаляются',
    'del.cardPhone': 'Телефон',
    'del.cardRole': 'Должность',
    'del.cardRoleV': 'Офис-менеджер',
    'del.cardDevice': 'Устройство',
    'del.cardDeviceV': 'iPhone 18 Pro · привязан',
    'del.cardDone': 'Удалено',
    'del.howTitle': 'Как запросить удаление',
    'del.howSub.a': 'Аккаунтами QRLog управляет ',
    'del.howSub.b': 'компания (работодатель)',
    'del.howSub.c':
      ', в которой вы работаете. Запрос можно отправить двумя способами.',
    'del.emailTitle': 'Напишите нам на e-mail',
    'del.emailText':
      '— отправьте письмо с темой ниже. Чтобы мы нашли аккаунт, укажите:',
    'del.tplSubjectL': 'Тема',
    'del.tplSubject': 'Запрос на удаление аккаунта',
    'del.tplBodyL': 'В письме',
    'del.tplName': 'Имя и фамилия',
    'del.tplPhone': 'номер телефона из аккаунта',
    'del.tplOr': '(или e-mail)',
    'del.tplCompany': 'компания, где вы работаете',
    'del.emailBtn': 'Написать письмо',
    'del.copyBtn': 'Скопировать адрес',
    'del.copied': 'Скопировано',
    'del.workTitle': 'Через работодателя',
    'del.workText1':
      'Обратитесь к тому, кто управляет QRLog в вашей компании (обычно отдел кадров или руководитель). Он может деактивировать или полностью удалить ваш аккаунт в админ-панели.',
    'del.workText2':
      'Так быстрее: аккаунт находится прямо в панели вашей компании.',
    'del.stepsTitle': 'Что происходит после запроса',
    'del.whoYou': 'Вы',
    'del.whoUs': 'QRLog',
    'del.s1t': 'Вы отправляете запрос',
    'del.s1d':
      'Пишете нам на e-mail или обращаетесь к администратору QRLog в вашей компании.',
    'del.s2t': 'Проверяем',
    'del.s2d':
      'Подтверждаем, что аккаунт действительно ваш, чтобы никто не запросил удаление от вашего имени.',
    'del.s3t': 'Удаляем',
    'del.s3d.a': 'После подтверждения — не позднее ',
    'del.s3d.b': '30 дней',
    'del.s3d.c': ' удаляются ваши личные данные и фотографии.',
    'del.listTitle': 'Что удаляется и что может остаться',
    'del.delTitle': 'Удаляется',
    'del.d1': 'Имя, фамилия, телефон, e-mail, должность',
    'del.d2': 'Эталонное фото и выбранное вами фото профиля',
    'del.d3': 'Селфи при входе и выходе',
    'del.d3s': 'включая фото с выездов',
    'del.d4': 'Данные устройства',
    'del.d4s': '«отпечаток», которым телефон привязан к аккаунту',
    'del.d5': 'Данные для входа',
    'del.d5s': 'PIN — хранился в зашифрованном виде',
    'del.keepTitle': 'Может храниться какое-то время',
    'del.keep1.a': 'Записи о посещаемости и зарплате',
    'del.keep1.b':
      ' (история входов и выходов) работодатель может хранить в течение срока, установленного законом, — для трудового и бухгалтерского учёта.',
    'del.keep2.a': 'Когда этот срок истекает, записи ',
    'del.keep2.b': 'удаляются или обезличиваются',
    'del.keep2.c': ', то есть их больше нельзя связать с конкретным человеком.',
    'del.note.a': 'Кнопка удаления на экране ',
    'del.note.b': '«Telefondakı hesablar» (аккаунты на телефоне)',
    'del.note.c':
      ' убирает аккаунт только из памяти этого телефона — сам аккаунт и записи не удаляются. Для полного удаления выберите один из способов выше.',

    // Məxfilik siyasəti (PrivacyBody.astro, PermissionsCard.astro)
    'priv.metaTitle': 'Политика конфиденциальности — QRLog',
    'priv.metaDesc': 'Политика конфиденциальности приложения QRLog: какие данные собираются и как используются.',
    'priv.eyebrow': 'Правовая информация',
    'priv.title': 'Политика конфиденциальности',
    'priv.lead': 'Простым языком объясняем, какие данные собирает приложение QRLog, зачем и кто может их видеть. Приложение предназначено для сотрудников компаний, использующих QRLog.',
    'priv.chipDate': 'Вступает в силу: 29 июля 2026',
    'priv.chipRead': 'Время чтения: ~4 минуты',
    'priv.permTitle': 'Разрешения приложения',
    'priv.permAria': 'Разрешения приложения: камера и геолокация — только при сканировании, уведомления — по желанию',
    'priv.permCam': 'Камера',
    'priv.permGps': 'Геолокация (GPS)',
    'priv.permPush': 'Уведомления',
    'priv.permOnScan': 'Только при сканировании',
    'priv.permOptional': 'По желанию',
    'priv.tldrTitle': 'Коротко',
    'priv.t1t': 'Не отслеживаем в течение дня',
    'priv.t1d': 'Геолокация проверяется только в момент сканирования QR. В фоне она не собирается.',
    'priv.t2t': 'Без рекламы',
    'priv.t2d': 'Ваши данные не используются для рекламы и никому не продаются.',
    'priv.t3t': 'Данными управляет ваша компания',
    'priv.t3d': 'Владелец данных — компания, в которой вы работаете. Другие компании их не видят.',
    'priv.t4t': 'Можно запросить удаление',
    'priv.t4d': 'Вы можете в любой момент запросить удаление аккаунта и данных.',
    'priv.toc': 'Содержание',
    'priv.tocAria': 'Содержание',
    'priv.intro.a': 'Эта политика объясняет, какие данные собирает приложение ',
    'priv.intro.b': ' (далее — «Приложение») и как оно их использует.',
    'priv.s1': 'Кто управляет данными',
    'priv.ctrlLabel': 'Контролёр (data controller)',
    'priv.ctrlName': 'Ваш работодатель',
    'priv.ctrlText': 'Компания решает, какие данные и для чего собираются.',
    'priv.procLabel': 'Обработчик (processor)',
    'priv.procText': 'Мы обрабатываем данные от имени и по поручению вашей компании.',
    'priv.s1p': 'С вопросами о данных сначала обращайтесь к работодателю.',
    'priv.s2': 'Какие данные мы собираем',
    'priv.colData': 'Данные',
    'priv.colWhy': 'Зачем',
    'priv.d1t': 'Личные и рабочие данные',
    'priv.d1w': 'Имя, фамилия, телефон и/или e-mail, должность, филиал',
    'priv.d1y': 'Чтобы создать аккаунт и записывать посещаемость нужному сотруднику.',
    'priv.d2t': 'Данные для входа',
    'priv.d2w': 'PIN — только в зашифрованном (хеш) виде',
    'priv.d2y': 'Чтобы в аккаунт входили только вы. В открытом виде нигде не хранится.',
    'priv.d3t': 'Геолокация (GPS)',
    'priv.d3w': 'Только в момент сканирования QR',
    'priv.d3y': 'Чтобы проверить, что вы в пределах рабочего места.',
    'priv.d4t': 'Фото при входе (селфи)',
    'priv.d4w': 'Фото при входе/выходе; фото работы на выезде',
    'priv.d4y': 'Чтобы подтвердить, что отметку сделали именно вы.',
    'priv.d5t': 'Фото профиля',
    'priv.optional': 'по желанию',
    'priv.d5w': 'Только если вы сами его добавили',
    'priv.d5y': 'Для показа в профиле. Ни с чем не сравнивается, удалить можно в любой момент.',
    'priv.d6t': 'Данные устройства',
    'priv.d6w': '«Отпечаток» устройства (идентификатор браузера)',
    'priv.d6y': 'Чтобы никто не отметился вместо вас.',
    'priv.d7t': 'Рабочее время и использование',
    'priv.d7w': 'Время входа и выхода, отгулы и отпуска, время последнего открытия приложения',
    'priv.d7y': 'Для отчётов о посещаемости и табеля.',
    'priv.d8t': 'Данные о зарплате',
    'priv.d8w': 'Если их внесла ваша компания',
    'priv.d8y': 'Для расчёта зарплаты по посещаемости.',
    'priv.photoNote.a': 'Если фотографирование вас беспокоит, поговорите с руководителем: требование фото можно отключить для отдельных сотрудников. Тогда вход всё равно фиксируется ',
    'priv.photoNote.b': 'по геолокации и устройству',
    'priv.photoNote.c': '.',
    'priv.s3': 'Как используются данные',
    'priv.u1': 'Учёт посещаемости (вход и выход) и отчёты',
    'priv.u2': 'Проверка, что вы на нужном рабочем месте (геозона)',
    'priv.u3': 'Подтверждение личности по фото (сравнение лица) и защита от подлога',
    'priv.u4': 'Отправка уведомлений о смене и выходе',
    'priv.s3p': 'Ваши данные видят только руководство вашей компании и менеджер вашего филиала. Другие компании их не видят.',
    'priv.s4': 'Технические сервисы',
    'priv.s4p': 'Для работы приложения данные обрабатываются следующими сервисами. Они обрабатывают данные только от имени QRLog.',
    'priv.v1': 'Хранение фотографий',
    'priv.v2': 'Сравнение лиц',
    'priv.v3': 'Push-уведомления',
    'priv.v4': 'Ответы чата «ИИ-помощник». В чат передаются только ваше сообщение и ваши показатели посещаемости; фото не передаются.',
    'priv.v5t': 'Хостинг-провайдер',
    'priv.v5': 'Дата-центр, где размещены серверы',
    'priv.s5': 'Хранение и безопасность',
    'priv.k1t': 'Хранение',
    'priv.k1d.a': 'Селфи при входе и выходе ',
    'priv.k1d.b': 'автоматически удаляются через 90 дней',
    'priv.k1d.c': '. Записи о посещаемости, эталонное фото, фото профиля и фото работы хранятся, пока действуют трудовые отношения, и в течение срока, требуемого законом.',
    'priv.k2t': 'При закрытии',
    'priv.k2d': 'При закрытии аккаунта компании данные удаляются или обезличиваются.',
    'priv.k3t': 'Защита',
    'priv.k3d': 'Все передачи шифруются (HTTPS/TLS). PIN не хранится в открытом виде. Доступ внутри компании ограничен ролями.',
    'priv.s6': 'Ваши права',
    'priv.r1': 'Просмотр',
    'priv.r2': 'Исправление',
    'priv.r3': 'Удаление',
    'priv.s6p': 'Чтобы воспользоваться этими правами, обратитесь сначала к работодателю, затем к нам: ',
    'priv.delBtn': 'Как удалить аккаунт',
    'priv.s7': 'Дети и изменения',
    'priv.kidsL': 'Дети',
    'priv.kidsT': 'Не для лиц младше 16 лет',
    'priv.kidsD': 'Приложение не предназначено для детей, и мы сознательно не собираем данные лиц младше 16 лет.',
    'priv.chgL': 'Изменения',
    'priv.chgT': 'Публикуются на этой странице',
    'priv.chgD': 'Политика может обновляться. Изменения публикуются на этой странице, а дата выше обновляется.',

    'blog.title': 'Блог',
    'blog.sub': 'Статьи о посещаемости и QR-системах.',
    'blog.metaTitle': 'Блог — QRLog',
    'blog.metaDesc': 'Статьи об учёте посещаемости, QR-системах и цифровизации учёта.',
    'blog.empty': 'Скоро здесь появятся первые статьи.',
    'blog.back': 'Все статьи',
    'blog.hero.title.a': 'О посещаемости, команде и ',
    'blog.hero.title.hl': 'рабочем дне',
    'blog.hero.title.b': '',
    'blog.hero.lead': 'Практичные статьи для руководителей и HR: как отслеживать приход сотрудников, не допускать фальшивых отметок и использовать QRLog на полную.',
    'blog.hero.imgAlt': 'QR-код, по которому проходит линия сканирования, и подтверждение входа',
    'blog.cats.aria': 'Категории',
    'blog.cat.all': 'Все',
    'blog.cat.davamiyyet': 'Посещаемость',
    'blog.cat.hr': 'HR и управление',
    'blog.cat.telimat': 'Инструкции',
    'blog.cat.yenilikler': 'Новости',
    'blog.featured': 'Рекомендуем',
    'blog.read': 'Читать',
    'blog.readTime': '{n} мин чтения',
    'blog.search.placeholder': 'Поиск статей...',
    'blog.search.label': 'Поиск статей',
    'blog.search.clear': 'Очистить поиск',
    'blog.search.results': 'Найдено: {n}',
    'blog.search.query': '«{q}»',
    'blog.search.emptyTitle': 'По запросу «{q}» статей не найдено',
    'blog.search.emptyHint': 'Попробуйте другое слово или очистите поиск.',
    'blog.search.reset': 'Очистить поиск',
    'blog.search.widen': 'Искать во всех категориях',
    'blog.crumb.aria': 'Навигация по сайту',
    'blog.crumb.home': 'Главная',
    'blog.summary.label': 'Коротко',
    'blog.share.title': 'Поделиться',
    'blog.share.copy': 'Скопировать ссылку',
    'blog.share.copied': 'Скопировано',
    'blog.share.whatsapp': 'Поделиться в WhatsApp',
    'blog.related.eyebrow': 'Читайте дальше',
    'blog.related.title': 'Читайте также',
    'blog.cta.title': 'Как QRLog будет работать в вашей команде?',
    'blog.cta.text': 'Напишите нам в WhatsApp — мы ответим на ваши вопросы.',
    'blog.cta.button': 'Написать в WhatsApp',

    'nf.title': 'Страница не найдена',
    'nf.sub':
      'Страница, которую вы ищете, перемещена или никогда не существовала. Возможно, ссылка устарела или в адресе есть опечатка.',
    'nf.btn': 'Вернуться на главную',

    'a11y.skip': 'Перейти к содержимому',
    'a11y.lang': 'Язык',
    'a11y.nav': 'Основное меню',
    'loader.aria': 'Страница загружается',
    'loader.scanning': 'Сканирование',
    'loader.done': 'Записано',

    'wa.aria': 'Написать в WhatsApp',
    'wa.tooltip': 'Написать в WhatsApp',
    'wa.message': 'Здравствуйте! Хочу узнать подробнее о QRLog.',

    'top.aria': 'Наверх',
  },

  en: {
    'meta.title': 'QRLog — QR-based staff attendance system | Azerbaijan',
    'meta.description':
      'QRLog is an attendance system: employees scan a QR code with their own phone to log check-in and check-out. Every entry is verified by a signed QR, GPS location, device and face check.',
    'meta.keywords':
      'staff attendance, employee attendance system, QR attendance, time tracking, GPS check-in, workforce attendance, QRLog, Azerbaijan',

    'nav.how': 'How it works',
    'nav.modules': 'Modules',
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
    'hero.d.checkIn': 'Check-in',
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

    'stats.title': 'In numbers',
    'stats.sub': 'In real companies, every day',
    'stats.s1': 'companies use QRLog',
    'stats.s2': 'staff check in by phone',
    'stats.s3': 'locations with a QR poster',
    'stats.s4': 'check-ins per workday',
    'stats.n1': 'Companies',
    'stats.n2': 'Employees',
    'stats.n3': 'Locations',
    'stats.n4': 'Daily check-ins',
    'stats.source': 'From the QRLog database',
    // «183» + «K» → «183K+»: no space in English (see az).
    'stats.thousand': 'K',

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

    'mod.eyebrow': 'Modules',
    'mod.title': 'Beyond attendance',
    'mod.sub': 'All in one panel — no separate system for each job.',
    'mod.carousel': 'carousel',
    'mod.slide': 'slide',
    'mod.slideLabel': '%s / %s',
    'mod.shift.name': 'Shift schedule',
    'mod.shift.desc': "Every employee's shift is set in advance.",
    'mod.leave.name': 'Leave and time off',
    'mod.leave.desc': 'Leave, sick days and time off are recorded and show up in reports.',
    'mod.field.name': 'Field tasks',
    'mod.field.desc': 'Assign on-site work to an employee and review the result against a checklist.',
    'mod.announce.name': 'Announcements',
    'mod.announce.desc': 'Reach the whole team with one message.',
    'mod.push.name': 'Push notifications',
    'mod.push.desc': "Shift reminders, announcements and warnings go straight to the employee's phone.",
    'mod.branches.name': 'Branches and roles',
    'mod.branches.desc': 'All branches in one panel; each role sees only what it needs.',
    'mod.kiosk.name': 'Kiosk mode',
    'mod.kiosk.desc': 'The QR on the entrance tablet refreshes itself; employees scan it with their own phone.',

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

    // «Saxtaya qarşı qoruma». «Problemlər» azərbaycanca qalır — panelin menyusunun adıdır.
    'af.eyebrow': 'Anti-fraud',
    'af.title': 'Every check-in passes four checks',
    'af.sub':
      "A check-in for someone else, from home or from a photo of the poster is either rejected or shows up flagged in the manager's panel. Checks start the moment the employee scans; the result is usually ready within seconds.",
    'af.rec.meta': 'Check-in · {t} · Head office',
    'af.rec.pending': 'checking',
    'af.rec.done': 'recorded',
    'af.rec.footer': 'One check-in · 4 checks',
    'af.sample': 'Sample data',
    'af.r1.t': 'QR signature',
    'af.r1.v': 'Head office poster',
    'af.r2.t': 'Device',
    'af.r2.v': 'Bound device',
    'af.r3.t': 'Location (GPS)',
    'af.r3.v': 'Inside branch radius',
    'af.r4.t': 'Face match',
    'af.r4.v': 'Match {p}%',
    'af.prevents': 'Prevents',
    'af.c1.t': 'Signed poster',
    'af.c1.p': 'fake codes',
    'af.c1.d':
      "The poster code is signed and tied to its branch: a fake code is rejected, and a code only works on its own branch's premises.",
    'af.c2.t': 'Device binding',
    'af.c2.p': 'checking in for others',
    'af.c2.d':
      "Each employee's account is bound to their phone. A check-in from a phone bound to someone else is rejected and listed under “Problemlər” with the owner's name.",
    'af.c3.t': 'GPS geofence',
    'af.c3.p': 'checking in from home',
    'af.c3.d':
      "A scan is accepted only within the branch's set radius. Checking in from home or on the way doesn't work.",
    'af.c4.t': 'Face check',
    'af.c4.p': 'buddy punching',
    'af.c4.d':
      "The check-in selfie is automatically compared with a reference photo. A scan on a friend's behalf reaches the manager flagged.",
    'af.door.t': 'No one gets stuck at the door',
    'af.door.d':
      "If the photo fails or the face doesn't match, the employee isn't blocked — the check-in is recorded and the manager sees it in the panel. A scan outside the radius is rejected; a submitted one is listed under “Problemlər” with its reason.",
    'af.rule1.t': 'Photo or face',
    'af.rule1.d': 'Check-in recorded; a face mismatch flags the row',
    'af.rule2.t': 'Outside the radius',
    'af.rule2.d': 'Scan rejected; a submitted one is listed in “Problemlər” with the reason',
    'af.rule3.t': 'No internet',
    'af.rule3.d': 'Scan saved on the phone, sent once back online',
    'af.rule4.t': 'Manual edit',
    'af.rule4.d': 'Logged with the name of whoever made it',
    'af.privacy': 'The selfie is used only to confirm attendance and identity',

    'aud.eyebrow': "Who it's for",
    'aud.title': 'Where does your team work?',
    'aud.sub': 'On sites, across branches or in one building — all run from the same panel.',

    'aud.site.pill': 'On sites',
    'aud.site.title': 'Construction, cleaning and logistics',
    'aud.site.desc': "Every check-in confirms the employee is inside the site's zone.",
    'aud.site.b1': 'GPS location check',
    'aud.site.b2': 'Each site has its own zone and radius',
    'aud.site.b3': 'Check-ins outside the zone are refused',

    'aud.chain.pill': 'Multi-branch chains',
    'aud.chain.title': 'Shop, restaurant and pharmacy chains',
    'aud.chain.desc': "Follow every branch's day from one place.",
    'aud.chain.b1': 'All branches in one panel',
    'aud.chain.b2': 'Branch managers see attendance for their own branches only',
    'aud.chain.b3': "Who's in and who's absent — on the live board",

    'aud.office.pill': 'Offices and head-office buildings',
    'aud.office.title': 'Business centres, holdings, clinics and training centres',
    'aud.office.desc':
      'A QRLog poster hangs at the entrance. Staff scan it with their own phone on the way in and out.',
    'aud.office.b1': 'No turnstiles, cards or fingerprint readers',
    'aud.office.b2': 'The poster is downloaded from the panel as a PDF and printed',
    'aud.office.b3': 'Everyone scans with their own phone — no queues',

    'aud.map.site': 'Construction site #4',
    'aud.map.verified': 'GPS verified',
    'aud.map.radius': '150 m',
    'aud.map.coordsLabel': 'GPS coordinates',

    'aud.branches.n1': 'Ganjlik',
    'aud.branches.n2': 'Yasamal',
    'aud.branches.n3': 'Nasimi',
    'aud.branches.sub': 'Branch · Baku',
    'aud.branches.status': '{n}/{m} at work',

    'sec.eyebrow': 'Security',
    'sec.title': 'Your data stays protected',
    'sec.sub': "Each company's data is isolated, and everyone sees only their own.",
    'sec.i1t': 'Encrypted connection',
    'sec.i1d': 'All traffic goes over HTTPS; certificates renew automatically.',
    'sec.i2t': 'Companies are kept apart',
    'sec.i2d':
      "Every request reaches only its own company's data. A request whose company can't be determined is rejected.",
    'sec.i3t': 'Nightly backups',
    'sec.i3d':
      'Every night a copy of the database goes to separate storage and is kept for 90 days. Restoring it is tested every week.',
    'sec.i4t': 'Role-based access',
    'sec.i4d':
      "Employees see only their own attendance, managers their branches', admins the whole company's.",
    'sec.e1': 'PINs are never stored in plain text',
    'sec.e2': 'Check-in photos are deleted automatically after 90 days',
    'sec.e3': 'After 8 wrong PIN attempts, sign-in is locked for 5 minutes',

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
      "Every site has its own GPS coordinates and radius — by default, a scan from outside that area is not accepted. An employee's account is bound to their own phone, and a photo is taken at check-in and automatically compared with their reference photo — if they don't match, the manager sees it.",
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
      'On Android, get it from Google Play. The App Store version is on its way — until then, QRLog runs on iPhone from the browser, just like an app.',
    'pwa.steps': 'On iPhone — today',
    'pwa.b1': 'Open it in the browser',
    'pwa.b2': 'Add to home screen',
    'pwa.b3': 'Use it like an app',
    'pwa.soon': 'Coming soon',
    'pwa.photoAlt': 'An employee holding a phone with the QRLog "Today" screen open',
    'gplay.alt': 'Get it on Google Play',

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

    'about.metaTitle': 'About — QRLog',
    'about.metaDesc':
      'QRLog is a QR-based staff attendance system built in Azerbaijan. We make attendance records simple, from a phone.',
    'about.eyebrow': 'About us',
    'about.title.a': 'We move attendance from the logbook ',
    'about.title.hl': 'to the phone',
    'about.title.b': '',
    'about.lead':
      "QRLog is built in Azerbaijan. Our goal is simple: when someone arrives and leaves work should live in a reliable record, not on paper or on someone's word.",
    'about.story.eyebrow': 'Our story',
    'about.story.title': "It started with one company's daily problem",
    'about.story.p1':
      'We started building QRLog for a large field-service company. Dozens of sites across the city, a paper logbook every morning and a timesheet put together by hand at the end of each month.',
    'about.story.p2a':
      "A turnstile at every site wasn't an option, and the logbook couldn't be fully trusted. We started from a simple idea: ",
    'about.story.p2b': 'every employee already has a phone.',
    'about.story.p2c': ' One QR poster on the wall should be enough.',
    'about.story.p3':
      'From day one the system was tested on real employees on real mornings. Today companies from other industries work the same way.',
    'about.v.aria': 'Before: a paper logbook. Now: the QRLog panel',
    'about.v.sample': 'Example',
    'about.v.jTitle': 'ATTENDANCE LOG',
    'about.v.jMeta': 'Site No. 3 · 14.10',
    'about.v.jName': 'Name',
    'about.v.jIn': 'In',
    'about.v.jSign': 'Signature',
    'about.v.aTitle': 'Today · Site 3',
    'about.v.aDate': '14 October',
    'about.v.live': 'Live',
    'about.v.in': 'In',
    'about.v.offline': 'offline',
    'about.v.noScan': 'No scan',
    'about.v.sAtWork': 'At work',
    'about.v.sFace': 'Face mismatch',
    'about.v.sAbsent': 'Not in',

    'contact.metaTitle': 'Contact — QRLog',
    'contact.metaDesc':
      'Contact QRLog. We answer questions about the attendance system, pricing and rollout.',
    'contact.phone': 'Phone',
    'contact.email': 'Email',
    'contact.whatsapp': 'WhatsApp',
    'contact.eyebrow': 'Contact',
    'contact.title.a': "Let's start with",
    'contact.title.hl': 'one message',
    'contact.lead':
      "Send us your company name and how many branches and employees you have. We'll reply with the right plan and setup steps in one message.",
    'contact.waLabel': 'WhatsApp · fastest',
    'contact.waOpen': 'Open chat',
    'contact.instagram': 'Instagram',
    'contact.copyPhone': 'Copy number',
    'contact.copyEmail': 'Copy address',
    'contact.chatAria': 'Sample WhatsApp chat',
    'contact.chatOnline': 'online',
    'contact.chatDay': 'Today',
    'contact.chatSample': 'Sample chat',
    'contact.chatReply':
      "Hi! Send us your company name and how many branches and employees you have, and we'll send a matching offer.",
    'contact.chatMe': "We're a cleaning services company:",
    'contact.chatL1': '3 branches',
    'contact.chatL2': 'about 120 employees',
    'contact.chatL3': 'some staff work in the field',
    'contact.chatInput': 'Message',

    'del.metaTitle': 'Account deletion — QRLog',
    'del.metaDesc':
      'How to request deletion of your account and related data in the QRLog attendance app.',
    'del.eyebrow': 'Account',
    'del.title': 'Account deletion',
    'del.lead':
      'Here we explain how to request deletion of your QRLog account and its data, what gets deleted and what may be kept.',
    'del.chipApp': 'QRLog attendance app',
    'del.chip30': 'Processed within 30 days',
    'del.cardAria': "Example: an employee's personal data being deleted",
    'del.cardPhone': 'Phone',
    'del.cardRole': 'Role',
    'del.cardRoleV': 'Office manager',
    'del.cardDevice': 'Device',
    'del.cardDeviceV': 'iPhone 18 Pro · linked',
    'del.cardDone': 'Deleted',
    'del.howTitle': 'How to request deletion',
    'del.howSub.a': 'QRLog accounts are managed by ',
    'del.howSub.b': 'the company you work for (your employer)',
    'del.howSub.c': '. There are two ways to make a request.',
    'del.emailTitle': 'Email us',
    'del.emailText':
      '— send an email with the subject below. So we can find your account, include:',
    'del.tplSubjectL': 'Subject',
    'del.tplSubject': 'Account deletion request',
    'del.tplBodyL': 'In the message',
    'del.tplName': 'Full name',
    'del.tplPhone': 'phone number on the account',
    'del.tplOr': '(or email)',
    'del.tplCompany': 'your company',
    'del.emailBtn': 'Write email',
    'del.copyBtn': 'Copy address',
    'del.copied': 'Copied',
    'del.workTitle': 'Through your employer',
    'del.workText1':
      'Contact whoever manages QRLog at your company (usually HR or your manager). They can deactivate or fully delete your account from the admin panel.',
    'del.workText2':
      "This is faster, because the account sits right in your company's panel.",
    'del.stepsTitle': 'What happens next',
    'del.whoYou': 'You',
    'del.whoUs': 'QRLog',
    'del.s1t': 'You send a request',
    'del.s1d': "You email us or contact your company's QRLog admin.",
    'del.s2t': 'We verify',
    'del.s2d':
      'We confirm the account is really yours, so no one can request deletion in your name.',
    'del.s3t': 'We delete',
    'del.s3d.a': 'After confirmation, within ',
    'del.s3d.b': '30 days',
    'del.s3d.c': ' at most, your personal data and photos are deleted.',
    'del.listTitle': 'What is deleted and what may remain',
    'del.delTitle': 'Deleted',
    'del.d1': 'Name, phone number, email, job title',
    'del.d2': 'Reference photo and the profile picture you chose',
    'del.d3': 'Check-in and check-out selfies',
    'del.d3s': 'including photos from field visits',
    'del.d4': 'Device data',
    'del.d4s': 'the "fingerprint" that links your phone to the account',
    'del.d5': 'Sign-in data',
    'del.d5s': 'PIN — stored in encrypted form',
    'del.keepTitle': 'May be kept for a period',
    'del.keep1.a': 'Attendance and payroll records',
    'del.keep1.b':
      ' (check-in and check-out history) may be kept by the employer for the period the law requires — for employment and accounting purposes.',
    'del.keep2.a': 'When that period ends, these records are ',
    'del.keep2.b': 'deleted or anonymised',
    'del.keep2.c': ', so they can no longer be linked to a specific person.',
    'del.note.a': "The delete button on the app's ",
    'del.note.b': '"Telefondakı hesablar" (accounts on this phone)',
    'del.note.c':
      " screen only removes the account from that phone's memory — your account and records are not deleted. For full deletion, use one of the ways above.",

    // Məxfilik siyasəti (PrivacyBody.astro, PermissionsCard.astro)
    'priv.metaTitle': 'Privacy policy — QRLog',
    'priv.metaDesc': 'QRLog attendance app privacy policy: what data it collects and how it is used.',
    'priv.eyebrow': 'Legal',
    'priv.title': 'Privacy policy',
    'priv.lead': 'In plain words: what data the QRLog attendance app collects, why, and who can see it. The app is for employees of companies that use QRLog.',
    'priv.chipDate': 'Effective date: 29 July 2026',
    'priv.chipRead': 'Reading time: ~4 minutes',
    'priv.permTitle': 'App permissions',
    'priv.permAria': 'App permissions: camera and location only while scanning, notifications optional',
    'priv.permCam': 'Camera',
    'priv.permGps': 'Location (GPS)',
    'priv.permPush': 'Notifications',
    'priv.permOnScan': 'Only while scanning',
    'priv.permOptional': 'Optional',
    'priv.tldrTitle': 'In short',
    'priv.t1t': "We don't track you all day",
    'priv.t1d': 'Location is checked only when you scan the QR code. Nothing is collected in the background.',
    'priv.t2t': 'No ads',
    'priv.t2d': 'Your data is not used for advertising and is never sold.',
    'priv.t3t': 'Your company is in charge',
    'priv.t3d': "Your data belongs to the company you work for. Other companies can't see it.",
    'priv.t4t': 'You can ask for deletion',
    'priv.t4d': 'You can request deletion of your account and data at any time.',
    'priv.toc': 'Contents',
    'priv.tocAria': 'Table of contents',
    'priv.intro.a': 'This policy explains what data the ',
    'priv.intro.b': ' attendance app (the "App") collects and how it uses it.',
    'priv.s1': 'Who controls the data',
    'priv.ctrlLabel': 'Data controller',
    'priv.ctrlName': 'Your employer',
    'priv.ctrlText': 'Your company decides what data is collected and why.',
    'priv.procLabel': 'Data processor',
    'priv.procText': "We process data on your company's behalf and on its instructions.",
    'priv.s1p': 'Questions about your data can go to your employer first.',
    'priv.s2': 'What data we collect',
    'priv.colData': 'Data',
    'priv.colWhy': 'Why',
    'priv.d1t': 'Personal and work details',
    'priv.d1w': 'Name, phone and/or email, job title, branch',
    'priv.d1y': 'To create the account and record attendance to the right person.',
    'priv.d2t': 'Sign-in data',
    'priv.d2w': 'PIN — stored only as a hash',
    'priv.d2y': 'So only you can sign in. It is never stored in plain text.',
    'priv.d3t': 'Location (GPS)',
    'priv.d3w': 'Only when you scan the QR code',
    'priv.d3y': 'To check you are within the allowed radius of your workplace.',
    'priv.d4t': 'Check-in photo (selfie)',
    'priv.d4w': 'Photo at check-in/out; work photo on field visits',
    'priv.d4y': 'To confirm the check-in was made by you.',
    'priv.d5t': 'Profile picture',
    'priv.optional': 'optional',
    'priv.d5w': 'Only if you add one yourself',
    'priv.d5y': 'Shown on your profile. Not compared with anything; you can remove it any time.',
    'priv.d6t': 'Device data',
    'priv.d6w': 'Device "fingerprint" (browser identifier)',
    'priv.d6y': 'So no one can check in on your behalf.',
    'priv.d7t': 'Working time and usage',
    'priv.d7w': 'Check-in/out times, leave and holiday records, when you last opened the app',
    'priv.d7y': 'For attendance reports and timesheets.',
    'priv.d8t': 'Payroll data',
    'priv.d8w': 'If your company has entered it',
    'priv.d8y': 'For attendance-based payroll reports.',
    'priv.photoNote.a': 'If taking photos bothers you, talk to your manager: the photo requirement can be turned off for individual employees. Check-ins are then still recorded ',
    'priv.photoNote.b': 'by location and device checks',
    'priv.photoNote.c': '.',
    'priv.s3': 'How the data is used',
    'priv.u1': 'Recording attendance (check-in and out) and reports',
    'priv.u2': 'Checking you are at the right workplace (geofence)',
    'priv.u3': 'Confirming identity by photo (face match) and preventing fraud',
    'priv.u4': 'Sending shift and check-out notifications',
    'priv.s3p': "Only your company's management and your branch manager can see your data. Other companies cannot.",
    'priv.s4': 'Technical services',
    'priv.s4p': "To run the app, data is processed by the services below. They process it only on QRLog's behalf.",
    'priv.v1': 'Photo storage',
    'priv.v2': 'Face matching',
    'priv.v3': 'Push notifications',
    'priv.v4': 'Answers in the "AI assistant" chat. Only your message and your own attendance figures are sent; photos are not.',
    'priv.v5t': 'Hosting provider',
    'priv.v5': 'The data centre where the servers run',
    'priv.s5': 'Retention and security',
    'priv.k1t': 'Retention',
    'priv.k1d.a': 'Check-in and check-out selfies are ',
    'priv.k1d.b': 'deleted automatically after 90 days',
    'priv.k1d.c': '. Attendance records and reference, profile and work photos are kept while you are employed and for as long as the law requires.',
    'priv.k2t': 'On closure',
    'priv.k2d': 'When a company account is closed, the data is deleted or anonymised.',
    'priv.k3t': 'Protection',
    'priv.k3d': 'All traffic is encrypted (HTTPS/TLS). PINs are never stored in plain text. Access inside the company is limited by role.',
    'priv.s6': 'Your rights',
    'priv.r1': 'View',
    'priv.r2': 'Correct',
    'priv.r3': 'Request deletion',
    'priv.s6p': 'To use these rights, contact your employer first, then us: ',
    'priv.delBtn': 'How to delete your account',
    'priv.s7': 'Children and changes',
    'priv.kidsL': 'Children',
    'priv.kidsT': 'Not for anyone under 16',
    'priv.kidsD': 'The app is not meant for children, and we do not knowingly collect data from anyone under 16.',
    'priv.chgL': 'Changes',
    'priv.chgT': 'Published on this page',
    'priv.chgD': 'This policy may change. Updates are published here and the date above is updated.',

    'blog.title': 'Blog',
    'blog.sub': 'Articles about attendance and QR systems.',
    'blog.metaTitle': 'Blog — QRLog',
    'blog.metaDesc': 'Articles about staff attendance, QR systems and digitising records.',
    'blog.empty': 'The first articles will appear here soon.',
    'blog.back': 'All articles',
    'blog.hero.title.a': 'Notes on attendance, teams and the ',
    'blog.hero.title.hl': 'working day',
    'blog.hero.title.b': '',
    'blog.hero.lead': 'Practical articles for managers and HR: how to track staff check-ins, stop fake attendance and get the most out of QRLog.',
    'blog.hero.imgAlt': 'A QR code with a scan line passing over it and a check-in confirmation',
    'blog.cats.aria': 'Categories',
    'blog.cat.all': 'All',
    'blog.cat.davamiyyet': 'Attendance',
    'blog.cat.hr': 'HR & management',
    'blog.cat.telimat': 'Guides',
    'blog.cat.yenilikler': 'News',
    'blog.featured': 'Featured',
    'blog.read': 'Read',
    'blog.readTime': '{n} min read',
    'blog.search.placeholder': 'Search articles...',
    'blog.search.label': 'Search articles',
    'blog.search.clear': 'Clear search',
    'blog.search.results': 'Results: {n}',
    'blog.search.query': '“{q}”',
    'blog.search.emptyTitle': 'No articles found for “{q}”',
    'blog.search.emptyHint': 'Try another word or clear the search.',
    'blog.search.reset': 'Clear search',
    'blog.search.widen': 'Search all categories',
    'blog.crumb.aria': 'Breadcrumb',
    'blog.crumb.home': 'Home',
    'blog.summary.label': 'In short',
    'blog.share.title': 'Share',
    'blog.share.copy': 'Copy link',
    'blog.share.copied': 'Copied',
    'blog.share.whatsapp': 'Share on WhatsApp',
    'blog.related.eyebrow': 'Keep reading',
    'blog.related.title': 'You may also like',
    'blog.cta.title': 'How would QRLog work in your team?',
    'blog.cta.text': 'Ask us on WhatsApp and we will reply.',
    'blog.cta.button': 'Chat on WhatsApp',

    'nf.title': 'Page not found',
    'nf.sub':
      'The page you’re looking for has moved or never existed. The link may be outdated, or there may be a typo in the address.',
    'nf.btn': 'Back to home',

    'a11y.skip': 'Skip to main content',
    'a11y.lang': 'Language',
    'a11y.nav': 'Main navigation',
    'loader.aria': 'Page is loading',
    'loader.scanning': 'Scanning',
    'loader.done': 'Recorded',

    'wa.aria': 'Message us on WhatsApp',
    'wa.tooltip': 'Message us on WhatsApp',
    'wa.message': "Hello! I'd like to learn more about QRLog.",

    'top.aria': 'Back to top',
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
 * Hüquqi səhifələr (məxfilik, hesabın silinməsi) artıq üç dildədir, ona görə burada yoxdur. Tərcüməsi
 * olmayan səhifə üçün başqa dil təklif etmək kiçik kosmetik səhv deyil: dil seçici oxucunu 404-ə
 * göndərir, hreflang teqləri isə axtarış sistemlərini heç vaxt yaradılmamış ünvanlara yönəldir.
 *
 * Səhifənin tərcüməsi olmayan kimi onu bura əlavə et, tərcüməsi hazır olanda isə buradan sil.
 */
const azOnlyRoutes = ['/bloq/', '/404/'] as const

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
