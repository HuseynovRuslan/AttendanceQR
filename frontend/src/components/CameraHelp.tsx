import { platform, type Platform } from '../lib/geo'

/**
 * What to do when the camera will not open.
 *
 * This screen used to tell everybody the same thing: "camera permission is not granted", with three
 * lines of text. It was a guess — cameraFailKind() defaulted to 'denied' because that is the common
 * case — and the guess sent people who had already granted every permission around the same three
 * settings screens a second time. One employee tried eight times in seventy minutes and was marked
 * absent for a day she worked.
 *
 * So: name the real reason when the browser tells us one, and when it does not, say so and show all
 * three things it could be instead of asserting the most likely. And show the settings screens as
 * pictures — the per-site permission is a lock icon nobody has ever noticed, and it is a great deal
 * easier to recognise a drawing of it than to parse "ünvan sətrinin solundakı 🔒 işarəsi".
 */

export type CameraFailKind = 'denied' | 'inuse' | 'notfound' | 'insecure' | 'loadfailed' | 'unknown'

/**
 * The reason filed against the employee's record for each kind.
 *
 * Every camera failure used to arrive as one label, `CameraBlocked`, so the Problems screen could say
 * that a scan failed but never why — the admin had to telephone and ask. These are the same events,
 * separated. `CameraBlocked` stays valid on the server: phones run a cached copy of this app for
 * days, and a queued offline report can be sent long after.
 */
export const CAMERA_FAIL_REASON: Record<CameraFailKind, string> = {
  denied: 'CameraDenied',
  inuse: 'CameraInUse',
  notfound: 'CameraNotFound',
  insecure: 'CameraInsecure',
  loadfailed: 'ScannerLoadFailed',
  unknown: 'CameraFailed',
}

/**
 * Read the reason out of whatever html5-qrcode / getUserMedia threw.
 *
 * Browsers disagree about error names and html5-qrcode sometimes rejects with a bare string, so both
 * the name and the message are examined. Anything unrecognised is 'unknown' — deliberately NOT
 * 'denied'. Guessing wrong here is worse than admitting it: it sends someone to a settings screen
 * that is already correct, and they conclude the app is broken.
 */
export function cameraFailKind(err: unknown): CameraFailKind {
  const name = (err as { name?: string } | null)?.name ?? ''
  const msg = String((err as { message?: string } | null)?.message ?? err ?? '').toLowerCase()

  // Definitive regardless of what was thrown: without a secure context the API is simply absent.
  if (typeof window !== 'undefined' && window.isSecureContext === false) return 'insecure'

  if (name === 'NotFoundError' || name === 'DevicesNotFoundError' ||
      msg.includes('notfounderror') || msg.includes('not found') || msg.includes('no camera'))
    return 'notfound'

  // Held by another app, or by a stream this page has not released yet. AbortError is how Firefox
  // reports the same thing.
  if (name === 'NotReadableError' || name === 'TrackStartError' || name === 'AbortError' ||
      msg.includes('notreadable') || msg.includes('in use') || msg.includes('could not start') ||
      msg.includes('starting videosource'))
    return 'inuse'

  if (name === 'NotAllowedError' || name === 'PermissionDeniedError' || name === 'SecurityError' ||
      msg.includes('notallowed') || msg.includes('permission') || msg.includes('denied') ||
      msg.includes('disallowed'))
    return 'denied'

  // The QR library is loaded on demand and awaited inside the same try as the camera, so a failed
  // chunk fetch on a weak connection used to be reported — and explained — as a camera fault. No
  // amount of permission-fixing helps; the phone needs a connection.
  if (msg.includes('dynamically imported module') || msg.includes('failed to fetch') ||
      msg.includes('importing a module script') || msg.includes('chunk'))
    return 'loadfailed'

  return 'unknown'
}

