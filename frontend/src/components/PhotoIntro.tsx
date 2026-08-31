// Shown after the QR is read and BEFORE the front camera opens. Two reasons it exists: seeing your
// own face appear unannounced is unpleasant, and the photo is only useful if the person is holding
// the phone properly — which they cannot do if they learn about it at the same moment it is taken.
// The examples are drawn inline rather than shipped as images, so there is nothing extra to load.
//
// The look is deliberately closer to a biometric prompt than to a form: this screen is asking someone
// to put their face in front of a camera at work, and the two seconds it has to make that feel
// considered rather than furtive are the same two seconds it has to teach them how to hold the phone.

/** One silhouette. `currentColor` lets the parent frame tint it. */
function Person({ cx, cy, scale = 1, opacity = 1 }: { cx: number; cy: number; scale?: number; opacity?: number }) {
  return (
    <g transform={`translate(${cx} ${cy}) scale(${scale})`} opacity={opacity}>
      <circle cx="0" cy="-14" r="11" fill="currentColor" />
      <path d="M-20 22c0-11 9-19 20-19s20 8 20 19z" fill="currentColor" />
    </g>
  )
}

/** The four corner marks of a viewfinder — what says "this is a camera" without a caption. */
function Reticle({ colour }: { colour: string }) {
  const arm = 'M0 14V4a4 4 0 0 1 4-4h10'
  return (
    <g stroke={colour} strokeWidth="2.5" fill="none" strokeLinecap="round">
      <path d={arm} transform="translate(8 8)" />
      <path d={arm} transform="translate(112 8) scale(-1 1)" />
      <path d={arm} transform="translate(8 112) scale(1 -1)" />
      <path d={arm} transform="translate(112 112) scale(-1 -1)" />
    </g>
  )
}

/**
 * The two frames side by side.
 *
 * They carry the whole instruction: one shows a face filling the frame, the other shows what the
 * camera actually gets when somebody holds the phone at waist height with a colleague behind them in
 * the queue. That second picture is the reason this screen earns its place — it is the mistake, drawn.
 */
