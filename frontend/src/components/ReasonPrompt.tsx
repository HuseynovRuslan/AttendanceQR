import { useState } from 'react'
import { submitAttendanceReason } from '../api/attendance'

// Preset chips so a non-technical employee taps instead of typing; "Digər" opens a short free text.
const LATE_REASONS = ['Nəqliyyat/tıxac', 'Səhhət', 'Ailə vəziyyəti', 'İcazəli', 'Digər']
const EARLY_REASONS = ['Səhhət', 'Ailə vəziyyəti', 'İcazəli', 'İş tapşırığı', 'Digər']

/**
 * Skippable reason prompt shown after a late check-in / early check-out. Preset chips + a "Digər" free
 * text; "Sonra" dismisses without recording. Never blocking — the scan is already saved either way.
 */
export function ReasonPrompt({
  recordId,
  kind,
  onDone,
}: {
  recordId: string
  kind: 'late' | 'early'
  onDone: () => void
}) {
  const [choice, setChoice] = useState('')
  const [custom, setCustom] = useState('')
  const [busy, setBusy] = useState(false)

  const reasons = kind === 'late' ? LATE_REASONS : EARLY_REASONS
  const title = kind === 'late' ? 'Niyə gec gəldiniz?' : 'Niyə tez çıxırsınız?'
  const finalReason = (choice === 'Digər' ? custom : choice).trim()

  async function submit() {
    if (!finalReason) return
    setBusy(true)
    await submitAttendanceReason(recordId, kind, finalReason) // best-effort; skippable anyway
    setBusy(false)
    onDone()
  }

  return (
    <div className="fixed inset-0 z-[70] flex items-end justify-center bg-black/50 p-0 sm:items-center sm:p-4">
      <div className="w-full max-w-sm rounded-t-3xl bg-white p-5 text-slate-900 shadow-xl sm:rounded-3xl">
        <h2 className="text-center text-lg font-bold">{title}</h2>
        <p className="mt-1 text-center text-sm text-slate-500">Səbəb seçin (istəyə bağlı)</p>

        <div className="mt-4 flex flex-wrap gap-2">
          {reasons.map((r) => (
            <button
              key={r}
              onClick={() => setChoice(r)}
              className={`rounded-full px-3 py-2 text-sm font-semibold transition ${
                choice === r ? 'bg-blue-600 text-white' : 'bg-slate-100 text-slate-700'
              }`}
            >
              {r}
            </button>
          ))}
        </div>

        {choice === 'Digər' && (
          <input
            type="text"
            value={custom}
            maxLength={200}
            autoFocus
            placeholder="Səbəbi yazın"
            onChange={(e) => setCustom(e.target.value)}
            className="mt-3 w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
          />
        )}

        <div className="mt-5 flex gap-2">
          <button onClick={onDone} className="flex-1 rounded-2xl bg-slate-100 py-3 font-semibold text-slate-600">
            Sonra
          </button>
          <button
            onClick={submit}
            disabled={busy || !finalReason}
            className="flex-1 rounded-2xl bg-blue-600 py-3 font-bold text-white transition disabled:opacity-50"
          >
            {busy ? 'Göndərilir…' : 'Göndər'}
          </button>
        </div>
      </div>
    </div>
  )
}
