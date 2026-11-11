import { useEffect, useRef, type ReactNode } from 'react'

/**
 * The pieces of the approval screens that know nothing about where their data comes from: Baku-time dates, how long
 * something waited, initials, the side sheet's keyboard contract, and the loading and empty states. Shared by the
 * real screens (pages/admin/approvals) and the design prototype (pages/proto/approvals), so the two cannot drift.
 */

const MONTHS = ['yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun', 'iyul', 'avqust', 'sentyabr', 'oktyabr', 'noyabr', 'dekabr']
const BAKU_OFFSET_MS = 4 * 3_600_000

/** «11 noyabr, 10:00» in Baku time, whatever the device's own time zone. */
export function when(iso: string): string {
  const d = new Date(new Date(iso).getTime() + BAKU_OFFSET_MS)
  const hh = String(d.getUTCHours()).padStart(2, '0')
  const mm = String(d.getUTCMinutes()).padStart(2, '0')
  return `${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}, ${hh}:${mm}`
}

/** «17 may» for a date of this year, «17 may 1994» for any other. */
export function day(isoDate: string, thisYear = new Date().getFullYear()): string {
  const [y, m, dd] = isoDate.slice(0, 10).split('-').map(Number)
  return `${dd} ${MONTHS[m - 1]}${y !== thisYear ? ` ${y}` : ''}`
}

/** The Baku calendar day of a moment, as yyyy-mm-dd. */
export function bakuDay(iso: string | Date): string {
  const t = typeof iso === 'string' ? new Date(iso).getTime() : iso.getTime()
  return new Date(t + BAKU_OFFSET_MS).toISOString().slice(0, 10)
}

/** How long something has waited, in words: «40 dəqiqə», «5 saat», «1 gün 7 saat». */
export function waited(fromIso: string, now: Date): string {
  const mins = Math.max(0, Math.round((now.getTime() - new Date(fromIso).getTime()) / 60_000))
  if (mins < 60) return `${mins} dəqiqə`
  const h = Math.floor(mins / 60)
  if (h < 24) return `${h} saat`
  const d = Math.floor(h / 24)
  return h % 24 ? `${d} gün ${h % 24} saat` : `${d} gün`
}

export function initials(name: string): string {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]).join('').toUpperCase()
}

/**
 * A side sheet's keyboard contract: focus moves in when it opens (the first field, else the sheet), Esc closes it,
 * Tab stays inside, and focus goes back to whatever opened it.
 */
export function useSheet(onClose: () => void) {
  const ref = useRef<HTMLDivElement>(null)
  const close = useRef(onClose)
  close.current = onClose
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null
    const el = ref.current
    const focusable = () => el
      ? [...el.querySelectorAll<HTMLElement>('a[href], button:not([disabled]), input:not([disabled]), select, textarea, [tabindex]:not([tabindex="-1"])')]
      : []
    const first = focusable().find((x) => x.getAttribute('aria-label') !== 'Bağla')
    ;(first ?? el)?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { e.stopPropagation(); close.current() }
      if (e.key !== 'Tab') return
      const f = focusable()
      if (!f.length) return
      if (e.shiftKey && document.activeElement === f[0]) { e.preventDefault(); f[f.length - 1].focus() }
      else if (!e.shiftKey && document.activeElement === f[f.length - 1]) { e.preventDefault(); f[0].focus() }
    }
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('keydown', onKey)
      opener?.focus?.()
    }
  }, [])
  return ref
}

export function Skeleton({ rows = 6 }: { rows?: number }) {
  return (
    <div className="apx-skel" aria-busy="true" aria-label="Yüklənir">
      {Array.from({ length: rows }, (_, i) => <i key={i} />)}
    </div>
  )
}

export function EmptyState({ title, text, action }: { title: string; text: string; action?: ReactNode }) {
  return (
    <div className="apx-state">
      <h3>{title}</h3>
      <p>{text}</p>
      {action}
    </div>
  )
}
