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
 * three things it could be instead of asserting the most likely.
 *
 * And show it. Every state opens with a picture of the FAULT, and each remedy is drawn as the phone
 * screen it lives on — because "ünvan sətrinin solundakı kilid işarəsi" is a sentence about a thing
 * nobody has ever consciously looked at, and a drawing of that screen is recognised in a second.
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
//
// Drawn, not photographed. A screenshot is one phone, one Android version and one language — and
// three of them is a megabyte on a connection that is often the problem in the first place. These
// are built from the parts people actually navigate by: the omnibox pill, the permission sheet, the
// blue toggle, the recents cards.

/** Phone-screen palette. Light, because the screens being imitated are light — a dark illustration
 *  on a dark card reads as an icon, and this needs to read as a phone. */
const UI = {
  bezel: '#04070f',
  screen: '#f6f7f9',
  card: '#ffffff',
  line: '#dfe3e8',
  fill: '#eef1f4',
  text: '#111418',
  dim: '#5f6368',
  blue: '#1a73e8',
  blueSoft: '#e8f0fe',
  green: '#188038',
  red: '#d93025',
  hiBg: '#fff4d6',
  hiLine: '#e8a317',
}

/** The card behind the illustrations — used to punch a hole in a glyph so a badge reads as on top. */
const CARD_BG = '#1e293b'

const FONT = 'system-ui, -apple-system, Segoe UI, Roboto, sans-serif'

/** A phone, with the status bar people subconsciously use to tell a screenshot from a diagram. */
function Phone({ h = 156, children }: { h?: number; children: React.ReactNode }) {
  return (
    <svg viewBox={`0 0 240 ${h}`} className="mt-3 w-full" role="img" aria-hidden="true" fontFamily={FONT}>
      <rect x="6" y="0" width="228" height={h} rx="19" fill={UI.bezel} />
      <rect x="11" y="5" width="218" height={h - 10} rx="14" fill={UI.screen} />
      <text x="25" y="17" fontSize="8" fontWeight="600" fill={UI.dim}>08:14</text>
      <g fill={UI.dim}>
        <rect x="186" y="13" width="3" height="4" rx="1" />
        <rect x="191" y="11" width="3" height="6" rx="1" />
        <rect x="196" y="9" width="3" height="8" rx="1" />
        <rect x="204" y="9" width="13" height="8" rx="2.5" />
        <rect x="218" y="11" width="2" height="4" rx="1" />
      </g>
      {children}
    </svg>
  )
}

/** "Press here." */
function Tap({ x, y, r = 11 }: { x: number; y: number; r?: number }) {
  return (
    <>
      <circle cx={x} cy={y} r={r + 4} fill="none" stroke={UI.hiLine} strokeWidth="1.4" opacity="0.45" />
      <circle cx={x} cy={y} r={r} fill="none" stroke={UI.hiLine} strokeWidth="2" />
    </>
  )
}

/** A small camera, for list rows. */
function MiniCam({ x, y, color }: { x: number; y: number; color: string }) {
  return (
    <g fill="none" stroke={color} strokeWidth="1.5" strokeLinejoin="round">
      <rect x={x} y={y + 2.5} width="14" height="10.5" rx="2.5" />
      <path d={`M${x + 4} ${y + 2.5}l1.5-2.5h3l1.5 2.5`} />
      <circle cx={x + 7} cy={y + 7.8} r="3" />
    </g>
  )
}

function Toggle({ x, y, on }: { x: number; y: number; on: boolean }) {
  return (
    <>
      <rect x={x} y={y} width="24" height="13" rx="6.5" fill={on ? UI.blue : UI.line} />
      <circle cx={on ? x + 17.5 : x + 6.5} cy={y + 6.5} r="4.6" fill="#fff" />
    </>
  )
}

