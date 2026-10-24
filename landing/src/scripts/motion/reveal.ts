import { gsap, ScrollTrigger } from './gsap'
import {
  CLEARED_PROPS,
  hasMotion,
  findStickyAncestor,
  isLoaderGone,
  markReady,
  pageRevealed,
  revertOnPageSwap,
  whenFontsReady,
} from './shared'
import { splitIntoLines } from './splitLines'

// ---------- Heading ----------
const LINE_SHIFT_PERCENT = 105
const LINE_DURATION = 1
const LINE_STAGGER = 0.1
const EYEBROW_SHIFT = 12
const EYEBROW_DURATION = 0.5
const SENTENCE_SHIFT = 16
const SENTENCE_DURATION = 0.7
const SENTENCE_DELAY = 0.3

// ---------- Cards and fade ----------
const CARD_SHIFT = 48
const CARD_DURATION = 0.8
const CARD_STAGGER = 0.08
const FADE_SHIFT = 24
const FADE_DURATION = 0.8

// ---------- Count ----------
const COUNT_DURATION = 1.6

// ---------- Parallax ----------
const PARALLAX_PERCENT = 6

// ---------- Easing ----------
const REVEAL_EASE = 'expo.out'
const TEXT_EASE = 'power3.out'
const COUNT_EASE = 'power2.out'

// ---------- Direction: where an item starts and resets ----------
// Scrolling down, an item plays when its top passes 85% of the viewport height (same as REVEAL_START).
const PLAY_LINE_FROM_BELOW = 0.85
// Scrolling up, it plays when its bottom passes 15% of the viewport height (same as REVEAL_END).
const PLAY_LINE_FROM_ABOVE = 0.15
// Dead-zone: a group resets only this far outside the viewport, so small scroll jitters never replay it.
const RESET_DEAD_ZONE_PX = 40

// ---------- Inline styles ----------
const REVEAL_CLEARED_PROPS = `${CLEARED_PROPS},will-change`
const WHILE_ANIMATING = 'transform, opacity'

type Side = 'below' | 'above'

// "below" moves things down (+), "above" moves them up (−).
function towards(side: Side, distance: number): number {
  if (side === 'below') {
    return distance
  }
  return -distance
}

// ---------- One reveal type: how to hide, play and finish its elements ----------
interface RevealKind {
  hide: (elements: HTMLElement[], side: Side) => void
  play: (elements: HTMLElement[], side: Side) => gsap.core.Animation | undefined
  finish: (elements: HTMLElement[]) => void
}

interface RevealItem {
  element: HTMLElement
  state: Side | 'shown'
  playFromBelowAt: number
  playFromAboveAt: number
  bottomAt: number
}

// ---------- Layout position (without the transforms GSAP put on it) ----------
function layoutTop(element: HTMLElement): number {
  const shiftPx = Number(gsap.getProperty(element, 'y'))
  const shiftPercent = Number(gsap.getProperty(element, 'yPercent'))
  const transformShift = shiftPx + (shiftPercent / 100) * element.offsetHeight
  return element.getBoundingClientRect().top + window.scrollY - transformShift
}

// ---------- Box to measure: a sticky element slides inside its parent, so the parent is its real range ----------
// (FAQ heading: measured by itself it "left" the screen while it was still stuck on it.)
function measuredBox(element: HTMLElement): HTMLElement {
  const sticky = findStickyAncestor(element)
  if (sticky?.parentElement) {
    return sticky.parentElement
  }
  return element
}

interface BoxRange {
  top: number
  bottom: number
}

function measureRange(element: HTMLElement): BoxRange {
  const box = measuredBox(element)
  const top = layoutTop(box)
  return { top, bottom: top + box.offsetHeight }
}

// ---------- Reveal group: one ScrollTrigger, items play in either direction ----------
class RevealGroup {
  private kind: RevealKind
  private items: RevealItem[]
  private running: gsap.core.Animation[] = []
  private trigger: ScrollTrigger

  constructor(group: HTMLElement, elements: HTMLElement[], kind: RevealKind) {
    this.kind = kind
    this.items = elements.map((element) => ({
      element,
      state: 'below',
      playFromBelowAt: 0,
      playFromAboveAt: 0,
      bottomAt: 0,
    }))
    kind.hide(elements, 'below')
    markReady(group)

    // Start/end are functions: they are measured again on every refresh (sticky only applies from lg).
    this.trigger = ScrollTrigger.create({
      start: () => measureRange(group).top - window.innerHeight - RESET_DEAD_ZONE_PX,
      end: () => measureRange(group).bottom + RESET_DEAD_ZONE_PX,
      onRefresh: () => this.handleRefresh(),
      onUpdate: () => this.playCrossedItems(),
      onLeave: () => this.hideAll('above'),
      onLeaveBack: () => this.hideAll('below'),
    })
  }