// ── Pictures ────────────────────────────────────────────────────────────────────────────────────
// Drawn rather than photographed: a screenshot is one phone, one Android version and one language,
// and it is a file to ship. These carry the shape people actually look for — where on the screen,
// which icon, which row — and stay legible at the size the card gives them.

const C = {
  bg: '#0f172a', // slate-900, one step darker than the card
  chrome: '#1e293b',
  line: '#334155',
  muted: '#475569',
  text: '#94a3b8',
  accent: '#fbbf24', // amber-400 — the thing to look at
  accentSoft: 'rgba(251,191,36,0.16)',
  ok: '#34d399',
  bad: '#f87171',
}

function Frame({ children }: { children: React.ReactNode }) {
  return (
    <svg viewBox="0 0 240 108" className="mt-2.5 w-full" role="img" aria-hidden="true">
      <rect x="0" y="0" width="240" height="108" rx="10" fill={C.bg} />
      {children}
    </svg>
  )
}

/** A tap: the ring people read as "press here". */
function Tap({ x, y }: { x: number; y: number }) {
  return (
    <>
      <circle cx={x} cy={y} r="13" fill="none" stroke={C.accent} strokeWidth="1.5" opacity="0.5" />
      <circle cx={x} cy={y} r="8.5" fill="none" stroke={C.accent} strokeWidth="2" />
    </>
  )
}

/** The per-site permission — the address bar, and the sheet that opens from it. */
function ArtSitePermission({ os }: { os: Platform }) {
  return (
    <Frame>
      {/* address bar */}
      <rect x="14" y="12" width="212" height="22" rx="11" fill={C.chrome} />
      {os === 'ios' ? (
        <text x="27" y="27" fontSize="10" fontWeight="700" fill={C.accent} textAnchor="middle">AA</text>
      ) : (
        <>
          {/* padlock */}
          <rect x="22" y="21" width="10" height="7" rx="1.5" fill={C.accent} />
          <path d="M24.5 21v-2a2.5 2.5 0 0 1 5 0v2" fill="none" stroke={C.accent} strokeWidth="1.6" />
        </>
      )}
      <rect x="40" y="19" width="86" height="7" rx="3.5" fill={C.muted} />
      <Tap x={27} y={23} />

      {/* the sheet it opens */}
      <rect x="34" y="46" width="150" height="50" rx="8" fill={C.chrome} stroke={C.line} />
      <rect x="44" y="55" width="52" height="6" rx="3" fill={C.muted} />
      <rect x="40" y="68" width="138" height="22" rx="6" fill={C.accentSoft} stroke={C.accent} strokeWidth="1.2" />
      {/* camera glyph */}
      <rect x="48" y="74" width="14" height="10" rx="2.5" fill="none" stroke={C.accent} strokeWidth="1.4" />
      <circle cx="55" cy="79" r="2.6" fill="none" stroke={C.accent} strokeWidth="1.4" />
      <rect x="68" y="76" width="40" height="6" rx="3" fill={C.accent} opacity="0.85" />
      {/* switch, on */}
      <rect x="146" y="74" width="22" height="11" rx="5.5" fill={C.accent} />
      <circle cx="162.5" cy="79.5" r="4" fill={C.bg} />
    </Frame>
  )
}