/** Step 1 — the per-site permission: the omnibox lock, and the sheet it opens. */
function ArtSitePermission({ os }: { os: Platform }) {
  return (
    <Phone>
      {/* browser toolbar */}
      <rect x="11" y="22" width="218" height="27" fill={UI.card} />
      <line x1="11" y1="49" x2="229" y2="49" stroke={UI.line} strokeWidth="1" />
      <rect x="22" y="27" width="146" height="17" rx="8.5" fill={UI.fill} />
      {os === 'ios' ? (
        <text x="33" y="39" fontSize="9" fontWeight="700" fill={UI.hiLine} textAnchor="middle">AA</text>
      ) : (
        <g>
          <rect x="29" y="34" width="8.5" height="6.5" rx="1.4" fill={UI.hiLine} />
          <path d="M31.2 34v-1.7a2.05 2.05 0 0 1 4.1 0V34" fill="none" stroke={UI.hiLine} strokeWidth="1.5" />
        </g>
      )}
      <text x="45" y="39" fontSize="8.5" fill={UI.text}>app.qrlog.az</text>
      <rect x="180" y="29" width="13" height="13" rx="3" fill="none" stroke={UI.dim} strokeWidth="1.3" />
      <text x="186.5" y="39.5" fontSize="7.5" fill={UI.dim} textAnchor="middle">3</text>
      <g fill={UI.dim}>
        <circle cx="208" cy="30.5" r="1.5" />
        <circle cx="208" cy="35.5" r="1.5" />
        <circle cx="208" cy="40.5" r="1.5" />
      </g>
      <Tap x={33} y={37} />

      {/* the sheet the lock opens */}
      <rect x="26" y="60" width="166" height="84" rx="11" fill={UI.card} stroke={UI.line} strokeWidth="1" />
      <text x="38" y="77" fontSize="8.5" fontWeight="700" fill={UI.text}>app.qrlog.az</text>
      <text x="38" y="88" fontSize="7" fill={UI.dim}>İcazələr</text>

      <rect x="32" y="95" width="154" height="24" rx="7" fill={UI.hiBg} stroke={UI.hiLine} strokeWidth="1.3" />
      <MiniCam x={41} y={100} color={UI.text} />
      <text x="62" y="110.5" fontSize="8.5" fontWeight="700" fill={UI.text}>Kamera</text>
      <Toggle x={152} y={100.5} on />

      <line x1="32" y1="122" x2="186" y2="122" stroke={UI.line} strokeWidth="1" />
      <g fill="none" stroke={UI.dim} strokeWidth="1.5">
        <path d="M48 138c0 0-6-5.5-6-9.5a6 6 0 0 1 12 0c0 4-6 9.5-6 9.5Z" />
      </g>
      <text x="62" y="135" fontSize="8.5" fill={UI.dim}>Məkan</text>
      <Toggle x={152} y={125} on />
    </Phone>
  )
}

/** Step 2 — the OS app permission: Settings → Apps → browser → Permissions. */
function ArtAppPermission({ os }: { os: Platform }) {
  const rows: { label: string; note: string; ok: boolean }[] = [
    { label: 'Kamera', note: 'İcazə verilib', ok: true },
    { label: 'Mikrofon', note: 'İcazə verilməyib', ok: false },
    { label: 'Məkan', note: 'İcazə verilib', ok: true },
  ]
  return (
    <Phone>
      <rect x="11" y="22" width="218" height="29" fill={UI.card} />
      <line x1="11" y1="51" x2="229" y2="51" stroke={UI.line} strokeWidth="1" />
      <path d="M31 31l-6 6 6 6" fill="none" stroke={UI.text} strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" />
      <text x="43" y="41" fontSize="10.5" fontWeight="700" fill={UI.text}>{os === 'ios' ? 'Safari' : 'Chrome'}</text>
      <text x="24" y="66" fontSize="7" fontWeight="700" fill={UI.blue} letterSpacing="0.9">İCAZƏLƏR</text>

      {rows.map((r, i) => {
        const y = 72 + i * 26
        const on = i === 0
        return (
          <g key={r.label}>
            {on && <rect x="18" y={y} width="204" height="24" rx="7" fill={UI.hiBg} stroke={UI.hiLine} strokeWidth="1.3" />}
            <circle cx="35" cy={y + 12} r="9.5" fill={on ? '#fff' : UI.blueSoft} />
            {i === 0 && <MiniCam x={28} y={y + 5} color={UI.blue} />}
            {i === 1 && (
              <g fill="none" stroke={UI.blue} strokeWidth="1.5" strokeLinecap="round">
                <rect x="32" y={y + 6} width="6" height="9.5" rx="3" />
                <path d={`M29.5 ${y + 13}a5.5 5.5 0 0 0 11 0M35 ${y + 18.5}v2`} />
              </g>
            )}
            {i === 2 && (
              <path
                d={`M35 ${y + 18}c0 0-6-5.5-6-9.5a6 6 0 0 1 12 0c0 4-6 9.5-6 9.5Z`}
                fill="none" stroke={UI.blue} strokeWidth="1.5"
              />
            )}
            <text x="52" y={y + 11} fontSize="8.5" fontWeight={on ? 700 : 400} fill={UI.text}>{r.label}</text>
            <text x="52" y={y + 20} fontSize="7" fill={r.ok ? UI.green : UI.dim}>{r.note}</text>
            {r.ok && (
              <path
                d={`M196 ${y + 12.5}l3 3.5 6-7`}
                fill="none" stroke={UI.green} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"
              />
            )}
          </g>
        )
      })}
    </Phone>
  )
}

/** Step 3 — leave the window inside another app. Drawn as the menu that actually does it, rather
 *  than as a comparison of two browsers: the employee needs the tap, not the concept. */
