import { platform, type GeoFailKind } from '../lib/geo'

// "Turn on GPS" is three separate switches, and employees hunt for the wrong one: the phone's global
// location toggle, the browser app's own OS permission (the one everybody misses), and the per-site
// permission inside the browser. We show all three, in order, and highlight the likely culprit.

type Step = { title: string; body: string }

const STEPS: Record<ReturnType<typeof platform>, Step[]> = {
  android: [
    { title: 'Telefonun məkanı', body: 'Ekranı yuxarıdan aşağı sürüşdürün → «Məkan» (Location) ikonunu yandırın.' },
    { title: 'Brauzerin icazəsi', body: 'Ayarlar → Tətbiqlər → Chrome → İcazələr → Məkan → «Tətbiq istifadə edilərkən».' },
    { title: 'Saytın icazəsi', body: 'Chrome-da ünvan sətrinin solundakı 🔒 işarəsi → İcazələr → Məkan → «İcazə ver».' },
  ],
  ios: [
    { title: 'Telefonun məkanı', body: 'Ayarlar → Məxfilik və Təhlükəsizlik → «Məkan Xidmətləri» → yandırın.' },
    { title: 'Brauzerin icazəsi', body: 'Həmin siyahıda aşağı sürüşdürün → «Safari Saytları» → «Soruşarkən» seçin.' },
    { title: 'Saytın icazəsi', body: 'Safari-də ünvan sətrindəki «ᴀA» → Vebsayt Ayarları → Məkan → «İcazə ver».' },
  ],
  other: [
    { title: 'Cihazın məkanı', body: 'Əməliyyat sisteminin ayarlarından məkan xidmətini yandırın.' },
    { title: 'Saytın icazəsi', body: 'Ünvan sətrindəki 🔒 işarəsi → Məkan → «İcazə ver».' },
  ],
}

/** Which step to point at. Chrome tells us the permission outright; on iOS we can't be sure, so we
 *  flag the two permission layers rather than pretend to know which one it is. */
function highlightFor(kind: GeoFailKind, os: ReturnType<typeof platform>): number[] {
  if (kind === 'denied') return os === 'other' ? [1] : [1, 2]
  if (kind === 'unavailable') return [0]
  return []
}

const HEADLINE: Record<GeoFailKind, { title: string; detail: string }> = {
  denied: {
    title: 'Məkan icazəsi verilməyib',
    detail: 'Brauzer bu sayta məkanı vermir. Aşağıdakı vurğulanmış addımları yoxlayın.',
  },
  unavailable: {
    title: 'Telefonun məkanı bağlıdır',
    detail: 'Cihazın məkan xidməti sönülüdür — əvvəlcə onu yandırın.',
  },
  timeout: {
    title: 'GPS siqnal tapmadı',
    detail: 'Açıq yerə çıxın, 10 saniyə gözləyin və yenidən yoxlayın.',
  },
  unsupported: {
    title: 'Bu brauzer məkanı dəstəkləmir',
    detail: 'Səhifəni Chrome və ya Safari ilə açın.',
  },
}

export function GpsHelp({ kind, onRetry, busy }: { kind: GeoFailKind; onRetry: () => void; busy?: boolean }) {
  const os = platform()
  const steps = STEPS[os]
  const highlight = highlightFor(kind, os)
  const { title, detail } = HEADLINE[kind]

  return (
    <div className="w-full max-w-sm rounded-2xl bg-slate-800 p-5 shadow-lg">
      <div className="text-center">
        <div className="text-5xl">📍</div>
        <h2 className="mt-2 text-lg font-bold text-white">{title}</h2>
        <p className="mt-1 text-sm text-slate-300">{detail}</p>
      </div>

      {kind !== 'timeout' && kind !== 'unsupported' && (
        <ol className="mt-5 space-y-2">
          {steps.map((step, i) => {
            const on = highlight.includes(i)
            return (
              <li
                key={step.title}
                className={`rounded-xl border p-3 ${
                  on ? 'border-amber-400/60 bg-amber-400/10' : 'border-slate-700 bg-slate-900/40'
                }`}
              >
                <div className="flex items-center gap-2">
                  <span
                    className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-bold ${
                      on ? 'bg-amber-400 text-slate-900' : 'bg-slate-700 text-slate-300'
                    }`}
                  >
                    {i + 1}
                  </span>
                  <span className={`text-sm font-semibold ${on ? 'text-amber-200' : 'text-slate-200'}`}>
                    {step.title}
                  </span>
                </div>
                <p className="mt-1.5 pl-8 text-sm leading-relaxed text-slate-400">{step.body}</p>
              </li>
            )
          })}
        </ol>
      )}

      <button
        onClick={onRetry}
        disabled={busy}
        className="mt-5 w-full rounded-lg bg-white py-3 font-semibold text-slate-900 transition hover:bg-slate-200 disabled:opacity-60"
      >
        {busy ? 'Yoxlanılır…' : 'Yenidən yoxla'}
      </button>

      <p className="mt-3 text-center text-xs text-slate-500">
        Düzəlmirsə, administratora bildirin — problem panelə də düşdü.
      </p>
    </div>
  )
}