/** The OS app permission — Settings → Apps → browser → Permissions → Camera. */
function ArtAppPermission() {
  return (
    <Frame>
      <rect x="14" y="12" width="212" height="18" rx="6" fill={C.chrome} />
      <rect x="24" y="18" width="58" height="6" rx="3" fill={C.text} />

      {[0, 1, 2].map((i) => {
        const y = 38 + i * 22
        const on = i === 1
        return (
          <g key={i}>
            <rect
              x="14" y={y} width="212" height="18" rx="6"
              fill={on ? C.accentSoft : 'none'}
              stroke={on ? C.accent : C.line}
              strokeWidth={on ? 1.2 : 1}
            />
            {on ? (
              <>
                <rect x="24" y={y + 4} width="13" height="10" rx="2.5" fill="none" stroke={C.accent} strokeWidth="1.4" />
                <circle cx="30.5" cy={y + 9} r="2.5" fill="none" stroke={C.accent} strokeWidth="1.4" />
                <rect x="45" y={y + 6} width="46" height="6" rx="3" fill={C.accent} opacity="0.85" />
                <rect x="188" y={y + 4} width="22" height="11" rx="5.5" fill={C.accent} />
                <circle cx="204.5" cy={y + 9.5} r="4" fill={C.bg} />
              </>
            ) : (
              <>
                <circle cx="30" cy={y + 9} r="5" fill={C.line} />
                <rect x="45" y={y + 6} width="52" height="6" rx="3" fill={C.muted} />
                <rect x="188" y={y + 4} width="22" height="11" rx="5.5" fill={C.line} />
                <circle cx="193.5" cy={y + 9.5} r="4" fill={C.muted} />
              </>
            )}
          </g>
        )
      })}
    </Frame>
  )
}

/** Which browser: the real one, not the window inside another app. */
function ArtBrowser({ os }: { os: Platform }) {
  const good = os === 'ios' ? 'Safari' : 'Chrome'
  return (
    <Frame>
      {/* wrong: a page inside another app — note the app's own bar above it */}
      <g>
        <rect x="24" y="20" width="80" height="70" rx="9" fill={C.chrome} stroke={C.line} />
        <rect x="24" y="20" width="80" height="16" rx="9" fill={C.line} />
        <rect x="24" y="28" width="80" height="8" fill={C.line} />
        <rect x="32" y="25" width="26" height="6" rx="3" fill={C.muted} />
        <rect x="34" y="46" width="60" height="5" rx="2.5" fill={C.muted} />
        <rect x="34" y="56" width="44" height="5" rx="2.5" fill={C.muted} />
        <circle cx="64" cy="76" r="9" fill="none" stroke={C.bad} strokeWidth="1.8" />
        <path d="M60 72l8 8M68 72l-8 8" stroke={C.bad} strokeWidth="1.8" strokeLinecap="round" />
      </g>

      <path d="M112 55h16" stroke={C.muted} strokeWidth="1.5" strokeLinecap="round" />
      <path d="M124 51l4 4-4 4" fill="none" stroke={C.muted} strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />

      {/* right: the browser itself */}
      <g>
        <rect x="136" y="20" width="80" height="70" rx="9" fill={C.chrome} stroke={C.accent} strokeWidth="1.2" />
        <rect x="144" y="27" width="64" height="12" rx="6" fill={C.bg} />
        <text x="176" y="36" fontSize="7.5" fontWeight="700" fill={C.accent} textAnchor="middle">{good}</text>
        <rect x="146" y="48" width="60" height="5" rx="2.5" fill={C.muted} />
        <rect x="146" y="58" width="44" height="5" rx="2.5" fill={C.muted} />
        <circle cx="176" cy="76" r="9" fill="none" stroke={C.ok} strokeWidth="1.8" />
        <path d="M172 76l3 3.5 5.5-6" fill="none" stroke={C.ok} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
      </g>
    </Frame>
  )
}