function ArtOpenInBrowser({ os }: { os: Platform }) {
  const target = os === 'ios' ? 'Safari-də aç' : 'Chrome-da aç'
  return (
    <Phone h={150}>
      {/* an in-app browser: the host app's own bar, not a browser's */}
      <rect x="11" y="22" width="218" height="28" fill="#1f2328" />
      <path d="M23 32l9 9M32 32l-9 9" stroke="#fff" strokeWidth="1.9" strokeLinecap="round" />
      <text x="44" y="40" fontSize="8.5" fontWeight="600" fill="#fff">app.qrlog.az</text>
      <g fill="#fff">
        <circle cx="212" cy="31" r="1.6" />
        <circle cx="212" cy="36" r="1.6" />
        <circle cx="212" cy="41" r="1.6" />
      </g>
      <Tap x={212} y={36} r={9} />

      {/* the menu it opens */}
      <rect x="108" y="58" width="116" height="62" rx="9" fill={UI.card} stroke={UI.line} strokeWidth="1" />
      <text x="120" y="76" fontSize="8.5" fill={UI.dim}>Linki kopyala</text>
      <rect x="112" y="84" width="108" height="28" rx="7" fill={UI.hiBg} stroke={UI.hiLine} strokeWidth="1.3" />
      <path
        d="M124 96h9m-3-3l3 3-3 3M122 90h-4v12h12v-4"
        fill="none" stroke={UI.text} strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"
      />
      <text x="142" y="101" fontSize="8.5" fontWeight="700" fill={UI.text}>{target}</text>

      {/* page underneath */}
      <rect x="26" y="62" width="66" height="6" rx="3" fill={UI.line} />
      <rect x="26" y="76" width="48" height="6" rx="3" fill={UI.line} />
      <rect x="26" y="126" width="120" height="6" rx="3" fill={UI.line} />
    </Phone>
  )
}

/** Free the camera: the recents screen, with the app that is holding it. */
function ArtRecents() {
  return (
    <Phone h={150}>
      {/* the app holding the camera */}
      <rect x="24" y="40" width="88" height="80" rx="11" fill={UI.card} stroke={UI.line} strokeWidth="1" />
      <text x="36" y="55" fontSize="8" fontWeight="700" fill={UI.text}>Kamera</text>
      <circle cx="100" cy="51" r="3.6" fill={UI.red} />
      <rect x="32" y="62" width="72" height="50" rx="7" fill="#20242a" />
      <circle cx="68" cy="87" r="13" fill="none" stroke="#6b7280" strokeWidth="2.4" />
      <circle cx="68" cy="87" r="6" fill="#6b7280" opacity="0.5" />

      {/* swipe it away */}
      <path d="M68 34V16" stroke={UI.hiLine} strokeWidth="2" strokeLinecap="round" strokeDasharray="4 4" />
      <path d="M63 21l5-5 5 5" fill="none" stroke={UI.hiLine} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />

      <rect x="126" y="40" width="88" height="80" rx="11" fill={UI.card} stroke={UI.line} strokeWidth="1" />
      <text x="138" y="55" fontSize="8" fontWeight="700" fill={UI.dim}>QRLog</text>
      <rect x="134" y="64" width="60" height="6" rx="3" fill={UI.line} />
      <rect x="134" y="76" width="44" height="6" rx="3" fill={UI.line} />
      <rect x="134" y="94" width="72" height="18" rx="9" fill={UI.fill} />

      <rect x="86" y="126" width="70" height="17" rx="8.5" fill={UI.card} stroke={UI.line} strokeWidth="1" />
      <text x="121" y="137.5" fontSize="8" fontWeight="600" fill={UI.dim} textAnchor="middle">Hamısını bağla</text>
    </Phone>
  )
}

// ── The fault itself ────────────────────────────────────────────────────────────────────────────
// Each state opens with a picture of what went wrong, so the screen is recognisable before it is
// read — including the three states that have no remedy to walk through and used to show nothing
// but an emoji.

function Hero({ children }: { children: React.ReactNode }) {
  return (
    <svg viewBox="0 0 100 72" className="mx-auto h-20 w-auto" role="img" aria-hidden="true" fontFamily={FONT}>
      {children}
    </svg>
  )
}

function CameraBody({ color, dashed = false }: { color: string; dashed?: boolean }) {
  return (
    <g
      fill="none" stroke={color} strokeWidth="3.4" strokeLinejoin="round" strokeLinecap="round"
      strokeDasharray={dashed ? '5 4.5' : undefined}
    >
      <path d="M20 30h60a4 4 0 0 1 4 4v26a4 4 0 0 1-4 4H20a4 4 0 0 1-4-4V34a4 4 0 0 1 4-4Z" />
      <path d="M36 30l5-8h18l5 8" />
      <circle cx="50" cy="47" r="12" />
    </g>
  )
}

