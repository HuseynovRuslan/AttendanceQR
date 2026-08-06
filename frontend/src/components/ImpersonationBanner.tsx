import { getImpersonation, exitImpersonation } from '../api/client'

// A fixed warning strip shown on EVERY screen while a super-admin is impersonating a tenant admin for
// support. It exists so the operator can never forget whose account they are inside — and can leave in
// one tap. Rendered once at the app root; renders nothing when not impersonating.
export function ImpersonationBanner() {
  const info = getImpersonation()
  if (!info) return null

  function exit() {
    exitImpersonation()
    // Full reload back to the operator console's companies list with the operator's own token restored.
    // Impersonation is only ever started from that console (admin.qrlog.az), so this is its home.
    window.location.href = '/tenants'
  }

  return (
    <div
      role="alert"
      style={{
        position: 'fixed', top: 0, left: 0, right: 0, zIndex: 99999,
        background: '#7c2d12', color: '#fff', padding: '6px 12px',
        display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12,
        fontSize: 13, fontWeight: 600, boxShadow: '0 1px 6px rgba(0,0,0,.25)',
      }}
    >
      <span>
        🎭 «{info.tenantName}» ({info.adminName}) adından baxırsınız — dəstək rejimi
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