/** The camera is held by something else: clear the open apps, then restart. */
function ArtRestart() {
  return (
    <Frame>
      {/* two app cards being swiped away */}
      <rect x="26" y="30" width="52" height="58" rx="8" fill={C.chrome} stroke={C.line} />
      <rect x="34" y="40" width="36" height="5" rx="2.5" fill={C.muted} />
      <rect x="34" y="50" width="24" height="5" rx="2.5" fill={C.muted} />
      <rect x="86" y="30" width="52" height="58" rx="8" fill={C.chrome} stroke={C.line} />
      {/* a camera glyph on the second card — the app holding it */}
      <rect x="97" y="46" width="20" height="14" rx="3" fill="none" stroke={C.bad} strokeWidth="1.6" />
      <circle cx="107" cy="53" r="3.6" fill="none" stroke={C.bad} strokeWidth="1.6" />
      {/* swiped away, above both cards */}
      <path d="M82 26V10" stroke={C.accent} strokeWidth="1.8" strokeLinecap="round" strokeDasharray="4 4" />
      <path d="M77 16l5-6 5 6" fill="none" stroke={C.accent} strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />

      <path d="M150 57h14" stroke={C.muted} strokeWidth="1.5" strokeLinecap="round" />
      <path d="M160 53l4 4-4 4" fill="none" stroke={C.muted} strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />

      {/* power: a dashed ring leaves the gap at the top, which is deterministic — an arc path here
          depends on the sweep flag putting the opening on the right side, and it did not. */}
      <circle cx="196" cy="57" r="20" fill="none" stroke={C.line} strokeWidth="2" />
      <circle
        cx="196" cy="57" r="11" fill="none" stroke={C.accent} strokeWidth="2.2"
        strokeDasharray="52 17" transform="rotate(-45 196 57)"
      />
      <path d="M196 43v14" stroke={C.accent} strokeWidth="2.2" strokeLinecap="round" />
    </Frame>
  )
}

// ── Steps ───────────────────────────────────────────────────────────────────────────────────────

type StepId = 'site' | 'app' | 'browser' | 'restart'
type Step = { title: string; body: string }

const STEPS: Record<Platform, Record<StepId, Step>> = {
  android: {
    site: {
      title: 'Saytın icazəsi',
      body: 'Ünvan sətrinin solundakı kilid işarəsinə basın → «İcazələr» → «Kamera» → «İcazə ver».',
    },
    app: {
      title: 'Brauzerin icazəsi',
      body: 'Ayarlar → Tətbiqlər → Chrome (və ya Samsung Internet) → İcazələr → Kamera → «İcazə ver».',
    },
    browser: {
      title: 'Düzgün brauzer',
      body: 'WhatsApp və ya Instagram-ın içindəki pəncərədə kamera işləmir. Linki Chrome-da açın.',
    },
    restart: {
      title: 'Kameranı boşaldın',
      body: 'Kamera, video zəng və QRLog-un digər tablarını bağlayın, sonra telefonu söndürüb yandırın.',
    },
  },
  ios: {
    site: {
      title: 'Saytın icazəsi',
      body: 'Ünvan sətrindəki «ᴀA» işarəsinə basın → «Vebsayt Ayarları» → «Kamera» → «İcazə ver».',
    },
    app: {
      title: 'Brauzerin icazəsi',
      body: 'Ayarlar → Safari → Kamera → «Soruş» və ya «İcazə ver».',
    },
    browser: {
      title: 'Düzgün brauzer',
      body: 'Səhifəni Safari-də açın — başqa tətbiqin içindəki pəncərədə kamera açılmır.',
    },
    restart: {
      title: 'Kameranı boşaldın',
      body: 'Kamera və video zəng tətbiqlərini bağlayın, sonra telefonu söndürüb yandırın.',
    },
  },
  other: {
    site: { title: 'Saytın icazəsi', body: 'Ünvan sətrindəki kilid işarəsi → Kamera → «İcazə ver».' },
    app: { title: 'Brauzerin icazəsi', body: 'Sistem ayarlarından brauzerə kamera icazəsi verin.' },
    browser: { title: 'Düzgün brauzer', body: 'Səhifəni Chrome və ya Safari-də açın.' },
    restart: { title: 'Kameranı boşaldın', body: 'Kameradan istifadə edən digər proqramları bağlayın.' },
  },
}

function art(id: StepId, os: Platform) {
  if (id === 'site') return <ArtSitePermission os={os} />
  if (id === 'app') return <ArtAppPermission />
  if (id === 'browser') return <ArtBrowser os={os} />
  return <ArtRestart />
}

/** A crew phone can record someone whose own phone will not cooperate — worth saying on the two
 *  screens where the phone may never be fixed today, and nowhere else. */