/** A badge sitting on the corner of the camera — the ring is card-coloured so it reads as on top. */
function Badge({ cx, cy, fill, children }: { cx: number; cy: number; fill: string; children: React.ReactNode }) {
  return (
    <>
      <circle cx={cx} cy={cy} r="15" fill={CARD_BG} />
      <circle cx={cx} cy={cy} r="11.5" fill={fill} />
      {children}
    </>
  )
}

const HERO: Record<CameraFailKind, React.ReactNode> = {
  // Locked, not broken — the distinction the whole screen turns on.
  denied: (
    <Hero>
      <CameraBody color={UI.red} />
      <Badge cx={78} cy={57} fill={UI.red}>
        <rect x="73" y="57" width="10" height="7.5" rx="1.6" fill="#fff" />
        <path d="M75.4 57v-2a2.6 2.6 0 0 1 5.2 0v2" fill="none" stroke="#fff" strokeWidth="1.8" />
      </Badge>
    </Hero>
  ),
  // Working, and somebody else has it.
  inuse: (
    <Hero>
      <CameraBody color={UI.hiLine} />
      <Badge cx={79} cy={25} fill={UI.red}>
        <circle cx="79" cy="25" r="4.6" fill="#fff" />
      </Badge>
    </Hero>
  ),
  // Absent. The dashed outline says "there is nothing here" without a second symbol.
  notfound: (
    <Hero>
      <CameraBody color={UI.red} dashed />
    </Hero>
  ),
  // Not a camera fault at all — an open padlock, which is what the browser is objecting to.
  insecure: (
    <Hero>
      <g fill="none" stroke={UI.red} strokeWidth="3.6" strokeLinecap="round" strokeLinejoin="round">
        <rect x="28" y="38" width="44" height="30" rx="5" />
        <path d="M37 38V29a13 13 0 0 1 26 0" />
      </g>
      <circle cx="50" cy="50" r="4" fill={UI.red} />
      <rect x="48.2" y="52" width="3.6" height="8" rx="1.8" fill={UI.red} />
    </Hero>
  ),
  // The connection, drawn as the thing everyone already reads as connection.
  loadfailed: (
    <Hero>
      <rect x="20" y="50" width="11" height="16" rx="2.5" fill={UI.hiLine} />
      <rect x="36" y="40" width="11" height="26" rx="2.5" fill="#475569" />
      <rect x="52" y="28" width="11" height="38" rx="2.5" fill="#475569" />
      <Badge cx={77} cy={30} fill={UI.hiLine}>
        <rect x="75.2" y="23" width="3.6" height="9" rx="1.8" fill={CARD_BG} />
        <circle cx="77" cy="35.5" r="2.1" fill={CARD_BG} />
      </Badge>
    </Hero>
  ),
  // Says what it is: we do not know.
  unknown: (
    <Hero>
      <CameraBody color={UI.red} />
      <text x="50" y="54" fontSize="19" fontWeight="800" fill={UI.red} textAnchor="middle">?</text>
    </Hero>
  ),
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
      body: 'WhatsApp və ya Instagram-ın içindəki pəncərədə kamera işləmir. Menyudan «Chrome-da aç» seçin.',
    },
    restart: {
      title: 'Kameranı boşaldın',
      body: 'Açıq tətbiqlərə baxıb Kameranı və video zəngi bağlayın, sonra telefonu söndürüb yandırın.',
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
      body: 'Başqa tətbiqin içindəki pəncərədə kamera açılmır. Menyudan «Safari-də aç» seçin.',
    },
    restart: {
      title: 'Kameranı boşaldın',
      body: 'Açıq tətbiqlərə baxıb Kameranı və video zəngi bağlayın, sonra telefonu söndürüb yandırın.',
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
  if (id === 'app') return <ArtAppPermission os={os} />
  if (id === 'browser') return <ArtOpenInBrowser os={os} />
  return <ArtRecents />
}

/** A crew phone can record someone whose own phone will not cooperate — worth saying on the screens
 *  where the phone may never be fixed today, and nowhere else. */
const CREW_NOTE =
  'Bu gün qeyd olunmaq üçün: yanınızdakı işçinin telefonunda Profil → adın yanındakı ⌄ → «Hesab əlavə et» ilə öz hesabınıza keçib skan edin.'

type Headline = {
  title: string
  detail: string
  /** Which remedies to show, in order. Empty means the picture and the text are the whole answer. */
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
        detail: 'Kameranı hazırda başqa tətbiq tutub — icazə ilə bağlı deyil.',
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
        {HERO[kind]}
        <h2 className="mt-3 text-lg font-bold text-white">{title}</h2>
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
