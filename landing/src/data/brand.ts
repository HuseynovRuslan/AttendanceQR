// Brand marks the SITE ITSELF renders. The three files a browser or a crawler fetches by a fixed
// name — favicon.png, logo-mark.png (apple-touch-icon + the Organization logo in JSON-LD) and
// og-image.png — are deliberately NOT here: they stay in public/, unhashed and unconverted, because
// their URL is the contract. See README.md → «Struktur».
import logoWordFile from '../assets/images/brand/logo-word.png'

// The wordmark is preloaded in BaseLayout, painted by Header and painted again inside the hero
// poster. astro:assets emits a SEPARATE file for every distinct set of options, so all three have to
// ask for the same one — a preload whose href does not match the <img> that follows downloads a
// second image nobody ever paints. Hence one shared options object instead of three <Image /> calls.
//
// 200px covers the largest of the three (the header: h-7 = 28px tall, so ~93px wide) at 2x DPR. The
// hero's copy is a third of that size and simply reuses the file the preload has already warmed.
export const LOGO_WORD = {
  src: logoWordFile,
  width: 200,
  height: 60,
  format: 'webp',
} as const
