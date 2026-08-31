// Shown after the QR is read and BEFORE the front camera opens. Two reasons it exists: seeing your
// own face appear unannounced is unpleasant, and the photo is only useful if the person is holding
// the phone properly — which they cannot do if they learn about it at the same moment it is taken.
// The examples are drawn inline rather than shipped as images, so there is nothing extra to load.

/** Sleek biometric silhouette */
function Person({ cx, cy, scale = 1, opacity = 1 }: { cx: number; cy: number; scale?: number; opacity?: number }) {
  return (
    <g transform={`translate(${cx} ${cy}) scale(${scale})`} opacity={opacity}>
      <circle cx="0" cy="-14" r="11" fill="currentColor" />
      <path d="M-20 22c0-11 9-19 20-19s20 8 20 19z" fill="currentColor" />
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
              <Person cx={60} cy={64} scale={1.35} />
            ) : (
              // Off to one side, small, with a colleague in the queue behind.
              <>
                <Person cx={44} cy={72} scale={0.95} />
                <Person cx={90} cy={66} scale={0.7} opacity={0.45} />
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
