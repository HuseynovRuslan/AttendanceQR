// SmoothScroll.astro Lenis nüsxəsini window.qrlLenis-ə yazır, BackToTop.astro oxuyur (telefonda
// undefined). window.lenis adı işlənmir: Lenis onu özü elan edir, toqquşardı.
import type Lenis from 'lenis'

declare global {
  interface Window {
    qrlLenis?: Lenis
    // Linklərlə və "Yuxarı" düyməsi ilə sürüşmənin ortaq ayarı: SmoothScroll.astro yazır, BackToTop.astro oxuyur.
    qrlAnchorScroll?: { duration: number; easing: (progress: number) => number }
  }
}
