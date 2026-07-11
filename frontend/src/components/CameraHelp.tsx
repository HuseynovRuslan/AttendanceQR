import { platform } from '../lib/geo'

// "Give camera permission" is, like GPS, several separate switches, and the one people can never find
// is the PER-SITE permission tucked next to the address bar. We surface it first, highlighted, with
// the OS app-permission and a "use Chrome" nudge behind it.

export type CameraFailKind = 'denied' | 'notfound' | 'inuse' | 'insecure'

/** Read the reason out of whatever html5-qrcode / getUserMedia threw. Defaults to 'denied' — by far
 *  the most common, and the browsers are inconsistent about the error name. */
export function cameraFailKind(err: unknown): CameraFailKind {
  const name = (err as { name?: string } | null)?.name ?? ''
  const msg = String((err as { message?: string } | null)?.message ?? err ?? '').toLowerCase()
  if (name === 'NotFoundError' || name === 'DevicesNotFoundError' || msg.includes('not found') || msg.includes('notfound'))
    return 'notfound'
  if (name === 'NotReadableError' || name === 'TrackStartError' || msg.includes('in use') || msg.includes('notreadable'))
    return 'inuse'
  if (typeof window !== 'undefined' && window.isSecureContext === false) return 'insecure'
  return 'denied'
}

type Step = { title: string; body: string }

const STEPS: Record<ReturnType<typeof platform>, Step[]> = {
  android: [
    {
      title: 'Saytın icazəsi',
      body: 'Ünvan sətrinin solundakı 🔒 (kilid) işarəsinə basın → «İcazələr» → «Kamera» → «İcazə ver». Sonra «Yenidən skan et».',
    },
    {
      title: 'Brauzerin icazəsi',
      body: 'Ayarlar → Tətbiqlər → Chrome → İcazələr → Kamera → «İcazə ver».',
    },
    {
      title: 'Chrome istifadə edin',
      body: 'Mi Browser və ya WhatsApp-ın içindəki brauzerlə deyil — səhifəni Chrome-da açın.',
    },
  ],
  ios: [
    {
      title: 'Saytın icazəsi',
      body: 'Ünvan sətrindəki «ᴀA» işarəsinə basın → «Vebsayt Ayarları» → «Kamera» → «İcazə ver».',
    },
    {
      title: 'Brauzerin icazəsi',
      body: 'Ayarlar → Safari → Kamera → «Soruş» və ya «İcazə ver».',
    },
    {
      title: 'Safari istifadə edin',
      body: 'Səhifəni Safari-də açın (başqa brauzerin içindəki pəncərədə yox).',
    },
  ],
  other: [
    {
      title: 'Saytın icazəsi',
      body: 'Ünvan sətrindəki 🔒 işarəsi → Kamera → «İcazə ver».',
    },
  ],
}

const HEADLINE: Record<CameraFailKind, { title: string; detail: string; showSteps: boolean }> = {
  denied: {
    title: 'Kamera icazəsi verilməyib',
    detail: 'QR skan üçün kamera lazımdır. Aşağıdakı vurğulanmış addımı yoxlayın.',
    showSteps: true,
  },
  inuse: {
    title: 'Kamera başqa proqramda açıqdır',
    detail: 'Kamera, video zəng və ya başqa proqramı bağlayın, sonra yenidən cəhd edin.',
    showSteps: false,
  },
  notfound: {
    title: 'Kamera tapılmadı',
    detail: 'Bu cihazda arxa kamera görünmür. Başqa telefondan cəhd edin.',
    showSteps: false,
  },
  insecure: {
    title: 'Təhlükəsiz bağlantı yoxdur',
    detail: 'Səhifəni https://bax.qrlog.az ünvanından açın.',
    showSteps: false,
  },
}

export function CameraHelp({
  kind,
  onRetry,
}: {
  kind: CameraFailKind
  onRetry: () => void
}) {
  const os = platform()
  const steps = STEPS[os]
  const { title, detail, showSteps } = HEADLINE[kind]

  return (
    <div className="w-full max-w-sm rounded-2xl bg-slate-800 p-5 shadow-lg">
      <div className="text-center">
        <div className="text-5xl">📷</div>
        <h2 className="mt-2 text-lg font-bold text-white">{title}</h2>
        <p className="mt-1 text-sm text-slate-300">{detail}</p>
      </div>

      {showSteps && (
        <ol className="mt-5 space-y-2">
          {steps.map((step, i) => {
            const on = i === 0 // the per-site permission — the one people can never find
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
        className="mt-5 w-full rounded-lg bg-white py-3 font-semibold text-slate-900 transition hover:bg-slate-200"
      >
        Yenidən skan et
      </button>

      <p className="mt-3 text-center text-xs text-slate-500">
        Düzəlmirsə, administratora bildirin.
      </p>
    </div>
  )
}
