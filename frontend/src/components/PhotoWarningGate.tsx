import { useEffect, useState } from 'react'
import { acknowledgeWarning, getPendingWarning, type PendingWarning } from '../lib/push'

/**
 * An admin's warning, put where the employee cannot miss it.
 *
 * The passive banner beside it on the home screen was not enough, and one person's record shows why:
 * twelve scans, five with no face in the photograph and one that was a picture of an actor on a
 * monitor — and on the morning of the actor, that banner read ZERO, because it counted only
 * "no face" and only within the calendar month, which had turned over two days earlier.
 *
 * So this one is not a banner. It covers the screen on open, it says who it is from, and it does not
 * go away until «Anladım» is pressed — which is also the point of it: the press is recorded, so the
 * admin stops guessing whether the message was read and knows.
 *
 * Deliberately NOT a block on scanning. The employee can dismiss it and go straight to work; a
 * warning that stopped somebody clocking in would cost them a day's pay over a photograph, which is
 * the exact trade this product refuses everywhere else.
 */
export function PhotoWarningGate() {
  const [warning, setWarning] = useState<PendingWarning | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let alive = true
    void (async () => {
      const { status, data } = await getPendingWarning()
      if (!alive) return
      if (status === 200 && data && 'id' in data) setWarning(data)
    })()
    return () => { alive = false }
  }, [])

  if (!warning) return null

  async function dismiss() {
    if (!warning) return
    setBusy(true)
    await acknowledgeWarning(warning.id)
    // Closed either way. A failed acknowledgement must not trap somebody behind a dialog on the
    // screen they opened to clock in — it will simply be shown again next time.
    setWarning(null)
    setBusy(false)
  }

  return (
    <div className="fixed inset-0 z-[9999] flex items-end justify-center bg-black/60 p-4 sm:items-center">
      <div className="w-full max-w-md rounded-3xl bg-white p-5 shadow-2xl">
        <div className="flex items-start gap-3">
          <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-red-100 text-xl">⚠️</span>
          <div className="min-w-0">
            <h2 className="text-lg font-bold text-slate-900">{warning.title}</h2>
            <p className="mt-0.5 text-xs font-semibold text-slate-500">Rəhbərlikdən</p>
          </div>
        </div>

        <p className="mt-4 whitespace-pre-line text-[15px] leading-relaxed text-slate-800">
          {warning.body}
        </p>

        <button
          type="button"
          onClick={() => void dismiss()}
          disabled={busy}
          className="mt-5 w-full rounded-2xl bg-slate-900 py-3 text-base font-bold text-white disabled:opacity-60"
        >
          {busy ? '…' : 'Anladım'}
        </button>
        {/* Said plainly. The press is the point of the whole feature — it is what turns "he probably
            did not see it" into a fact — and hiding that from the person pressing would be sly. */}
        <p className="mt-2 text-center text-xs text-slate-400">
          «Anladım» düyməsi rəhbərliyə oxuduğunuzu bildirir
        </p>
      </div>
    </div>
  )
}
