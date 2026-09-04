import { useEffect, useRef } from 'react'
import { getImpersonation, exitImpersonation, impersonationReturnPath } from '../api/client'

// A fixed warning strip shown on EVERY screen while a super-admin is impersonating a tenant admin for
// support. It exists so the operator can never forget whose account they are inside — and can leave in
// one tap. Rendered once at the app root; renders nothing when not impersonating.
export function ImpersonationBanner() {
  const info = getImpersonation()
  const ref = useRef<HTMLDivElement | null>(null)

  // The strip is position:fixed, so it takes NO space in the layout — and it sat straight on top of
  // the admin topbar, hiding the hamburger button with it. On a phone that is the only way to the
  // menu: the whole navigation of the panel was behind this warning. Its height is published as a
  // variable so the page can leave room for it (see .topbar in theme.css), measured rather than
  // guessed because the text wraps to two lines on a narrow screen.
  useEffect(() => {
    const el = ref.current
    const root = document.documentElement
    if (!el) { root.style.setProperty('--imp-h', '0px'); return }
    const apply = () => root.style.setProperty('--imp-h', `${el.offsetHeight}px`)
    apply()
    const ro = new ResizeObserver(apply)
    ro.observe(el)
    return () => { ro.disconnect(); root.style.setProperty('--imp-h', '0px') }
  }, [info?.tenantName, info?.readOnly])

  if (!info) return null

  function exit() {
    // Read the return path BEFORE exiting — exitImpersonation clears it.
    const back = impersonationReturnPath()
    exitImpersonation()
    // Full reload, back to the screen the session was started from, with the operator's own token
    // restored. This used to be hard-coded to the operator console's /tenants, which is not a route
    // on every host a session can now begin on.
    window.location.href = back
  }

  // Read-only is a calmer state and says so: a slate strip that names the COMPANY, not the seat.
  // The loud support colour is reserved for a session that can actually change that company's data.
  const view = info.readOnly === true

  return (
    <div
      ref={ref}
      role="alert"
      style={{
        position: 'fixed', top: 0, left: 0, right: 0, zIndex: 99999,
        background: view ? '#1e3a5f' : '#7c2d12', color: '#fff', padding: '6px 12px',
        display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12,
        fontSize: 13, fontWeight: 600, boxShadow: '0 1px 6px rgba(0,0,0,.25)',
      }}
    >
      <span>
        {view
          ? <>👁 «{info.tenantName}» — yalnız oxu rejimi, dəyişiklik edilə bilməz</>
          : <>🎭 «{info.tenantName}» ({info.adminName}) adından baxırsınız — dəstək rejimi</>}
      </span>
      <button
        onClick={exit}
        style={{
          background: '#fff', color: '#7c2d12', border: 'none', borderRadius: 6,
          padding: '3px 12px', fontWeight: 800, cursor: 'pointer', fontSize: 12,
        }}
      >
        Çıx
      </button>
    </div>
  )
}
