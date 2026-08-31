// Shown after the QR is read and BEFORE the front camera opens.
// Explains the selfie requirement with real demonstration photos and clear visual guidance.

function PhotoExample({
  good,
  src,
  label,
}: {
  good: boolean
  src: string
  label: string
}) {
  return (
    <div className="flex flex-col items-center gap-2">
      <div
        className={`relative h-28 w-28 overflow-hidden rounded-2xl border transition-all ${
          good
            ? 'border-blue-500/50 shadow-[0_0_20px_rgba(37,99,235,0.25)]'
            : 'border-rose-500/40 shadow-[0_0_20px_rgba(244,63,94,0.15)]'
        }`}
      >
        {/* Real demonstration photo */}
        <img
          src={src}
          alt={label}
          className="h-full w-full object-cover"
          loading="eager"
        />

        {/* HUD Corner Reticles */}
        <div
          className={`pointer-events-none absolute top-1 left-1 h-3 w-3 border-t-2 border-l-2 ${
            good ? 'border-blue-400' : 'border-rose-400'
          }`}
        />
        <div
          className={`pointer-events-none absolute top-1 right-1 h-3 w-3 border-t-2 border-r-2 ${
            good ? 'border-blue-400' : 'border-rose-400'
          }`}
        />
        <div
          className={`pointer-events-none absolute bottom-1 left-1 h-3 w-3 border-b-2 border-l-2 ${
            good ? 'border-blue-400' : 'border-rose-400'
          }`}
        />
        <div
          className={`pointer-events-none absolute bottom-1 right-1 h-3 w-3 border-b-2 border-r-2 ${
            good ? 'border-blue-400' : 'border-rose-400'
          }`}
        />

        {/* Subtle Biometric Crosshair */}
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center opacity-25">
          <div className={`h-10 w-10 rounded-full border border-dashed ${good ? 'border-blue-300' : 'border-rose-300'}`} />
        </div>

        {/* The verdict, on the picture itself.
            Green tick and red cross rather than the brand blue: this one pairing is understood
            without being read, in any language and by anyone who has ever seen a form. The frame
            keeps QRLog's blue; the badge is the only place a second colour earns its keep. */}
        <span
          className={`absolute -top-1.5 -right-1.5 grid h-7 w-7 place-items-center rounded-full text-sm font-black text-white ring-2 ring-[#0B111E] ${
            good ? 'bg-emerald-500' : 'bg-rose-500'
          }`}
        >
          {good ? '✓' : '✕'}
        </span>
      </div>

      {/* Sentence case, not small uppercase. «SƏHV» at 12px is a harder read than «Yanlış», and
          Azerbaijani upper-casing is where ı/İ go wrong — this label has one job and no room to be
          clever. */}
      <span
        className={`rounded-full px-3.5 py-1 text-sm font-extrabold ${
          good ? 'bg-emerald-500/20 text-emerald-300' : 'bg-rose-500/20 text-rose-300'
        }`}
      >
        {label}
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
    <div className="relative w-full max-w-sm overflow-hidden rounded-3xl border border-white/10 bg-[#0B111E]/95 p-6 text-center shadow-[0_25px_60px_-15px_rgba(0,0,0,0.8),0_0_35px_rgba(37,99,235,0.12)] backdrop-blur-2xl">
      {/* QRLog Signature Ambient Blue Glow */}
      <div className="pointer-events-none absolute -top-24 left-1/2 -translate-x-1/2 h-44 w-44 rounded-full bg-blue-600/20 blur-3xl" />

      {/* Hero Biometric Emblem in QRLog Blue/White */}
      <div className="relative mx-auto flex h-16 w-16 items-center justify-center rounded-2xl border border-blue-500/40 bg-gradient-to-b from-blue-600/30 to-blue-900/20 shadow-[0_0_25px_rgba(37,99,235,0.35)]">
        {/* Animated pulse ring */}
        <span className="absolute -inset-1 rounded-2xl border border-blue-400/30 animate-pulse" />
        <svg
          className="h-8 w-8 text-blue-400"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={1.8}
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z" />
          <circle cx="12" cy="13" r="4" />
          <path d="M12 9v1M12 16v1M8 13H7M17 13h-1" opacity={0.6} />
        </svg>
      </div>

      {/* Crystal White Title & Subtitle */}
      <h2 className="mt-4 text-2xl font-extrabold tracking-tight text-white">İndi şəkil çəkiləcək</h2>
      <p className="mt-1.5 text-sm font-medium text-slate-300">Giriş qeydiyyatı üçün ön kamera açılacaq</p>

      {/* Warning if previous check-in lacked a clear face */}
      {lastUnverified && (
        <div className="mt-3.5 flex items-start gap-2.5 rounded-xl border border-amber-500/30 bg-amber-500/15 p-3 text-left text-xs font-medium text-amber-200 backdrop-blur-sm">
          <span className="text-base shrink-0">⚠️</span>
          <span>Son girişinizin şəklində üz görünmürdü. Zəhmət olmasa, bu dəfə üzünüz aydın görünsün.</span>
        </div>
      )}

      {/* Real Photo Demonstration (Düzgün vs Səhv) */}
      <div className="mt-5 flex items-center justify-center gap-6">
        <PhotoExample
          good
          src="/brand/selfie-good.jpg"
          label="Doğru"
        />
        <PhotoExample
          good={false}
          src="/brand/selfie-bad.jpg"
          label="Yanlış"
        />
      </div>

      {/* Instruction Guidelines in clean QRLog Blue glass card */}
      <div className="mt-5 space-y-2.5 rounded-2xl border border-white/[0.08] bg-white/[0.03] p-3.5 text-left">
        <div className="flex items-center gap-3">
          <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-blue-500/20 text-blue-400">
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <rect x="5" y="2" width="14" height="20" rx="2" strokeLinecap="round" strokeLinejoin="round" />
              <path d="M12 18h.01" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </div>
          <span className="text-xs font-medium text-slate-200">Telefonu göz bərabərində və yaxın tutun</span>
        </div>

        <div className="flex items-center gap-3">
          <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-blue-500/20 text-blue-400">
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <circle cx="12" cy="12" r="8" strokeLinecap="round" strokeLinejoin="round" strokeDasharray="3 3" />
              <path d="M9 10h.01M15 10h.01M9.5 15a3.5 3.5 0 0 0 5 0" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </div>
          <span className="text-xs font-medium text-slate-200">Üzünüz çərçivənin tam mərkəzində olsun</span>
        </div>

        <div className="flex items-center gap-3">
          <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-blue-500/20 text-blue-400">
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path d="M16 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" strokeLinecap="round" strokeLinejoin="round" />
              <circle cx="10" cy="7" r="4" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </div>
          <span className="text-xs font-medium text-slate-200">Kadrda yalnız siz olun</span>
        </div>
      </div>

      {/* QRLog Signature Sapphire Blue CTA Button */}
      <button
        onClick={onReady}
        className="group relative mt-5 flex w-full items-center justify-center gap-2.5 overflow-hidden rounded-2xl bg-gradient-to-r from-blue-600 via-blue-500 to-indigo-600 py-3.5 text-base font-extrabold text-white shadow-[0_4px_25px_rgba(37,99,235,0.4)] transition-all duration-200 hover:shadow-[0_6px_30px_rgba(37,99,235,0.6)] active:scale-[0.98] cursor-pointer"
      >
        <span className="absolute inset-0 bg-gradient-to-t from-black/15 to-transparent pointer-events-none" />
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

      {/* Countdown timer pill in Electric Blue */}
      <div className="mt-3 flex items-center justify-center gap-2 text-xs font-medium text-slate-400">
        <span className="relative flex h-2 w-2">
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-blue-400 opacity-75" />
          <span className="relative inline-flex h-2 w-2 rounded-full bg-blue-500" />
        </span>
        <span>
          <strong className="text-blue-400 font-semibold">{secondsLeft} san</strong> sonra avtomatik başlayacaq
        </span>
      </div>
    </div>
  )
}
