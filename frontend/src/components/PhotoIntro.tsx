// Shown after the QR is read and BEFORE the front camera opens. Two reasons it exists: seeing your
// own face appear unannounced is unpleasant, and the photo is only useful if the person is holding
// the phone properly — which they cannot do if they learn about it at the same moment it is taken.
// The examples are drawn inline rather than shipped as images, so there is nothing extra to load.

/**
 * A person, drawn rather than suggested.
 *
 * This was a circle on a dome — a pictogram, which is fine on a sign and useless here, because the
 * whole job of these two frames is to show somebody what their own face should look like inside the
 * oval. A shape that could equally be a user icon teaches nothing. So: a head with hair and ears, a
 * neck, shoulders, and enough of a face to read as one at 80 pixels.
 *
 * Still inline SVG. A photograph would need a real person's consent, a licence, and a download on a
 * phone that may be on one bar of signal at a gate at seven in the morning — and it would look like
 * one specific stranger rather than like whoever is holding the phone.
 *
 * `turn` swings the head and shifts the features, which is what makes the "wrong" frame read as
 * somebody looking away rather than as a smaller copy of the same picture.
 */
function Person({
  cx, cy, scale = 1, opacity = 1, turn = 0,
}: { cx: number; cy: number; scale?: number; opacity?: number; turn?: number }) {
  return (
    <g transform={`translate(${cx} ${cy}) scale(${scale})`} opacity={opacity} fill="currentColor">
      {/* Shoulders and chest — cut off by the frame, as a real chest-up shot is. */}
      <path d="M-26 40c0-13 11-23 26-23s26 10 26 23v6h-52z" />
      {/* Neck. Starts ABOVE the chin (y=3) and is drawn before the head, so the jaw paints over it —
          starting it below the chin left a gap and the head floated. */}
      <path d="M-6 -2h12v22a6 6 0 0 1-12 0z" opacity="0.85" />
      <g transform={`rotate(${turn})`}>
        {/* Ears, drawn before the head so the head's edge overlaps them. */}
        <ellipse cx="-12.5" cy="-4" rx="2.6" ry="4" opacity="0.9" />
        <ellipse cx="12.5" cy="-4" rx="2.6" ry="4" opacity="0.9" />
        {/* The head: taller than wide, with a jaw — not a circle. */}
        <path d="M0-22c7.2 0 12 5.2 12 12.5 0 4-.6 7.6-2 10.6C8-.4 4.6 3 0 3S-8-.4-10 1.1c-1.4-3-2-6.6-2-10.6C-12-16.8-7.2-22 0-22Z" />
        {/* Hair, sitting on the crown. */}
        <path d="M-12.4-9.5c-.6-8 4.6-13.5 12.4-13.5s13 5.5 12.4 13.5c-1.2-4.4-3-6.6-5.4-7.4-2.6-.9-5.6.5-9 .5-2.8 0-5.4-.6-7.2 1.2-1.2 1.2-2.2 3.4-3.2 5.7Z" />
        {/* Enough face to be a face. Cut out of the head so they read at any tint. */}
        <g fill="#0B1120" opacity="0.55">
          <ellipse cx="-4.6" cy="-8" rx="1.5" ry="1.9" />
          <ellipse cx="4.6" cy="-8" rx="1.5" ry="1.9" />
          <path d="M-4 -3.2c1.2 1.1 6.8 1.1 8 0" fill="none" stroke="#0B1120" strokeWidth="1.4" strokeLinecap="round" />
        </g>
      </g>
    </g>
  )
}

