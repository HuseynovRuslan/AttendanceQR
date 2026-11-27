import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { onForegroundPush, type ForegroundPush } from '../lib/foregroundPush'
import { IconX } from './icons'
import { NotificationKindIcon } from './NotificationKindIcon'

/** How long the banner stays when nobody touches it. */
export const BANNER_MS = 8000

/**
 * Shows a push that arrived while the Android app is open (lib/foregroundPush) — what Android would have drawn in the
 * drawer had the app been closed. At the top, under the status bar, so it never sits over the scan button or the
 * bottom navigation; gone by itself in a few seconds, never takes focus, and a newer push replaces it. A tap opens
 * what the push points to, as a tap on the system notification would.
 */
export function ForegroundPushBanner() {
  const navigate = useNavigate()
  const [push, setPush] = useState<ForegroundPush | null>(null)
  const [held, setHeld] = useState(false)

  useEffect(() => onForegroundPush((p) => setPush(p)), [])

  useEffect(() => {
    if (!push || held) return
    const t = setTimeout(() => setPush(null), BANNER_MS)
    return () => clearTimeout(t)
  }, [push, held])

  const close = () => {
    setPush(null)
    setHeld(false)
  }
  const target = push?.url && push.url.startsWith('/') ? push.url : null

  const text = push && (
    <>
      {push.title && (
        <span style={{ display: 'block', fontWeight: 700, fontSize: 14, color: 'var(--c900)', lineHeight: 1.4 }}>{push.title}</span>
      )}
      {push.body && (
        <span style={{ display: 'block', fontSize: 13, color: 'var(--c600)', lineHeight: 1.45, marginTop: 2, whiteSpace: 'pre-line' }}>
          {push.body}
        </span>
      )}
    </>
  )

  return (
    <div
      aria-live="polite"
      role="status"
      style={{
        position: 'fixed', top: 'calc(env(safe-area-inset-top, 0px) + 12px)', left: '50%', transform: 'translateX(-50%)',
        zIndex: 1001, width: 420, maxWidth: 'calc(100vw - 32px)',
      }}
    >
      {push && (
        <div
          className="card"
          data-testid="foreground-push"
          onMouseEnter={() => setHeld(true)}
          onMouseLeave={() => setHeld(false)}
          onFocus={() => setHeld(true)}
          onBlur={() => setHeld(false)}
          style={{ display: 'flex', alignItems: 'flex-start', gap: 10, padding: '12px 8px 12px 14px', boxShadow: 'var(--sh)' }}
        >
          <NotificationKindIcon typeKey="push" size={32} />
          {target ? (
            <button
              type="button"
              onClick={() => {
                close()
                navigate(target)
              }}
              style={{
                flex: 1, minWidth: 0, border: 'none', background: 'transparent', padding: 0, textAlign: 'left',
                cursor: 'pointer', fontFamily: 'inherit',
              }}
            >
              {text}
            </button>
          ) : (
            <div style={{ flex: 1, minWidth: 0 }}>{text}</div>
          )}
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            onClick={close}
            aria-label="Bağla"
            style={{ minWidth: 40, minHeight: 40, padding: 8, marginTop: -6 }}
          >
            <IconX />
          </button>
        </div>
      )}
    </div>
  )
}
