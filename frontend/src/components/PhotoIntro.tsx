// Shown after the QR is read and BEFORE the front camera opens. Two reasons it exists: seeing your
// own face appear unannounced is unpleasant, and the photo is only useful if the person is holding
// the phone properly — which they cannot do if they learn about it at the same moment it is taken.
// The examples are drawn inline rather than shipped as images, so there is nothing extra to load.

/** One silhouette. `currentColor` lets the parent circle tint it. */
function Person({ cx, cy, scale = 1, opacity = 1 }: { cx: number; cy: number; scale?: number; opacity?: number }) {
  return (
    <g transform={`translate(${cx} ${cy}) scale(${scale})`} opacity={opacity}>
      <circle cx="0" cy="-14" r="11" fill="currentColor" />
      <path d="M-20 22c0-11 9-19 20-19s20 8 20 19z" fill="currentColor" />
    </g>
  )
}

function Example({ good }: { good: boolean }) {
  const id = good ? 'clip-ok' : 'clip-bad'
  return (
    <div className="flex flex-col items-center gap-1.5">
      <svg viewBox="0 0 120 120" className={`h-24 w-24 ${good ? 'text-green-300' : 'text-red-300'}`}>
        <defs>
          <clipPath id={id}>
            <circle cx="60" cy="60" r="52" />
          </clipPath>
        </defs>
        <circle
          cx="60"
          cy="60"
          r="52"
          fill="rgba(255,255,255,0.05)"
          stroke={good ? '#22c55e' : '#ef4444'}
          strokeWidth="3"
        />
        <g clipPath={`url(#${id})`}>
          {good ? (
            // Centred, filling the frame — what a phone held at face height produces.
            <Person cx={60} cy={64} scale={1.4} />
          ) : (
            // Off to one side, small, with a colleague in the queue behind.
            <>
              <Person cx={44} cy={72} scale={1} />
              <Person cx={90} cy={66} scale={0.72} opacity={0.55} />
            </>
          )}
        </g>
      </svg>
      <span className={`text-xs font-bold ${good ? 'text-green-400' : 'text-red-400'}`}>
        {good ? '✓ Düzgün' : '✕ Səhv'}
      </span>
    </div>
  )
}

export function PhotoIntro({ secondsLeft, onReady }: { secondsLeft: number; onReady: () => void }) {
  return (
    <div className="w-full max-w-sm rounded-2xl bg-slate-800 p-5 text-center shadow-lg">
      <div className="text-4xl">📷</div>
      <h2 className="mt-2 text-xl font-bold text-white">İndi şəkil çəkiləcək</h2>
      <p className="mt-1 text-sm text-slate-300">Giriş qeydiyyatı üçün ön kamera açılacaq.</p>

      <div className="mt-4 flex items-start justify-center gap-6">
        <Example good />
        <Example good={false} />
      </div>

      <ul className="mt-4 space-y-2 text-left text-sm text-slate-300">
        <li>📱 Telefonu üzünüzə yaxın tutun</li>
        <li>😊 Üzünüz çərçivəni doldursun</li>
        <li>👤 Kadrda tək siz olun</li>
      </ul>

      <button
        onClick={onReady}
        className="mt-5 w-full rounded-lg bg-white py-3 font-semibold text-slate-900 transition hover:bg-slate-200"
      >
        Hazıram
      </button>
      {/* Auto-advance so a hesitant employee never blocks the queue by not tapping anything. */}
      <p className="mt-2 text-xs text-slate-500">{secondsLeft} saniyə sonra avtomatik başlayacaq</p>
    </div>
  )
}
