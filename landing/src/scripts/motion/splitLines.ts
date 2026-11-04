import { SplitText } from './gsap'

// ---------- Line class (mask spacing lives in Motion.astro) ----------
const LINE_CLASS = 'split-line'

// May return the lines' animation (SplitText keeps its progress across a re-split) or nothing.
type BuildLineAnimation = (lines: HTMLElement[]) => gsap.core.Animation | void

// ---------- Split a heading into masked lines (call once the font is ready) ----------
export function splitIntoLines(
  title: HTMLElement,
  buildAnimation: BuildLineAnimation,
  keepWholeSelector = ''
): SplitText {
  return SplitText.create(title, {
    type: 'lines',
    mask: 'lines',
    linesClass: LINE_CLASS,
    autoSplit: true,
    ignore: keepWholeSelector,
    onSplit: (split) => buildAnimation(split.lines as HTMLElement[]),
  })
}