function Example({ good }: { good: boolean }) {
  const id = good ? 'pi-clip-ok' : 'pi-clip-bad'
  const colour = good ? '#34D399' : '#F87171'
  return (
    <div className="flex flex-col items-center gap-2">
      <svg viewBox="0 0 120 120" className={`h-[104px] w-[104px] ${good ? 'text-emerald-300/90' : 'text-rose-300/80'}`}>
        <defs>
          <clipPath id={id}>
            <rect x="14" y="14" width="92" height="92" rx="26" />
          </clipPath>
          <linearGradient id={`${id}-bg`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={colour} stopOpacity="0.14" />
            <stop offset="100%" stopColor={colour} stopOpacity="0.03" />
          </linearGradient>
        </defs>
        <rect x="14" y="14" width="92" height="92" rx="26" fill={`url(#${id}-bg)`} />
        <g clipPath={`url(#${id})`}>
          {good ? (
            // Centred, filling the frame — what a phone held at face height produces.
            <Person cx={60} cy={68} scale={1.5} />
          ) : (
            // Off to one side, small, with a colleague in the queue behind.
            <>
              <Person cx={44} cy={76} scale={1} />
              <Person cx={92} cy={68} scale={0.72} opacity={0.5} />
            </>
          )}
        </g>
        <rect x="14" y="14" width="92" height="92" rx="26" fill="none" stroke={colour} strokeOpacity="0.35" strokeWidth="1.5" />
        <Reticle colour={colour} />
      </svg>
      <span
        className={`rounded-full px-2.5 py-1 text-[11px] font-extrabold tracking-wide ${
          good
            ? 'bg-emerald-400/15 text-emerald-300 ring-1 ring-emerald-400/30'
            : 'bg-rose-400/15 text-rose-300 ring-1 ring-rose-400/30'
        }`}
      >
        {good ? '✓ Düzgün' : '✕ Səhv'}
      </span>
    </div>
  )
}

/** One line of advice, as a card rather than a bullet — three glanceable things, not a paragraph. */
function Tip({ icon, title, text }: { icon: string; title: string; text: string }) {
  return (
    <div className="flex items-center gap-3 rounded-2xl border border-white/[0.07] bg-white/[0.04] px-3 py-2.5">
      <span className="grid h-9 w-9 flex-none place-items-center rounded-xl bg-emerald-400/10 text-base ring-1 ring-emerald-400/20">
        {icon}
      </span>
      <span className="min-w-0">
        <span className="block text-[13px] font-bold text-white">{title}</span>
        <span className="block text-[12px] leading-snug text-slate-400">{text}</span>
      </span>
    </div>
  )
}

/** The seconds left, as a ring that drains — a number alone reads as a deadline, a ring as progress. */
function Countdown({ secondsLeft, total }: { secondsLeft: number; total: number }) {
  const r = 11
  const c = 2 * Math.PI * r
  const left = Math.max(0, Math.min(1, secondsLeft / total))
  return (
    <span className="mt-3 inline-flex items-center gap-2 rounded-full border border-white/[0.07] bg-white/[0.03] px-3 py-1.5 text-[12px] font-semibold text-slate-400">
      <svg viewBox="0 0 28 28" className="h-4 w-4 -rotate-90">
        <circle cx="14" cy="14" r={r} fill="none" stroke="currentColor" strokeOpacity="0.2" strokeWidth="3" />
        <circle
          cx="14" cy="14" r={r} fill="none" stroke="#34D399" strokeWidth="3" strokeLinecap="round"
          strokeDasharray={c} strokeDashoffset={c * (1 - left)}
        />
      </svg>
      <b className="tabular-nums text-slate-200">{secondsLeft} san</b> sonra avtomatik başlayır
    </span>
  )
}

export function PhotoIntro({
  secondsLeft,
  totalSeconds,
  onReady,
  lastUnverified = false,
}: {
  secondsLeft: number
  /** How long the wait is in full — the ring needs it to know what "full" looks like. Passed rather
   *  than assumed, so changing INTRO_MS cannot leave the ring quietly measuring the wrong thing. */
  totalSeconds: number
  onReady: () => void
  /** Their previous check-in photo showed no face. */
  lastUnverified?: boolean
}) {
  return (
    <div className="relative w-full max-w-sm overflow-hidden rounded-3xl border border-white/10 bg-white/[0.045] p-5 text-center shadow-[0_24px_60px_-20px_rgba(0,0,0,0.8)] backdrop-blur-2xl">
      {/* A single light source above the card, so the glass has somewhere to catch. */}
      <div
        aria-hidden
        className="pointer-events-none absolute -top-24 left-1/2 h-48 w-72 -translate-x-1/2 rounded-full bg-emerald-400/15 blur-3xl"
      />

      <div className="relative">
        {/* The lens: rings that breathe outward, the one moment of motion on the screen. It replaces a
            📷 emoji, which rendered as a different picture on every phone and as none on some. */}
        <div className="relative mx-auto grid h-20 w-20 place-items-center">
          <span className="absolute inset-0 rounded-full bg-emerald-400/10 motion-safe:animate-ping" />
          <span className="absolute inset-2 rounded-full bg-emerald-400/10" />
          <svg viewBox="0 0 48 48" className="relative h-12 w-12 text-emerald-300">
            <circle cx="24" cy="24" r="21" fill="none" stroke="currentColor" strokeOpacity="0.35" strokeWidth="1.5" />
            <circle cx="24" cy="24" r="13" fill="none" stroke="currentColor" strokeWidth="2" />
            <circle cx="24" cy="24" r="6" fill="currentColor" fillOpacity="0.35" />
            <circle cx="19.5" cy="19.5" r="2" fill="#ECFDF5" />
            {/* Four gaps in the outer ring turn a circle into an instrument. */}
            <g stroke="currentColor" strokeWidth="2.5" strokeLinecap="round">
              <path d="M24 1.5v6M24 40.5v6M1.5 24h6M40.5 24h6" />
            </g>
          </svg>
        </div>

        <h2 className="mt-4 text-2xl font-extrabold tracking-tight text-white">İndi şəkil çəkiləcək</h2>
        <p className="mt-1.5 text-[13px] leading-relaxed text-slate-400">
          Giriş qeydiyyatı üçün ön kamera açılacaq.
        </p>

        {/* The one moment a warning can still change what they do — after the fact it is an argument
            about a day nobody remembers. Shown on every phone, including those whose browser cannot
            check the photo itself: the server flagged the last one, so this always reaches them. */}
        {lastUnverified && (
          <p className="mt-4 rounded-2xl border border-amber-400/25 bg-amber-400/10 px-3 py-2.5 text-[13px] font-semibold leading-snug text-amber-200">
            ⚠️ Son girişinizin şəklində üz görünmürdü. Bu dəfə üzünüz aydın görünsün.
          </p>
        )}

        <div className="mt-5 flex items-start justify-center gap-5">
          <Example good />
          <Example good={false} />
        </div>

        <div className="mt-5 flex flex-col gap-2 text-left">
          <Tip icon="🎯" title="Məsafə" text="Telefonu göz bərabərində tutun" />
          <Tip icon="🔲" title="Çərçivə" text="Üzünüz çərçivəni doldursun" />
          <Tip icon="👤" title="Tək" text="Kadrda yalnız siz olun" />
        </div>

        <button
          onClick={onReady}
          className="mt-5 w-full rounded-2xl bg-gradient-to-b from-emerald-400 to-teal-500 py-3.5 text-base font-extrabold text-slate-950 shadow-[0_10px_30px_-8px_rgba(16,185,129,0.7)] transition active:scale-[0.98]"
        >
          <span className="inline-flex items-center justify-center gap-2">
            <svg viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M4 8h3l1.5-2h7L17 8h3a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1Z" />
              <circle cx="12" cy="13.5" r="3.5" />
            </svg>
            Hazıram
          </span>
        </button>

        {/* Auto-advance so a hesitant employee never blocks the queue by not tapping anything. */}
        <Countdown secondsLeft={secondsLeft} total={totalSeconds} />
      </div>
    </div>
  )
}
