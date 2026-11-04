import { ScrollTrigger } from './gsap'

// ---------- Scroll positions ----------
export const REVEAL_START = 'top 85%'
export const REVEAL_END = 'bottom 15%'

// ---------- Inline styles removed after a reveal ----------
export const CLEARED_PROPS = 'opacity,transform,transition'

// ---------- Class and attribute names ----------
const MOTION_CLASS = 'has-motion'
const LOADER_LOCK_CLASS = 'qrl-lock'
const READY_ATTRIBUTE = 'data-motion-ready'

// ---------- Motion switch ----------
export function hasMotion(): boolean {
  return document.documentElement.classList.contains(MOTION_CLASS)
}

export function markReady(element: Element): void {
  element.setAttribute(READY_ATTRIBUTE, '')
}

// ---------- Sticky: the element itself or its nearest sticky parent (null if none) ----------
export function findStickyAncestor(element: HTMLElement): HTMLElement | null {
  let current: HTMLElement | null = element
  while (current && current !== document.body) {
    if (getComputedStyle(current).position === 'sticky') {
      return current
    }
    current = current.parentElement
  }
  return null
}

// ---------- Page loader ----------
function whenRootClassRemoved(className: string): Promise<void> {
  const root = document.documentElement
  return new Promise((resolve) => {
    if (!root.classList.contains(className)) {
      resolve()
      return
    }
    const observer = new MutationObserver(() => {
      if (root.classList.contains(className)) {
        return
      }
      observer.disconnect()
      resolve()
    })
    observer.observe(root, { attributes: true, attributeFilter: ['class'] })
  })
}

export const pageRevealed: Promise<void> = whenRootClassRemoved(LOADER_LOCK_CLASS)

export function isLoaderGone(): boolean {
  return !document.documentElement.classList.contains(LOADER_LOCK_CLASS)
}

// ---------- Fonts (time-limited: a slow font must not hold the reveals back) ----------
const TITLE_FONT = '600 1em Onest'
const TITLE_FONT_WAIT_MS = 1500
const ALL_FONTS_WAIT_MS = 3000

function waitMs(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}

export function whenTitleFontReady(): Promise<void> {
  const fontLoaded = document.fonts.load(TITLE_FONT).then(
    () => undefined,
    () => undefined
  )
  return Promise.race([fontLoaded, waitMs(TITLE_FONT_WAIT_MS)])
}

export function whenFontsReady(): Promise<void> {
  const fontsLoaded = document.fonts.ready.then(() => undefined)
  return Promise.race([fontsLoaded, waitMs(ALL_FONTS_WAIT_MS)])
}

// ---------- Astro view transitions ----------
export function revertOnPageSwap(context: gsap.Context | gsap.MatchMedia): void {
  document.addEventListener('astro:before-swap', () => context.revert(), { once: true })
}

// ---------- Address bar anchor ----------
function alignToHashWhileLoaderIsOn(): void {
  const isLoaderOn = document.documentElement.classList.contains(LOADER_LOCK_CLASS)
  if (!location.hash || !isLoaderOn) {
    return
  }
  const target = document.getElementById(location.hash.slice(1))
  if (target) {
    target.scrollIntoView({ block: 'start', behavior: 'instant' })
  }
}

// ---------- Refresh: once, after fonts, images and the page-load intro (a refresh mid-intro drops frames) ----------
const INTRO_LENGTH_MS = 2400

function whenWindowLoaded(): Promise<void> {
  return new Promise((resolve) => {
    if (document.readyState === 'complete') {
      resolve()
      return
    }
    window.addEventListener('load', () => resolve(), { once: true })
  })
}

function whenIntroFinished(): Promise<void> {
  return pageRevealed.then(() => waitMs(INTRO_LENGTH_MS))
}

export function refreshWhenPageSettles(): void {
  ScrollTrigger.config({ autoRefreshEvents: 'visibilitychange,DOMContentLoaded,resize' })
  ScrollTrigger.addEventListener('refresh', alignToHashWhileLoaderIsOn)

  Promise.all([document.fonts.ready, whenWindowLoaded(), whenIntroFinished()]).then(() => ScrollTrigger.refresh())
  window.addEventListener('pageshow', refreshAfterBackForwardCache)
}

// ---------- Back/forward cache: one refresh, after Lenis has resynced (SmoothScroll.astro) ----------
function refreshAfterBackForwardCache(event: PageTransitionEvent): void {
  if (!event.persisted) {
    return
  }
  requestAnimationFrame(() => ScrollTrigger.refresh())
}
