// Every translated string on the site. Three languages, one flat key space — a component only ever
// calls t('some.key'), so adding a language means adding a block here and nothing else.
//
// The copy describes the product as it actually ships: a printed, permanent QR poster per site,
// GPS + bound device + selfie on scan, an Azerbaijani admin panel with Excel reports, and a PWA.
// Please keep it that way — a claim on this page is a promise the app has to keep.

export const languages = ['az', 'ru', 'en'] as const
export type Lang = (typeof languages)[number]
export const defaultLang: Lang = 'az'

export const localeMap: Record<Lang, string> = {
  az: 'az_AZ',
  ru: 'ru_RU',
  en: 'en_US',
}

export const htmlLang: Record<Lang, string> = {
  az: 'az-AZ',
  ru: 'ru-RU',
  en: 'en-US',
}

// Path prefix per language ('' for default AZ)
export const localePrefix: Record<Lang, string> = {
  az: '',
  ru: '/ru',
  en: '/en',
}

export const ui = {
  az: {
    'meta.title': 'QRLog — QR ilə işçi davamiyyəti sistemi | Azərbaycan',
    'meta.description':
      'QRLog — işçilərin telefonu ilə QR kodu skan edərək giriş-çıxışını qeydə alan davamiyyət sistemi. GPS məkan yoxlaması, cihaz bağlaması və foto təsdiqi ilə saxta girişin qarşısını alır.',
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
      'İşçi iş yerindəki QR posteri telefonu ilə skan edir; sistem məkanı, cihazı və şəkli yoxlayıb giriş-çıxışı qeydə alır. Turniket, barmaq izi cihazı və ya kağız jurnal lazım deyil.',
    'hero.cta2': 'Necə işləyir →',
    'hero.s1n': '~10 san',
    'hero.s1l': 'bir girişin qeydə alınması',
    'hero.s2n': '0 ₼',
    'hero.s2l': 'əlavə avadanlıq xərci',
    'hero.s3n': 'Canlı',
    'hero.s3l': 'kim işdədir — anlıq',
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
    'stats.s3': 'qat yoxlama: məkan, cihaz, şəkil',
    'stats.s4': 'filial və işçi limiti',
    'stats.s4v': 'Limitsiz',

    'how.eyebrow': 'İş prinsipi',
    'how.title': 'Üç addımda hazırdır',
    'how.sub': 'Bir dəfə qurulur — sonra hər gün özü işləyir.',
    'how.s1t': 'QR posteri asın',
    'how.s1d':
      'Hər filial üçün bir dəfə sabit QR poster çap edib divara asırsınız. Kod dəyişmir — posteri təzələmək lazım gəlmir.',
    'how.s2t': 'İşçi telefonla skan edir',
    'how.s2d':
      'Gələndə və gedəndə işçi öz telefonu ilə QR-ı skan edir. Məkan, cihaz və şəkil eyni anda yoxlanılır.',
    'how.s3t': 'Rəhbərlik canlı görür',
    'how.s3d':
      'Giriş-çıxış dərhal admin panelə düşür. Tarix üzrə filtrləyin, Excel hesabatı yükləyin, filial üzrə bölün.',

    'feat.eyebrow': 'İmkanlar',
    'feat.title': 'Davamiyyət üçün lazım olan hər şey',
    'feat.sub': 'Gündəlik uçotdan aylıq hesabata qədər bir paneldə.',
    'feat.f1t': 'Canlı davamiyyət lövhəsi',
    'feat.f1d': 'Kim gəlib, kim çıxıb, kim yoxdur — anlıq görünür, əl ilə heç nə sayılmır.',
    'feat.f2t': 'Excel hesabatları',
    'feat.f2d': 'Tarix və filial üzrə hesabatı bir kliklə Excel faylı kimi yükləyin.',
    'feat.f3t': 'GPS məkan yoxlaması',
    'feat.f3d': 'Skan yalnız filialın təyin olunmuş radiusunda qəbul edilir — evdən “gəldim” olmur.',
    'feat.f4t': 'Cihaz bağlaması',
    'feat.f4d': 'Hər işçi öz telefonuna bağlanır; tanınmayan cihazdan giriş nəzarət altındadır.',
    'feat.f5t': 'Rollar və filial əhatəsi',
    'feat.f5d': 'İşçi, menecer, admin. Menecer yalnız öz filiallarını görür — artıq bir sətir də yox.',
    'feat.f6t': 'Quraşdırma yoxdur (PWA)',
    'feat.f6d': 'Brauzerdən açılır, ana ekrana tətbiq kimi əlavə olunur. Mağazadan yükləmək lazım deyil.',

    'dash.eyebrow': 'Admin panel',
    'dash.title': 'Bütün davamiyyət bir ekranda',
    'dash.sub': 'Panel canlı yenilənir. Aşağıdakı ekran nümunəvi məlumatla göstərilib.',
    'dash.present': 'İşdə',
    'dash.out': 'Çıxış edib',
    'dash.absent': 'Gəlməyib',
    'dash.rate': 'iştirak',
    'dash.demo': 'nümunə',

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
    'price.sub': 'Təşkilatınızın ölçüsünə uyğun plan seçin. Gizli ödəniş yoxdur.',
    'price.popular': 'POPULYAR',
    'price.mo': '/ay',
    'price.note': 'Qiymətlər dəqiqləşdirilir — yekun təklif üçün bizimlə əlaqə saxlayın.',
    'price.p1n': 'Start',
    'price.p1d': 'Kiçik komanda və sınaq üçün.',
    'price.p1a': 'Pulsuz',
    'price.p1f1': '50 işçiyə qədər',
    'price.p1f2': 'Bir filial və QR poster',
    'price.p1f3': 'Canlı davamiyyət lövhəsi',
    'price.p1c': 'Əlaqə saxlayın',
    'price.p2n': 'Biznes',
    'price.p2d': 'Böyüyən şirkətlər üçün.',
    // Used only if PRICING sets this plan's amount to null in src/data/site.ts.
    'price.p2a': 'Fərdi',
    'price.p2f1': '500 işçiyə qədər',
    'price.p2f2': 'Limitsiz filial',
    'price.p2f3': 'Excel hesabatları və maaş',
    'price.p2f4': 'Push bildirişləri',
    'price.p2c': 'Əlaqə saxlayın',
    'price.p3n': 'Enterprise',
    'price.p3d': 'Böyük təşkilatlar üçün fərdi həll.',
    'price.p3a': 'Fərdi',
    'price.p3f1': 'Limitsiz işçi',
    'price.p3f2': 'Öz subdomeniniz',
    'price.p3f3': 'Quraşdırma dəstəyi',
    'price.p3c': 'Əlaqə saxlayın',

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
      'Mağazadan yükləmək lazım deyil. QRLog PWA-dır — brauzerdən açılır və istəyə görə telefonun ana ekranına tətbiq kimi əlavə edilir.',
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
    'pwa.title': 'Ana ekrana əlavə edin',
    'pwa.sub':
      'QRLog PWA-dır: brauzerdə açılır, sonra “Ana ekrana əlavə et” ilə tətbiq kimi işləyir. App Store və ya Google Play lazım deyil — yeniləmə də özü gəlir.',
    'pwa.b1': 'Brauzerdə açın',
    'pwa.b2': 'Ana ekrana əlavə edin',
    'pwa.b3': 'Tətbiq kimi işlədin',

    'cta.title': 'Davamiyyəti bu gün rəqəmsallaşdırın',
    'cta.sub': 'Filialı və işçiləri əlavə edin, QR posteri asın — həmin gün işə düşür.',
    'cta.btn1': 'Əlaqə saxlayın',

    'foot.tag': 'QR əsaslı işçi davamiyyəti sistemi. Telefonla işləyir, avadanlıq tələb etmir.',
    'foot.product': 'Məhsul',
    'foot.company': 'Şirkət',
    'foot.contact': 'Əlaqə',
    'foot.about': 'Haqqımızda',
    'foot.blog': 'Bloq',
    'foot.support': 'Dəstək',
    'foot.rights': 'Bütün hüquqlar qorunur.',
    'foot.privacy': 'Məxfilik',
    'foot.terms': 'Şərtlər',

    'about.title': 'Haqqımızda',
    'about.sub': 'QR əsaslı işçi davamiyyəti sistemi.',
    'about.metaTitle': 'Haqqımızda — QRLog',
    'about.metaDesc':
      'QRLog — Azərbaycanda QR əsaslı işçi davamiyyəti sistemi. Davamiyyət uçotunu telefonla sadələşdiririk.',
    'about.p1':
      'QRLog işçi davamiyyətinin uçotunu sadə, sürətli və etibarlı etmək üçün yaradılıb. Turniket və bahalı terminallar əvəzinə işçilər öz telefonları ilə iş yerindəki QR posteri skan edir.',
    'about.p2':
      'Sistem eyni anda üç şeyi yoxlayır: işçinin filial ərazisində olduğunu (GPS), tanınmış cihazdan skan etdiyini və girişdəki şəklin referansla uyğunluğunu. Bu yoxlamalar saxta girişi çətinləşdirir, amma heç biri girişi dayandırmır — kamera işləməsə belə işçi gəldiyini qeydə ala bilir, çünki əmək haqqı həmin qeydə bağlıdır.',
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
    'contact.address': 'Ünvan',
    'contact.writeTitle': 'Bizə yazın',
    'contact.writeText':
      'Şirkətin adını, filial sayını və təxmini işçi sayını yazsanız, sizə uyğun təklifi bir cavabda göndərərik.',
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
  },

  ru: {
    'meta.title': 'QRLog — учёт посещаемости сотрудников по QR | Азербайджан',
    'meta.description':
      'QRLog — система учёта прихода и ухода сотрудников: сотрудник сканирует QR-постер телефоном, система проверяет геолокацию, устройство и фото. Без турникетов и бумажных журналов.',
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
      'Сотрудник сканирует QR-постер на рабочем месте своим телефоном; система проверяет локацию, устройство и фото — и записывает приход или уход. Турникеты, сканеры отпечатков и бумажные журналы не нужны.',
    'hero.cta2': 'Как это работает →',
    'hero.s1n': '~10 сек',
    'hero.s1l': 'на одну отметку',
    'hero.s2n': '0 ₼',
    'hero.s2l': 'затрат на оборудование',
    'hero.s3n': 'Онлайн',
    'hero.s3l': 'кто на месте — сразу',
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
    'stats.s3': 'слоя проверки: локация, устройство, фото',
    'stats.s4': 'лимит филиалов и сотрудников',
    'stats.s4v': 'Без лимита',

    'how.eyebrow': 'Принцип работы',
    'how.title': 'Готово за три шага',
    'how.sub': 'Настраивается один раз — дальше работает само.',
    'how.s1t': 'Повесьте QR-постер',
    'how.s1d':
      'Для каждого филиала один раз печатается постоянный QR-постер. Код не меняется — перепечатывать не нужно.',
    'how.s2t': 'Сотрудник сканирует',
    'how.s2d':
      'Приходя и уходя, сотрудник сканирует QR своим телефоном. Локация, устройство и фото проверяются одновременно.',
    'how.s3t': 'Руководство видит онлайн',
    'how.s3d':
      'Отметки сразу попадают в админ-панель. Фильтруйте по датам, выгружайте отчёт в Excel, разбивайте по филиалам.',

    'feat.eyebrow': 'Возможности',
    'feat.title': 'Всё, что нужно для учёта',
    'feat.sub': 'От ежедневных отметок до месячного отчёта — в одной панели.',
    'feat.f1t': 'Живая доска посещаемости',
    'feat.f1d': 'Кто пришёл, кто ушёл, кого нет — видно сразу, без ручного подсчёта.',
    'feat.f2t': 'Отчёты в Excel',
    'feat.f2d': 'Отчёт по датам и филиалам выгружается в Excel одним кликом.',
    'feat.f3t': 'Проверка геолокации',
    'feat.f3d': 'Скан принимается только в заданном радиусе филиала — «пришёл» из дома не пройдёт.',
    'feat.f4t': 'Привязка устройства',
    'feat.f4d':
      'Каждый сотрудник привязан к своему телефону; вход с чужого устройства контролируется.',
    'feat.f5t': 'Роли и охват филиалов',
    'feat.f5d': 'Сотрудник, менеджер, админ. Менеджер видит только свои филиалы — ни строкой больше.',
    'feat.f6t': 'Без установки (PWA)',
    'feat.f6d': 'Открывается в браузере и добавляется на главный экран. Магазин приложений не нужен.',

    'dash.eyebrow': 'Админ-панель',
    'dash.title': 'Вся посещаемость на одном экране',
    'dash.sub': 'Панель обновляется в реальном времени. Экран ниже показан с примерными данными.',
    'dash.present': 'На месте',
    'dash.out': 'Ушли',
    'dash.absent': 'Не пришли',
    'dash.rate': 'явка',
    'dash.demo': 'пример',

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
    'price.sub': 'Выберите план под размер организации. Без скрытых платежей.',
    'price.popular': 'ПОПУЛЯРНЫЙ',
    'price.mo': '/мес',
    'price.note': 'Цены уточняются — свяжитесь с нами для окончательного предложения.',
    'price.p1n': 'Start',
    'price.p1d': 'Для небольшой команды и теста.',
    'price.p1a': 'Бесплатно',
    'price.p1f1': 'До 50 сотрудников',
    'price.p1f2': 'Один филиал и QR-постер',
    'price.p1f3': 'Живая доска посещаемости',
    'price.p1c': 'Связаться',
    'price.p2n': 'Бизнес',
    'price.p2d': 'Для растущих компаний.',
    'price.p2a': 'Индивид.',
    'price.p2f1': 'До 500 сотрудников',
    'price.p2f2': 'Без лимита филиалов',
    'price.p2f3': 'Отчёты Excel и зарплата',
    'price.p2f4': 'Push-уведомления',
    'price.p2c': 'Связаться',
    'price.p3n': 'Enterprise',
    'price.p3d': 'Индивидуальное решение для крупных организаций.',
    'price.p3a': 'Индивид.',
    'price.p3f1': 'Без лимита сотрудников',
    'price.p3f2': 'Свой поддомен',
    'price.p3f3': 'Помощь при внедрении',
    'price.p3c': 'Связаться',

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
      'Из магазина — нет. QRLog это PWA: открывается в браузере и при желании добавляется на главный экран телефона как приложение.',
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
    'pwa.title': 'Добавьте на главный экран',
    'pwa.sub':
      'QRLog это PWA: открывается в браузере, а затем через «Добавить на главный экран» работает как приложение. App Store и Google Play не нужны — обновления приходят сами.',
    'pwa.b1': 'Откройте в браузере',
    'pwa.b2': 'Добавьте на главный экран',
    'pwa.b3': 'Пользуйтесь как приложением',

    'cta.title': 'Оцифруйте посещаемость уже сегодня',
    'cta.sub': 'Добавьте филиалы и сотрудников, повесьте QR-постер — заработает в тот же день.',
    'cta.btn1': 'Связаться',

    'foot.tag': 'Система учёта посещаемости по QR. Работает с телефона, оборудование не нужно.',
    'foot.product': 'Продукт',
    'foot.company': 'Компания',
    'foot.contact': 'Контакты',
    'foot.about': 'О нас',
    'foot.blog': 'Блог',
    'foot.support': 'Поддержка',
    'foot.rights': 'Все права защищены.',
    'foot.privacy': 'Конфиденциальность',
    'foot.terms': 'Условия',

    'about.title': 'О нас',
    'about.sub': 'Система учёта посещаемости на основе QR.',
    'about.metaTitle': 'О нас — QRLog',
    'about.metaDesc':
      'QRLog — система учёта посещаемости сотрудников по QR в Азербайджане. Упрощаем учёт с помощью телефона.',
    'about.p1':
      'QRLog создан, чтобы сделать учёт посещаемости простым, быстрым и надёжным. Вместо турникетов и дорогих терминалов сотрудники сканируют QR-постер на рабочем месте своим телефоном.',
    'about.p2':
      'Система одновременно проверяет три вещи: находится ли сотрудник на территории филиала (GPS), сканирует ли он со знакомого устройства и совпадает ли фото с эталоном. Эти проверки усложняют подлог, но ни одна из них не блокирует отметку — даже если камера не работает, сотрудник может зафиксировать приход, потому что от этой записи зависит его зарплата.',
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
    'contact.address': 'Адрес',
    'contact.writeTitle': 'Напишите нам',
    'contact.writeText':
      'Укажите название компании, количество филиалов и примерное число сотрудников — пришлём подходящее предложение одним письмом.',
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
  },

  en: {
    'meta.title': 'QRLog — QR-based staff attendance system | Azerbaijan',
    'meta.description':
      'QRLog records staff check-in and check-out from a phone: the employee scans a printed QR poster and the system verifies location, device and photo. No turnstiles, no paper registers.',
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
      'An employee scans the QR poster at their site with their own phone; the system checks the location, the device and the photo, then records the check-in or check-out. No turnstiles, no fingerprint readers, no paper register.',
    'hero.cta2': 'How it works →',
    'hero.s1n': '~10 sec',
    'hero.s1l': 'to record one check-in',
    'hero.s2n': '0 ₼',
    'hero.s2l': 'spent on hardware',
    'hero.s3n': 'Live',
    'hero.s3l': "who's on site — right now",
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
    'stats.s3': 'layers of checking: location, device, photo',
    'stats.s4': 'limit on sites and employees',
    'stats.s4v': 'None',

    'how.eyebrow': 'How it works',
    'how.title': 'Ready in three steps',
    'how.sub': 'Set it up once — it runs itself after that.',
    'how.s1t': 'Put up the QR poster',
    'how.s1d':
      'Each site gets one permanent printed QR poster on the wall. The code does not rotate, so the poster never needs reprinting.',
    'how.s2t': 'Staff scan with their phone',
    'how.s2d':
      'On arrival and on leaving, the employee scans the QR with their own phone. Location, device and photo are checked at the same time.',
    'how.s3t': 'Managers see it live',
    'how.s3d':
      'Check-ins land in the admin panel immediately. Filter by date, export to Excel, split by site.',

    'feat.eyebrow': 'Features',
    'feat.title': 'Everything attendance needs',
    'feat.sub': 'From the daily board to the monthly report, in one panel.',
    'feat.f1t': 'Live attendance board',
    'feat.f1d':
      'Who arrived, who left, who is missing — visible instantly, with nothing counted by hand.',
    'feat.f2t': 'Excel reports',
    'feat.f2d': 'Download a report by date range and site as an Excel file in one click.',
    'feat.f3t': 'GPS location check',
    'feat.f3d': 'A scan is only accepted inside the site radius — nobody checks in from home.',
    'feat.f4t': 'Device binding',
    'feat.f4d':
      'Each employee is bound to their own phone; a scan from an unknown device is controlled.',
    'feat.f5t': 'Roles and site scope',
    'feat.f5d': 'Employee, manager, admin. A manager sees only their own sites — not one row more.',
    'feat.f6t': 'Nothing to install (PWA)',
    'feat.f6d': 'It opens in the browser and can be added to the home screen. No app store needed.',

    'dash.eyebrow': 'Admin panel',
    'dash.title': 'All attendance on one screen',
    'dash.sub': 'The board updates live. The screen below is shown with sample data.',
    'dash.present': 'On site',
    'dash.out': 'Checked out',
    'dash.absent': 'Absent',
    'dash.rate': 'attendance',
    'dash.demo': 'sample',

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
    'price.sub': 'Pick the plan that matches your size. No hidden fees.',
    'price.popular': 'POPULAR',
    'price.mo': '/mo',
    'price.note': 'Pricing is being finalised — contact us for a firm quote.',
    'price.p1n': 'Start',
    'price.p1d': 'For a small team or a trial.',
    'price.p1a': 'Free',
    'price.p1f1': 'Up to 50 employees',
    'price.p1f2': 'One site and QR poster',
    'price.p1f3': 'Live attendance board',
    'price.p1c': 'Contact us',
    'price.p2n': 'Business',
    'price.p2d': 'For growing companies.',
    'price.p2a': 'Custom',
    'price.p2f1': 'Up to 500 employees',
    'price.p2f2': 'Unlimited sites',
    'price.p2f3': 'Excel reports and payroll',
    'price.p2f4': 'Push notifications',
    'price.p2c': 'Contact us',
    'price.p3n': 'Enterprise',
    'price.p3d': 'A tailored setup for large organisations.',
    'price.p3a': 'Custom',
    'price.p3f1': 'Unlimited employees',
    'price.p3f2': 'Your own subdomain',
    'price.p3f3': 'Onboarding support',
    'price.p3c': 'Contact us',

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
      'Not from a store. QRLog is a PWA — it opens in the browser and can optionally be added to the phone home screen like an app.',
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
    'pwa.title': 'Add it to the home screen',
    'pwa.sub':
      'QRLog is a PWA: it opens in the browser, and "Add to home screen" makes it behave like an app. No App Store or Google Play — and updates arrive on their own.',
    'pwa.b1': 'Open it in the browser',
    'pwa.b2': 'Add to home screen',
    'pwa.b3': 'Use it like an app',

    'cta.title': 'Digitise attendance today',
    'cta.sub': 'Add your sites and employees, put up the QR poster — it works the same day.',
    'cta.btn1': 'Contact us',

    'foot.tag': 'QR-based staff attendance. Runs on a phone, needs no hardware.',
    'foot.product': 'Product',
    'foot.company': 'Company',
    'foot.contact': 'Contact',
    'foot.about': 'About',
    'foot.blog': 'Blog',
    'foot.support': 'Support',
    'foot.rights': 'All rights reserved.',
    'foot.privacy': 'Privacy',
    'foot.terms': 'Terms',

    'about.title': 'About us',
    'about.sub': 'QR-based staff attendance.',
    'about.metaTitle': 'About — QRLog',
    'about.metaDesc':
      'QRLog is a QR-based staff attendance system built in Azerbaijan. We make attendance records simple, from a phone.',
    'about.p1':
      'QRLog exists to make attendance records simple, fast and reliable. Instead of turnstiles and expensive terminals, employees scan the QR poster at their workplace with their own phone.',
    'about.p2':
      'The system checks three things at once: that the employee is inside the site radius (GPS), that they are scanning from a known device, and that the check-in photo matches the reference. Those checks make a fake check-in hard, but none of them blocks one — even with a broken camera an employee can still record that they came to work, because their pay depends on that record.',
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
    'contact.address': 'Address',
    'contact.writeTitle': 'Write to us',
    'contact.writeText':
      'Tell us the company name, how many sites you have and roughly how many employees, and we will send a matching quote in one reply.',
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
  },
} as const

export type UIKey = keyof (typeof ui)['az']

export function useTranslations(lang: Lang) {
  return function t(key: UIKey): string {
    return (
      (ui[lang] as Record<string, string>)[key] ?? (ui[defaultLang] as Record<string, string>)[key]
    )
  }
}

// Build a localized URL for a given path. Slugs stay Azerbaijani in every language on purpose:
// /haqqimizda/ is already indexed, and translating the slug would have broken it for no gain.
export function localizedPath(lang: Lang, path = '/'): string {
  const prefix = localePrefix[lang]
  if (path === '/') return prefix === '' ? '/' : `${prefix}/`
  return `${prefix}${path}`
}
