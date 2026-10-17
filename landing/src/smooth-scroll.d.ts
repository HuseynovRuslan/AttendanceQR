// SmoothScroll.astro Lenis nüsxəsini window.qrlLenis-ə yazır ki, BackToTop.astro onu işlədə bilsin.
// Lenis yaradılmayıbsa (telefon, "hərəkəti azalt"), window.qrlLenis undefined olur — tipdəki "?" bunu
// bildirir. window.lenis adı qəsdən işlənmir: Lenis onu öz bayraq obyekti üçün yazır və öz tipini də
// orada elan edir, ikisi toqquşardı.
// https://www.typescriptlang.org/docs/handbook/declaration-merging.html#global-augmentation
import type Lenis from 'lenis'

declare global {
  interface Window {
    qrlLenis?: Lenis
    // Linklərlə və "Yuxarı" düyməsi ilə sürüşmənin ortaq ayarı (müddət + əyri): SmoothScroll.astro yazır,
    // BackToTop.astro oxuyur. qrlLenis kimi, Lenis yaradılmayıbsa undefined olur.
    qrlAnchorScroll?: { duration: number; easing: (progress: number) => number }
  }
}
