import { useEffect, useRef, useState } from 'react'
import { IconCalendar } from './icons'
import { fmtDate } from '../lib/format'

// Manual selection is capped at ~3 months so a report can't be pointed at the entire history by
// accident (and the query stays fast). Presets below never exceed this on their own.
const MAX_MANUAL_DAYS = 92

function toIso(d: Date): string {
  return d.toISOString().slice(0, 10)
}
function daysAgo(n: number): Date {
  return new Date(Date.now() - n * 86_400_000)
}
function buildPresets(): { key: string; label: string; from: string; to: string }[] {
  const today = new Date()
  const todayIso = toIso(today)
  const startOfMonth = (offsetMonths: number) => toIso(new Date(today.getFullYear(), today.getMonth() + offsetMonths, 1))
  const endOfLastMonth = toIso(new Date(today.getFullYear(), today.getMonth(), 0))
  return [
    { key: 'today', label: 'Bu gün', from: todayIso, to: todayIso },
    { key: 'yesterday', label: 'Dünən', from: toIso(daysAgo(1)), to: toIso(daysAgo(1)) },
    { key: 'last7', label: 'Son 7 gün', from: toIso(daysAgo(6)), to: todayIso },
    { key: 'last30', label: 'Son 30 gün', from: toIso(daysAgo(29)), to: todayIso },
    { key: 'thisMonth', label: 'Bu ay', from: startOfMonth(0), to: todayIso },
    { key: 'lastMonth', label: 'Keçən ay', from: startOfMonth(-1), to: endOfLastMonth },
    { key: 'last3months', label: 'Son 3 ay', from: toIso(daysAgo(MAX_MANUAL_DAYS - 1)), to: todayIso },
  ]
}

/**
 * A single button showing the current range ("dd.mm.yyyy – dd.mm.yyyy") that opens a dropdown of quick
 * presets (Bu gün / Dünən / Son 7 gün / …) plus a manual from–to pair capped at ~3 months. Selecting a
 * preset applies immediately; manual selection needs an explicit "Tətbiq et".
 */
export function DateRangePicker({
  from,
  to,
  onApply,
}: {
  from: string
  to: string
  onApply: (from: string, to: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [manual, setManual] = useState(false)
  const [manualFrom, setManualFrom] = useState(from)
  const [manualTo, setManualTo] = useState(to)
  const rootRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    function onClickOutside(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
        setOpen(false)
        setManual(false)
      }
    }
    document.addEventListener('mousedown', onClickOutside)
    return () => document.removeEventListener('mousedown', onClickOutside)
  }, [])

  const todayIso = toIso(new Date())
  const presets = buildPresets()
  const activeKey = presets.find((p) => p.from === from && p.to === to)?.key

  function selectPreset(p: { from: string; to: string }) {
    onApply(p.from, p.to)
    setOpen(false)
  }

  function startManual() {
    setManualFrom(from)
    setManualTo(to)
    setManual(true)
  }

  function applyManual() {
    let f = manualFrom
    const t = manualTo
    // Clamp silently to the cap rather than rejecting — keeps the end date the user picked.
    if ((new Date(t).getTime() - new Date(f).getTime()) / 86_400_000 > MAX_MANUAL_DAYS) {
      f = toIso(new Date(new Date(t).getTime() - MAX_MANUAL_DAYS * 86_400_000))
    }
    onApply(f, t)
    setOpen(false)
    setManual(false)
  }

  return (
    <div ref={rootRef} style={{ position: 'relative' }}>
      <label className="form-label">Tarix aralığı</label>
      <button
        type="button"
        className="inp"
        onClick={() => setOpen((v) => !v)}
        style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10, minWidth: 230, cursor: 'pointer' }}
      >
        <span className="mono">{fmtDate(from)} – {fmtDate(to)}</span>
        <IconCalendar style={{ width: 15, height: 15, color: 'var(--c400)', flexShrink: 0 }} />
      </button>

      {open && (
        <div
          className="card"
          style={{ position: 'absolute', top: '110%', left: 0, minWidth: 230, zIndex: 40, padding: 6 }}
        >
          {!manual ? (
            <>
              {presets.map((p) => (
                <button
                  key={p.key}
                  type="button"
                  onClick={() => selectPreset(p)}
                  className={`dropdown-item${activeKey === p.key ? ' active' : ''}`}
                >
                  {p.label}
                </button>
              ))}
              <button type="button" onClick={startManual} className="dropdown-item">
                Əl ilə seçim (maks. 3 ay)
              </button>
            </>
          ) : (
            <div style={{ padding: 8, display: 'flex', flexDirection: 'column', gap: 10 }}>
              <div>
                <label className="form-label">Başlanğıc</label>
                <input
                  className="inp"
                  type="date"
                  value={manualFrom}
                  max={manualTo}
                  onChange={(e) => setManualFrom(e.target.value)}
                />
              </div>
              <div>
                <label className="form-label">Son</label>
                <input
                  className="inp"
                  type="date"
                  value={manualTo}
                  max={todayIso}
                  onChange={(e) => setManualTo(e.target.value)}
                />
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                <button type="button" className="btn btn-sm" onClick={() => setManual(false)}>← Geri</button>
                <button type="button" className="btn btn-primary btn-sm" onClick={applyManual} style={{ flex: 1 }}>
                  Tətbiq et
                </button>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
