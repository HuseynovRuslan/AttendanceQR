// Saytın özünün çəkdiyi loqolar. favicon.png, logo-mark.png və og-image.png burada deyil, public/-dədir:
// brauzer və axtarış robotları onları sabit adla çəkir, ona görə URL dəyişməməlidir.
import logoWordFile from '../assets/images/brand/logo-word.png'

// BaseLayout (preload), Header və hero posteri eyni ayarlarla çəkməlidir: astro:assets hər fərqli ayar
// üçün ayrı fayl yaradır, uyğun gəlməyən preload isə heç vaxt göstərilməyən ikinci şəkil yükləyər.
// 200px ən böyüyünə (Header, ~93px) 2x ekranda çatır.
export const LOGO_WORD = {
  src: logoWordFile,
  width: 200,
  height: 60,
  format: 'webp',
} as const
