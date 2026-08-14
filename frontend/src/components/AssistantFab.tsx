import { useNavigate } from 'react-router-dom'
import { useFeatureEnabled } from '../branding/BrandingContext'

/**
 * The floating "ask the assistant" button on the employee home — the shape people already know from
 * shopping apps, redrawn in QRLog's own language (blue, soft shadows) rather than copied.
 *
 * Home only, on purpose: this is the screen someone stares at when a number looks wrong, which is
 * exactly the moment the chat is for. On every other screen it would just sit on top of content —
 * and the scan flow especially must never gain a floating anything.
 *
 * Sits above the h-16 bottom tab bar (bottom-20), under the nav's z-30 so no sheet or dialog ever
 * has to fight it.
 */
export function AssistantFab() {
  const navigate = useNavigate()
  const enabled = useFeatureEnabled('assistant')
  if (!enabled) return null

  return (
    <button
      onClick={() => navigate('/help')}
      aria-label="Süni intellekt köməkçisi ilə söhbət"
      className="fixed bottom-20 right-4 z-20 flex items-center gap-2.5 rounded-full bg-blue-600 py-2.5 pl-4 pr-2.5 text-white shadow-lg shadow-blue-600/30 transition active:scale-95"
    >
      <span className="text-left leading-tight">
        <span className="block text-[11px] font-medium text-blue-100">Sualınız var?</span>
        {/* "Süni intellekt", not "AI": the words the workforce actually knows. A size down from
            text-sm so the longer phrase still fits one line on a narrow phone. */}
        <span className="block text-[13px] font-bold">Süni intellekt köməkçisi</span>
      </span>
      <span className="flex h-9 w-9 items-center justify-center rounded-full bg-white/20">
        {/* Sparkles — drawn inline; the icon set has no "AI" glyph and one emoji would render
            differently on every phone. */}
        <svg viewBox="0 0 24 24" fill="currentColor" className="h-5 w-5" aria-hidden>
          <path d="M12 3l1.7 4.6L18 9.5l-4.3 1.9L12 16l-1.7-4.6L6 9.5l4.3-1.9L12 3z" />
          <path d="M18.5 14l.9 2.3 2.1.9-2.1.9-.9 2.3-.9-2.3-2.1-.9 2.1-.9.9-2.3z" opacity=".85" />
        </svg>
      </span>
    </button>
  )
}
