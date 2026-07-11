// The pre-scan verification, made visible. The three checks (device binding, location, camera) really
// do run before a scan; this just animates them one after another so it reads as a deliberate,
// thorough process rather than a hidden pause. Honest: each row reflects a real result.

export type CheckStep = 'idle' | 'run' | 'ok' | 'warn' | 'fail'

export interface ScanChecks {
  device: CheckStep
  location: CheckStep
  camera: CheckStep
}

const ICONS: Record<string, string> = {
  device: '<rect x="7" y="2" width="10" height="20" rx="2"/><circle cx="12" cy="18" r="1" fill="currentColor" stroke="none"/>',
  location: '<path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0z"/><circle cx="12" cy="10" r="3"/>',
  camera: '<path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/><circle cx="12" cy="13" r="4"/>',
}

const ROWS: { key: keyof ScanChecks; label: string; hint: string }[] = [
  { key: 'device', label: 'Cihaz təsdiqlənir', hint: 'Bu telefon hesabınıza bağlıdır' },
  { key: 'location', label: 'Məkan yoxlanılır', hint: 'İş yerində olduğunuz təsdiqlənir' },
  { key: 'camera', label: 'Kamera hazırlanır', hint: 'QR skanı üçün kamera açılır' },
]

function Indicator({ step }: { step: CheckStep }) {
  if (step === 'ok' || step === 'warn') {
    const color = step === 'ok' ? 'bg-green-500' : 'bg-blue-500'
    return (
      <span className={`check-pop grid h-8 w-8 place-items-center rounded-full ${color} text-white`}>
        <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round"><path d="M20 6 9 17l-5-5" /></svg>
      </span>
    )
  }
  if (step === 'fail') {
    return (
      <span className="check-pop grid h-8 w-8 place-items-center rounded-full bg-red-500 text-white">
        <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round"><path d="M6 6l12 12M18 6L6 18" /></svg>
      </span>
    )
  }
  if (step === 'run') {
    return <span className="spinner h-8 w-8 rounded-full border-[3px] border-white/20 border-t-white" />
  }
  return <span className="h-8 w-8 rounded-full border-[3px] border-white/10" />
}

export function ScanChecklist({ checks }: { checks: ScanChecks }) {
  return (
    <div className="absolute inset-0 z-40 flex flex-col items-center justify-center gap-6 bg-slate-900 px-6">
      <p className="text-lg font-semibold text-slate-200">Yoxlanılır…</p>

      <div className="w-full max-w-sm space-y-3">
        {ROWS.map((row) => {
          const step = checks[row.key]
          const active = step !== 'idle'
          return (
            <div
              key={row.key}
              className={`flex items-center gap-4 rounded-2xl border p-4 transition-all duration-500 ${
                active ? 'border-slate-700 bg-slate-800 opacity-100' : 'border-slate-800 bg-slate-800/40 opacity-40'
              }`}
            >
              <span
                className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl ${
                  step === 'ok' ? 'bg-green-500/15 text-green-400'
                  : step === 'warn' ? 'bg-blue-500/15 text-blue-400'
                  : step === 'fail' ? 'bg-red-500/15 text-red-400'
                  : 'bg-slate-700/50 text-slate-400'
                }`}
              >
                <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" dangerouslySetInnerHTML={{ __html: ICONS[row.key] }} />
              </span>
              <div className="min-w-0 flex-1">
                <div className="font-semibold text-white">{row.label}</div>
                <div className="text-xs text-slate-400">{row.hint}</div>
              </div>
              <Indicator step={step} />
            </div>
          )
        })}
      </div>

      <style>{`
        @keyframes spin { to { transform: rotate(360deg); } }
        .spinner { animation: spin 0.7s linear infinite; }
        @keyframes pop { 0% { transform: scale(0.4); opacity: 0; } 60% { transform: scale(1.15); } 100% { transform: scale(1); opacity: 1; } }
        .check-pop { animation: pop 0.35s cubic-bezier(0.34, 1.56, 0.64, 1); }
      `}</style>
    </div>
  )
}
