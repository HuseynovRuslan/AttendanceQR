import { gsap, ScrollTrigger } from './gsap'
import { findStickyAncestor, hasMotion, revertOnPageSwap } from './shared'

// ---------- Desktop only (phones and tablets keep just the reveals) ----------
const DESKTOP_MEDIA = '(min-width: 1024px)'

// ---------- Skew: [data-skew] leans with the scroll speed ----------
const MAX_SKEW_DEG = 3
// Scroll speed (px/s) that reaches MAX_SKEW_DEG; a normal wheel scroll stays around 1°.
const VELOCITY_FOR_MAX_SKEW = 3000
// Dead-zone: slower than this (px/s) counts as standing still, so a tiny drift does not lean anything.
const VELOCITY_DEAD_ZONE = 80
// How long the skew takes to follow the speed, and to settle back to 0 when scrolling stops.
const SKEW_SETTLE_SECONDS = 0.5
const SKEW_EASE = 'power3.out'
// Below this the skew counts as 0 (and will-change is removed).
const SKEW_AT_REST_DEG = 0.01
// Elements this close to the viewport are already tracked, so they never enter the screen straight.
const SKEW_TRACK_MARGIN = '25% 0px'

// ---------- Heading parallax ----------
const HEADING_PARALLAX_PERCENT = 4

function skewForVelocity(velocity: number): number {
  if (Math.abs(velocity) < VELOCITY_DEAD_ZONE) {
    return 0
  }
  const skew = (-velocity / VELOCITY_FOR_MAX_SKEW) * MAX_SKEW_DEG
  return gsap.utils.clamp(-MAX_SKEW_DEG, MAX_SKEW_DEG, skew)
}

// ---------- Scroll skew: one quickTo value, written to the elements near the screen ----------
class ScrollSkew {
  private elements: HTMLElement[]
  private nearScreen = new Set<HTMLElement>()
  private setters = new Map<HTMLElement, (value: number) => void>()
  private current = { skew: 0 }
  private skewTo: gsap.QuickToFunc
  private observer: IntersectionObserver
  private lastScrollY = window.scrollY
  private lastTarget = 0

  constructor(elements: HTMLElement[]) {
    this.elements = elements
    for (const element of elements) {
      this.setters.set(element, gsap.quickSetter(element, 'skewY', 'deg') as (value: number) => void)
    }
    this.skewTo = gsap.quickTo(this.current, 'skew', {
      duration: SKEW_SETTLE_SECONDS,
      ease: SKEW_EASE,
      onUpdate: () => this.apply(),
    })
    this.observer = new IntersectionObserver((entries) => this.trackNearScreen(entries), {
      rootMargin: SKEW_TRACK_MARGIN,
    })
    for (const element of elements) {
      this.observer.observe(element)
    }
    gsap.ticker.add(this.followScrollSpeed)
    ScrollTrigger.addEventListener('refreshInit', this.straighten)
  }

  destroy(): void {
    gsap.ticker.remove(this.followScrollSpeed)
    ScrollTrigger.removeEventListener('refreshInit', this.straighten)
    this.observer.disconnect()
    this.skewTo.tween.kill()
    this.straightenAll()
  }

  // Speed = scroll distance since the last frame (works with Lenis and with native scrolling).
  private followScrollSpeed = (_time: number, deltaMs: number): void => {
    const scrollY = window.scrollY
    const distance = scrollY - this.lastScrollY
    this.lastScrollY = scrollY
    if (deltaMs <= 0) {
      return
    }
    const target = skewForVelocity((distance / deltaMs) * 1000)
    if (target === this.lastTarget) {
      return
    }
    this.lastTarget = target
    this.skewTo(target)
  }

  private apply(): void {
    const isMoving = Math.abs(this.current.skew) > SKEW_AT_REST_DEG
    const skew = isMoving ? this.current.skew : 0
    for (const element of this.nearScreen) {
      this.setters.get(element)?.(skew)
      element.style.willChange = isMoving ? 'transform' : ''
    }
  }

  private trackNearScreen(entries: IntersectionObserverEntry[]): void {
    for (const entry of entries) {
      const element = entry.target as HTMLElement
      if (entry.isIntersecting) {
        this.nearScreen.add(element)
        this.setters.get(element)?.(this.current.skew)
      } else {
        this.nearScreen.delete(element)
        this.setters.get(element)?.(0)
        element.style.willChange = ''
      }
    }
  }

  // ScrollTrigger measures right after this: everything must be straight.
  private straighten = (): void => {
    this.lastTarget = 0
    this.skewTo(0, 0)
    this.straightenAll()
  }

  private straightenAll(): void {
    for (const element of this.elements) {
      this.setters.get(element)?.(0)
      element.style.willChange = ''
    }
  }
}

// ---------- Heading parallax (headings inside a sticky column stay put) ----------
function addHeadingParallax(heading: HTMLElement): void {
  gsap.fromTo(
    heading,
    { yPercent: HEADING_PARALLAX_PERCENT },
    {
      yPercent: -HEADING_PARALLAX_PERCENT,
      ease: 'none',
      scrollTrigger: { trigger: heading, start: 'top bottom', end: 'bottom top', scrub: true },
    }
  )
}

function startDesktopAccents(): () => void {
  const headings = document.querySelectorAll<HTMLElement>('[data-reveal="heading"]')
  for (const heading of headings) {
    if (!findStickyAncestor(heading)) {
      addHeadingParallax(heading)
    }
  }

  const skewed = Array.from(document.querySelectorAll<HTMLElement>('[data-skew]'))
  const skew = skewed.length > 0 ? new ScrollSkew(skewed) : undefined
  return () => skew?.destroy()
}

export function initScrollAccents(): void {
  if (!hasMotion()) {
    return
  }
  const media = gsap.matchMedia()
  media.add(DESKTOP_MEDIA, startDesktopAccents)
  revertOnPageSwap(media)
}