const CREW_NOTE =
  'Bu gün qeyd olunmaq üçün: yanınızdakı işçinin telefonunda Profil → adın yanındakı ⌄ → «Hesab əlavə et» ilə öz hesabınıza keçib skan edin.'

type Headline = {
  title: string
  detail: string
  /** Which remedies to show, in order. Empty means the text above is the whole answer. */
  steps: StepId[]
  note?: string
}

function headline(kind: CameraFailKind): Headline {
  switch (kind) {
    case 'denied':
      return {
        title: 'Kamera icazəsi verilməyib',
        detail: 'Brauzer kameraya icazə verilmədiyini bildirdi. Vurğulanmış addımdan başlayın.',
        steps: ['site', 'app', 'browser'],
      }
    case 'inuse':
      return {
        title: 'Kamera başqa proqramda açıqdır',
        detail: 'Kamera hazırda başqa tətbiq tərəfindən tutulub — icazə ilə bağlı deyil.',
        steps: ['restart'],
      }
    case 'notfound':
      return {
        title: 'Kamera tapılmadı',
        detail: 'Bu cihazda kamera görünmür.',
        steps: [],
        note: CREW_NOTE,
      }
    case 'insecure':
      return {
        title: 'Təhlükəsiz bağlantı yoxdur',
        // The address was once hard-coded to one company's subdomain, which told every OTHER
        // company's employees to open a host that is not theirs. The page knows where it is.
        detail: `Səhifəni https://${typeof location === 'undefined' ? 'qrlog.az' : location.host} ünvanından açın.`,
        steps: [],
      }
    case 'loadfailed':
      return {
        title: 'Skan proqramı yüklənmədi',
        detail: 'İnternet bağlantısı zəifdir — kamera ilə bağlı problem deyil. Şəbəkəni yoxlayıb yenidən cəhd edin.',
        steps: [],
      }
    default:
      return {
        title: 'Kamera açılmadı',
        // Said plainly. The alternative — asserting the most likely cause — is what sent people who
        // had already granted every permission back through the same settings screens.
        detail: 'Səbəbi dəqiq müəyyən etmək mümkün olmadı. Aşağıdakı üç ehtimalı sıra ilə yoxlayın.',
        steps: ['site', 'restart', 'browser'],
        note: CREW_NOTE,
      }
  }
}

export function CameraHelp({ kind, onRetry }: { kind: CameraFailKind; onRetry: () => void }) {
  const os = platform()
  const { title, detail, steps, note } = headline(kind)
  // With a named cause the first remedy is the one to try; with an unknown cause there is no first,
  // and highlighting one would be the same guess this screen exists to stop making.
  const highlight = kind === 'unknown' ? -1 : 0

  return (
    <div className="w-full max-w-sm rounded-2xl bg-slate-800 p-5 shadow-lg">
      <div className="text-center">
        <div className="text-5xl">📷</div>
        <h2 className="mt-2 text-lg font-bold text-white">{title}</h2>
        <p className="mt-1 text-sm text-slate-300">{detail}</p>
      </div>

      {steps.length > 0 && (
        <ol className="mt-5 space-y-2.5">
          {steps.map((id, i) => {
            const step = STEPS[os][id]
            const on = i === highlight
            return (
              <li
                key={id}
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
                {art(id, os)}
              </li>
            )
          })}
        </ol>
      )}

      {note && (
        <p className="mt-4 rounded-xl border border-slate-700 bg-slate-900/40 p-3 text-sm leading-relaxed text-slate-300">
          {note}
        </p>
      )}

      <button
        onClick={onRetry}
        className="mt-5 w-full rounded-lg bg-white py-3 font-semibold text-slate-900 transition hover:bg-slate-200"
      >
        Yenidən skan et
      </button>

      <p className="mt-3 text-center text-xs text-slate-500">
        Düzəlmirsə, administratora bildirin — bu cəhd artıq qeydə alındı.
      </p>
    </div>
  )
}