function Example({ good }: { good: boolean }) {
  const id = good ? 'clip-ok-hud' : 'clip-bad-hud'
  return (
    <div className="flex flex-col items-center gap-2">
      <div
        className={`relative flex h-24 w-24 items-center justify-center rounded-2xl border transition-all ${
          good
            ? 'border-emerald-500/40 bg-gradient-to-b from-emerald-500/10 to-emerald-500/[0.02] shadow-[0_0_15px_rgba(16,185,129,0.15)]'
            : 'border-rose-500/35 bg-gradient-to-b from-rose-500/10 to-rose-500/[0.02] shadow-[0_0_15px_rgba(244,63,94,0.1)]'
        }`}
      >
        {/* HUD Corner Reticles */}
        <div
          className={`pointer-events-none absolute -top-1 -left-1 h-2.5 w-2.5 border-t-2 border-l-2 ${
            good ? 'border-emerald-400' : 'border-rose-400'
          }`}
        />
        <div
          className={`pointer-events-none absolute -top-1 -right-1 h-2.5 w-2.5 border-t-2 border-r-2 ${
            good ? 'border-emerald-400' : 'border-rose-400'
          }`}
        />
        <div
          className={`pointer-events-none absolute -bottom-1 -left-1 h-2.5 w-2.5 border-b-2 border-l-2 ${
            good ? 'border-emerald-400' : 'border-rose-400'
          }`}
        />
        <div
          className={`pointer-events-none absolute -bottom-1 -right-1 h-2.5 w-2.5 border-b-2 border-r-2 ${
            good ? 'border-emerald-400' : 'border-rose-400'
          }`}
        />

        <svg viewBox="0 0 120 120" className={`h-20 w-20 ${good ? 'text-emerald-300' : 'text-rose-300/80'}`}>
          <defs>
            <clipPath id={id}>
              <circle cx="60" cy="60" r="48" />
            </clipPath>
          </defs>

          {/* Biometric subtle crosshair / target ring */}
          <circle
            cx="60"
            cy="60"
            r="44"
            fill="none"
            stroke="currentColor"
            strokeWidth="1"
            strokeDasharray={good ? '4 4' : '2 4'}
            opacity={0.3}
          />

          <g clipPath={`url(#${id})`}>
            {good ? (
              // Centred, filling the frame — what a phone held at face height produces.
              <Person cx={60} cy={62} scale={1.45} />
            ) : (
              // Off to one side, small, with a colleague in the queue behind.
              <>
                {/* Off to one side, turned away, and a colleague waiting in the queue behind. */}
                <Person cx={46} cy={74} scale={0.9} turn={-14} />
                <Person cx={88} cy={64} scale={0.62} opacity={0.4} turn={9} />
              </>
            )}
          </g>
        </svg>
      </div>

      <span
        className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[11px] font-bold tracking-wide uppercase ${
          good
            ? 'border border-emerald-500/30 bg-emerald-500/15 text-emerald-400'
            : 'border border-rose-500/30 bg-rose-500/15 text-rose-400'
        }`}
      >
        {good ? '✓ Düzgün' : '✕ Səhv'}
      </span>
    </div>
  )
}

export function PhotoIntro({
  secondsLeft,
  onReady,
  lastUnverified = false,
}: {
  secondsLeft: number
  onReady: () => void
  /** Their previous check-in photo showed no face. */
  lastUnverified?: boolean
}) {
  return (
    <div className="relative w-full max-w-sm overflow-hidden rounded-3xl border border-white/10 bg-slate-900/90 p-6 text-center shadow-[0_25px_50px_-12px_rgba(0,0,0,0.7),0_0_35px_rgba(16,185,129,0.06)] backdrop-blur-2xl">
      {/* Subtle ambient light aura */}
      <div className="pointer-events-none absolute -top-20 left-1/2 -translate-x-1/2 h-36 w-36 rounded-full bg-emerald-500/15 blur-3xl" />

      {/* Hero Biometric Emblem */}
      <div className="relative mx-auto flex h-16 w-16 items-center justify-center rounded-2xl border border-emerald-500/30 bg-gradient-to-b from-emerald-500/20 to-teal-500/5 shadow-[0_0_25px_rgba(16,185,129,0.2)]">
        {/* Animated pulse ring */}
        <span className="absolute -inset-1 rounded-2xl border border-emerald-400/20 animate-pulse" />
        <svg
          className="h-8 w-8 text-emerald-400"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={1.75}
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z" />
          <circle cx="12" cy="13" r="4" />
          <path d="M12 9v1M12 16v1M8 13H7M17 13h-1" opacity={0.6} />
        </svg>
      </div>

      {/* Title & Subtitle with crystal white high contrast */}
      <h2 className="mt-4 text-2xl font-extrabold tracking-tight text-white">İndi şəkil çəkiləcək</h2>
      <p className="mt-1.5 text-sm font-medium text-slate-300">Giriş qeydiyyatı üçün ön kamera açılacaq</p>

      {/* Warning if previous check-in lacked a clear face */}
      {lastUnverified && (
        <div className="mt-3.5 flex items-start gap-2.5 rounded-xl border border-amber-500/30 bg-amber-500/10 p-3 text-left text-xs font-medium text-amber-200 backdrop-blur-sm">
          <span className="text-base shrink-0">⚠️</span>
          <span>Son girişinizin şəklində üz görünmürdü. Zəhmət olmasa, bu dəfə üzünüz aydın görünsün.</span>
        </div>
      )}

      {/* Biometric HUD Comparison */}
      <div className="mt-5 flex items-center justify-center gap-6">
        <Example good />
        <Example good={false} />
      </div>

      {/* Instruction Guidelines in clean unified glass micro-card */}
      <div className="mt-5 space-y-2.5 rounded-2xl border border-white/[0.07] bg-white/[0.03] p-3.5 text-left">
        <div className="flex items-center gap-3">
          <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-emerald-500/15 text-emerald-400">
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <rect x="5" y="2" width="14" height="20" rx="2" strokeLinecap="round" strokeLinejoin="round" />
              <path d="M12 18h.01" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </div>
          <span className="text-xs font-medium text-slate-200">Telefonu üzünüzə yaxın və düz tutun</span>
        </div>

        <div className="flex items-center gap-3">
          <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-emerald-500/15 text-emerald-400">
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <circle cx="12" cy="12" r="8" strokeLinecap="round" strokeLinejoin="round" strokeDasharray="3 3" />
              <path d="M9 10h.01M15 10h.01M9.5 15a3.5 3.5 0 0 0 5 0" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </div>
          <span className="text-xs font-medium text-slate-200">Üzünüz çərçivənin tam mərkəzində olsun</span>
        </div>

        <div className="flex items-center gap-3">
          <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-emerald-500/15 text-emerald-400">
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M16 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" strokeLinecap="round" strokeLinejoin="round" />
              <circle cx="10" cy="7" r="4" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </div>
          <span className="text-xs font-medium text-slate-200">Kadrda yalnız siz olun</span>
        </div>
      </div>

      {/* Ultra-Premium CTA Button */}
      <button
        onClick={onReady}
        className="group relative mt-5 flex w-full items-center justify-center gap-2.5 overflow-hidden rounded-2xl bg-gradient-to-r from-emerald-500 via-teal-500 to-emerald-600 py-3.5 text-base font-extrabold text-white shadow-[0_4px_25px_rgba(16,185,129,0.35)] transition-all duration-200 hover:shadow-[0_6px_30px_rgba(16,185,129,0.5)] active:scale-[0.98] cursor-pointer"
      >
        <span className="absolute inset-0 bg-gradient-to-t from-black/10 to-transparent pointer-events-none" />
        <svg
          className="h-5 w-5 transition-transform duration-200 group-hover:scale-110"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={2.2}
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z" />
          <circle cx="12" cy="13" r="4" />
        </svg>
        <span>Hazıram</span>
      </button>

      {/* Sleek countdown timer pill */}
      <div className="mt-3 flex items-center justify-center gap-2 text-xs font-medium text-slate-400">
        <span className="relative flex h-2 w-2">
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
          <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
        </span>
        <span>
          <strong className="text-emerald-400 font-semibold">{secondsLeft} san</strong> sonra avtomatik başlayacaq
        </span>
      </div>
    </div>
  )
}