  // ---------- Hidden elements were replaced (a heading split again on resize) ----------
  rehide(): void {
    for (const side of ['below', 'above'] as const) {
      const hidden = this.elementsIn(side)
      if (hidden.length > 0) {
        this.kind.hide(hidden, side)
      }
    }
  }

  // ---------- First Tab: final state, no more resets ----------
  finish(): void {
    this.trigger.kill()
    this.stopRunning()
    for (const item of this.items) {
      item.state = 'shown'
    }
    this.kind.finish(this.items.map((item) => item.element))
  }

  private elementsIn(state: RevealItem['state']): HTMLElement[] {
    return this.items.filter((item) => item.state === state).map((item) => item.element)
  }

  private measure(): void {
    const viewport = window.innerHeight
    for (const item of this.items) {
      const range = measureRange(item.element)
      item.playFromBelowAt = range.top - viewport * PLAY_LINE_FROM_BELOW
      item.playFromAboveAt = range.bottom - viewport * PLAY_LINE_FROM_ABOVE
      item.bottomAt = range.bottom
    }
  }

  // Page opened lower down (anchor, reload): items already above the viewport will come from above.
  private handleRefresh(): void {
    this.measure()
    const passed = this.items.filter((item) => item.state === 'below' && window.scrollY > item.bottomAt)
    if (passed.length > 0) {
      for (const item of passed) {
        item.state = 'above'
      }
      this.kind.hide(
        passed.map((item) => item.element),
        'above'
      )
    }
    this.playCrossedItems()
  }

  private playCrossedItems(): void {
    const scroll = window.scrollY
    const fromBelow: RevealItem[] = []
    const fromAbove: RevealItem[] = []
    for (const item of this.items) {
      if (item.state === 'below' && scroll >= item.playFromBelowAt) {
        fromBelow.push(item)
      } else if (item.state === 'above' && scroll <= item.playFromAboveAt) {
        fromAbove.push(item)
      }
    }
    // Coming from above, the last item is nearest the screen, so it goes first.
    fromAbove.reverse()
    this.play(fromBelow, 'below')
    this.play(fromAbove, 'above')
  }

  private play(items: RevealItem[], side: Side): void {
    if (items.length === 0) {
      return
    }
    for (const item of items) {
      item.state = 'shown'
    }
    if (isLoaderGone()) {
      this.startAnimation(items, side)
      return
    }
    pageRevealed.then(() => this.startAnimation(items, side))
  }

  private startAnimation(items: RevealItem[], side: Side): void {
    const stillShown = items.filter((item) => item.state === 'shown').map((item) => item.element)
    if (stillShown.length === 0) {
      return
    }
    const animation = this.kind.play(stillShown, side)
    if (animation) {
      this.running.push(animation)
    }
  }

  // Off-screen only: set the hidden state without animating.
  private hideAll(side: Side): void {
    this.stopRunning()
    for (const item of this.items) {
      item.state = side
    }
    this.kind.hide(
      this.items.map((item) => item.element),
      side
    )
  }

  private stopRunning(): void {
    for (const animation of this.running) {
      animation.kill()
    }
    this.running.length = 0
  }
}

// ---------- Keyboard: finish everything on the first Tab ----------
const groups: RevealGroup[] = []
let isKeyboardUser = false

function finishAllOnFirstTab(event: KeyboardEvent): void {
  if (event.key !== 'Tab') {
    return
  }
  isKeyboardUser = true
  document.removeEventListener('keydown', finishAllOnFirstTab)
  for (const group of groups) {
    group.finish()
  }
  groups.length = 0
}

function watch(group: HTMLElement, elements: HTMLElement[], kind: RevealKind): RevealGroup {
  const revealGroup = new RevealGroup(group, elements, kind)
  groups.push(revealGroup)
  return revealGroup
}

// ---------- Slide: cards and fade rise (or drop) into place ----------
function createSlideReveal(distance: number, duration: number, stagger: number): RevealKind {
  return {
    hide: (elements, side) => {
      gsap.set(elements, { opacity: 0, y: towards(side, distance), transition: 'none', willChange: 'auto' })
    },
    play: (elements) => {
      gsap.set(elements, { willChange: WHILE_ANIMATING })
      return gsap.to(elements, {
        opacity: 1,
        y: 0,
        duration,
        ease: REVEAL_EASE,
        stagger,
        clearProps: REVEAL_CLEARED_PROPS,
      })
    },
    finish: (elements) => {
      gsap.set(elements, { clearProps: REVEAL_CLEARED_PROPS })
    },
  }
}

