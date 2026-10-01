import { platform } from '../lib/geo'

/**
 * The phone is allowed to know where it is — roughly. Android 12+ and iOS 14+ both let somebody grant
 * location WITHOUT precision, and the browser then returns a deliberately blurred fix: on Android it
 * comes back as exactly ±2000 m, every time, from any spot on earth.
 *
 * That is the whole of it for Əhməyeva Tünzalə at Dədə Qorqud Parkı (25.09: eight refusals, all
 * «±2000 m») and for three women at Heydər Əliyev Mərkəzi. The app was telling them to go outside and
 * wait — advice that cannot work, because nothing about the sky is the problem. Their days were being
 * typed in by hand.
 *
 * So: when the margin is that wide, say what it actually is and where the switch lives.
 */
export function PreciseLocationHelp({ onRetry }: { onRetry?: () => void }) {
  const os = platform()
  const steps = os === 'ios'
    ? ['Parametrlər', 'Məxfilik və Təhlükəsizlik', 'Məkan Xidmətləri', 'Safari', '«Dəqiq Məkan» — aktiv edin']
    : ['Parametrlər', 'Tətbiqlər', 'Chrome', 'İcazələr → Məkan', '«Dəqiq məkandan istifadə» — aktiv edin']

  return (
    <div className="w-full max-w-sm rounded-3xl border border-amber-500/30 bg-gradient-to-b from-amber-950/60 to-slate-900/90 p-5 text-left shadow-2xl backdrop-blur-2xl">
      <div className="flex items-center gap-2">
        <span className="text-xl">🎯</span>
        <h3 className="text-base font-extrabold text-white">Telefonda «dəqiq məkan» söndürülüb</h3>
      </div>
      <p className="mt-2 text-xs font-medium leading-relaxed text-slate-300">
        Telefon yerinizi bilərəkdən təxmini verir, ona görə açıq havada gözləmək kömək etmir. Bir dəfə
        bu ayarı açmaq kifayətdir:
      </p>
      <ol className="mt-3 space-y-1.5">
        {steps.map((s, i) => (
          <li key={s} className="flex items-start gap-2 text-xs font-medium text-slate-200">
            <span className="mt-[1px] flex h-4 w-4 shrink-0 items-center justify-center rounded-full bg-amber-500/20 text-[10px] font-bold text-amber-300">
              {i + 1}
            </span>
            <span className={i === steps.length - 1 ? 'text-amber-200 font-bold' : undefined}>{s}</span>
          </li>
        ))}
      </ol>
      {os === 'android' && (
        <p className="mt-3 text-[11px] leading-relaxed text-slate-400">
          Samsung telefonlarda üstəlik: Parametrlər → Məkan → Məkan xidmətləri → «Google Məkan
          Dəqiqliyi» açıq olmalıdır.
        </p>
      )}
      {onRetry && (
        <button
          onClick={onRetry}
          className="mt-4 w-full rounded-2xl bg-white py-3 text-sm font-bold text-slate-900 transition active:scale-[0.98] cursor-pointer"
        >
          Açdım — yenidən yoxla
        </button>
      )}
    </div>
  )
}
