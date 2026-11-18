/**
 * How a shift is named in a picker, and what the free-hours mode is called on screen.
 *
 * The mode is «Sərbəst iş qrafiki» (Schedule.isFlexible in the code): no hours, no lateness, only the hours
 * worked. «Elastik» was the first name and read as jargon — «sərbəst iş qrafiki» is what a job advert says.
 * The long form names the mode; the short one fits a button or a badge.
 *
 * A picker shows «name · hours». A free-hours shift has no hours, so it shows the mode instead — unless the
 * admin already put it in the name, or the option would read «Sərbəst iş qrafiki · sərbəst qrafik».
 */
export const FREE_MODE = 'Sərbəst iş qrafiki'
export const FREE_SHORT = 'Sərbəst'

export interface ShiftLike {
  name: string
  isFlexible?: boolean
  shiftStart: string
  shiftEnd: string
  isOvernight?: boolean
}

/** The name already says it — «Sərbəst …», or the old «Elastik» a company may still be using. */
export const namesFreeMode = (name: string): boolean => /sərbəst|elastik/i.test(name)

/** «Gündüz · 09:00–18:00», «Barmenlər · sərbəst qrafik», «Sərbəst iş qrafiki». */
export function shiftOptionLabel(s: ShiftLike, { moon = false } = {}): string {
  if (s.isFlexible) return namesFreeMode(s.name) ? s.name : `${s.name} · sərbəst qrafik`
  return `${s.name} · ${s.shiftStart}–${s.shiftEnd}${moon && s.isOvernight ? ' 🌙' : ''}`
}

/** «Gündüz (09:00–18:00)», «Barmenlər (sərbəst qrafik)», «Sərbəst iş qrafiki». */
export function shiftParenLabel(s: ShiftLike): string {
  if (s.isFlexible) return namesFreeMode(s.name) ? s.name : `${s.name} (sərbəst qrafik)`
  return `${s.name} (${s.shiftStart}–${s.shiftEnd})`
}