const cardsReveal = createSlideReveal(CARD_SHIFT, CARD_DURATION, CARD_STAGGER)
const fadeReveal = createSlideReveal(FADE_SHIFT, FADE_DURATION, 0)

function revealCards(group: HTMLElement): void {
  const cards = Array.from(group.children) as HTMLElement[]
  watch(group, cards, cardsReveal)
}

function revealFade(element: HTMLElement): void {
  watch(element, [element], fadeReveal)
}

// ---------- Heading: eyebrow, masked title lines, sentence ----------
interface HeadingParts {
  lines: HTMLElement[]
  eyebrow: Element[]
  sentence: Element[]
}

function findTitle(container: HTMLElement): HTMLElement | null {
  if (container.matches('h1, h2')) {
    return container
  }
  return container.querySelector<HTMLElement>(':scope > h1, :scope > h2')
}

function hideHeading(heading: HeadingParts, side: Side): void {
  gsap.set(heading.lines, { yPercent: towards(side, LINE_SHIFT_PERCENT), willChange: 'auto' })
  const textState = { opacity: 0, transition: 'none', willChange: 'auto' }
  if (heading.eyebrow.length > 0) {
    gsap.set(heading.eyebrow, { ...textState, y: towards(side, EYEBROW_SHIFT) })
  }
  if (heading.sentence.length > 0) {
    gsap.set(heading.sentence, { ...textState, y: towards(side, SENTENCE_SHIFT) })
  }
}

// Coming from above, everything runs bottom-up: last line first, sentence before the eyebrow.
function playHeading(heading: HeadingParts, side: Side): gsap.core.Timeline {
  const isFromAbove = side === 'above'
  const timeline = gsap.timeline()
  gsap.set(heading.lines, { willChange: 'transform' })
  timeline.to(
    heading.lines,
    {
      yPercent: 0,
      duration: LINE_DURATION,
      ease: REVEAL_EASE,
      stagger: { each: LINE_STAGGER, from: isFromAbove ? 'end' : 'start' },
      clearProps: 'transform,will-change',
    },
    0
  )
  const textEnd = { opacity: 1, y: 0, ease: TEXT_EASE, clearProps: REVEAL_CLEARED_PROPS }
  if (heading.eyebrow.length > 0) {
    timeline.to(heading.eyebrow, { ...textEnd, duration: EYEBROW_DURATION }, isFromAbove ? SENTENCE_DELAY : 0)
  }
  if (heading.sentence.length > 0) {
    timeline.to(heading.sentence, { ...textEnd, duration: SENTENCE_DURATION }, isFromAbove ? 0 : SENTENCE_DELAY)
  }
  return timeline
}

function finishHeading(heading: HeadingParts): void {
  gsap.set(heading.lines, { clearProps: 'transform,will-change' })
  gsap.set([...heading.eyebrow, ...heading.sentence], { clearProps: REVEAL_CLEARED_PROPS })
}

function revealHeading(container: HTMLElement): void {
  const title = findTitle(container)
  if (!title) {
    markReady(container)
    return
  }

  const children = Array.from(container.children)
  const titleIndex = children.indexOf(title)
  const heading: HeadingParts = {
    lines: [],
    eyebrow: titleIndex > 0 ? children.slice(0, titleIndex) : [],
    sentence: titleIndex >= 0 ? children.slice(titleIndex + 1) : [],
  }
  const kind: RevealKind = {
    hide: (_elements, side) => hideHeading(heading, side),
    play: (_elements, side) => playHeading(heading, side),
    finish: () => finishHeading(heading),
  }
  let group: RevealGroup | undefined

  // onSplit runs now and again after a resize; new lines must get the current hidden state.
  splitIntoLines(title, (lines) => {
    heading.lines = lines
    group?.rehide()
  })
  group = watch(container, [container], kind)
}

// ---------- Count: "1 000+" counts up from 0 (only when it comes from below) ----------
interface CountParts {
  prefix: string
  suffix: string
  separator: string
  value: number
}

function isDigit(character: string): boolean {
  return character >= '0' && character <= '9'
}

function parseCount(text: string): CountParts | null {
  let first = -1
  let last = -1
  for (let index = 0; index < text.length; index++) {
    if (isDigit(text[index])) {
      if (first === -1) {
        first = index
      }
      last = index
    }
  }
  if (first === -1) {
    return null
  }

  const numberPart = text.slice(first, last + 1)
  let digits = ''
  let separator = ''
  for (const character of numberPart) {
    if (isDigit(character)) {
      digits += character
    } else if (separator === '') {
      separator = character
    }
  }
  return { prefix: text.slice(0, first), suffix: text.slice(last + 1), separator, value: Number(digits) }
}

function formatCount(value: number, parts: CountParts): string {
  const digits = String(value)
  let grouped = ''
  for (let index = 0; index < digits.length; index++) {
    const digitsLeft = digits.length - index
    if (index > 0 && parts.separator !== '' && digitsLeft % 3 === 0) {
      grouped += parts.separator
    }
    grouped += digits[index]
  }
  return parts.prefix + grouped + parts.suffix
}

// Width is locked while the number is short, so nothing next to it moves (CLS stays 0).
function lockWidth(element: HTMLElement): void {
  if (getComputedStyle(element).display === 'inline') {
    element.style.display = 'inline-block'
  }
  element.style.minWidth = `${element.getBoundingClientRect().width}px`
  element.style.textAlign = 'right'
}

function unlockWidth(element: HTMLElement): void {
  element.style.removeProperty('display')
  element.style.removeProperty('min-width')
  element.style.removeProperty('text-align')
}

function showCountStart(element: HTMLElement, parts: CountParts): void {
  lockWidth(element)
  element.textContent = formatCount(0, parts)
}

function showCountEnd(element: HTMLElement, finalText: string): void {
  element.textContent = finalText
  unlockWidth(element)
}

function countUp(element: HTMLElement, parts: CountParts, finalText: string): gsap.core.Tween {
  const counter = { value: 0 }
  return gsap.to(counter, {
    value: parts.value,
    duration: COUNT_DURATION,
    ease: COUNT_EASE,
    onUpdate: () => {
      element.textContent = formatCount(Math.round(counter.value), parts)
    },
    onComplete: () => showCountEnd(element, finalText),
  })
}

function revealCount(element: HTMLElement): void {
  const finalText = element.textContent ?? ''
  const parts = parseCount(finalText)
  if (!parts) {
    markReady(element)
    return
  }

  const kind: RevealKind = {
    hide: (_elements, side) => {
      if (side === 'below') {
        showCountStart(element, parts)
      } else {
        showCountEnd(element, finalText)
      }
    },
    play: (_elements, side) => {
      if (side === 'below') {
        return countUp(element, parts, finalText)
      }
      return undefined
    },
    finish: () => showCountEnd(element, finalText),
  }
  watch(element, [element], kind)
}

// ---------- Parallax: decorative layer drifts while scrolling ----------
function addParallax(element: HTMLElement): void {
  gsap.fromTo(
    element,
    { yPercent: PARALLAX_PERCENT },
    {
      yPercent: -PARALLAX_PERCENT,
      ease: 'none',
      scrollTrigger: { trigger: element, start: 'top bottom', end: 'bottom top', scrub: true },
    }
  )
}

// ---------- Setup (headings and counters wait for the web fonts) ----------
type Reveal = (element: HTMLElement) => void

const REVEALS_NOW: Record<string, Reveal> = {
  cards: revealCards,
  fade: revealFade,
}

const REVEALS_AFTER_FONTS: Record<string, Reveal> = {
  heading: revealHeading,
  count: revealCount,
}

function showWithoutMotion(element: HTMLElement): void {
  markReady(element)
  gsap.set(element, { clearProps: CLEARED_PROPS })
}

function setUpSafely(element: HTMLElement, reveal: Reveal): void {
  try {
    reveal(element)
  } catch (error) {
    console.error(error)
    showWithoutMotion(element)
  }
}

function setUpReveals(elements: HTMLElement[], reveals: Record<string, Reveal>): void {
  for (const element of elements) {
    const type = element.dataset.reveal ?? ''
    if (!(type in reveals)) {
      continue
    }
    if (isKeyboardUser) {
      showWithoutMotion(element)
    } else {
      setUpSafely(element, reveals[type])
    }
  }
}

function showUnknownReveals(elements: HTMLElement[]): void {
  for (const element of elements) {
    const type = element.dataset.reveal ?? ''
    if (!(type in REVEALS_NOW) && !(type in REVEALS_AFTER_FONTS)) {
      showWithoutMotion(element)
    }
  }
}

export function initReveals(): void {
  const elements = Array.from(document.querySelectorAll<HTMLElement>('[data-reveal]'))
  const context = gsap.context(() => {
    document.querySelectorAll<HTMLElement>('[data-parallax]').forEach(addParallax)
    if (hasMotion()) {
      showUnknownReveals(elements)
      setUpReveals(elements, REVEALS_NOW)
    }
  })

  if (hasMotion()) {
    whenFontsReady().then(() => {
      context.add(() => setUpReveals(elements, REVEALS_AFTER_FONTS))
    })
    document.addEventListener('keydown', finishAllOnFirstTab)
  }
  revertOnPageSwap(context)
}
