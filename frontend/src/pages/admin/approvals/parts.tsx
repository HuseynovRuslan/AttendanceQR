import { useCallback, useEffect, useRef, useState } from 'react'
import { NavLink } from 'react-router-dom'
import type { ApiResponse } from '../../../api/client'
import type { RequestStatus } from '../../../api/approvals'
import { IconAlert, IconRefresh, IconX } from '../../../components/icons'
import { useSheet, when } from '../../approvals/shared'
import { useApprovals } from './ApprovalsContext'
import { NETWORK, ROLE_NAME, STATUS_LABEL, errorText, screensFor, type ApprovalRole, type RouteStop } from './model'

/** A server answer, or the network's failure as status 0 — so a screen handles both in one place. */
export async function call<T>(p: Promise<ApiResponse<T>>): Promise<ApiResponse<T | undefined>> {
  try {
    return await p
  } catch {
    return { status: NETWORK, data: undefined }
  }
}

/** Loads one thing, keeps the last good answer while reloading, and never lets a slow old answer overwrite a new one. */
export function useLoad<T>(load: () => Promise<ApiResponse<unknown>>, ok: (data: unknown) => data is T, deps: unknown[]) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const seq = useRef(0)
  const run = useCallback(async () => {
    const mine = ++seq.current
    setLoading(true)
    const r = await load()
    if (mine !== seq.current) return
    if (r.status === 200 && ok(r.data)) {
      setData(r.data)
      setError(null)
    } else {
      setError(errorText(r.status, r.data))
    }
    setLoading(false)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
  useEffect(() => { void run() }, [run])
  return { data, error, loading, reload: run }
}

export function StatusMark({ status }: { status: RequestStatus }) {
  return <span className={`apx-st apx-st-${status.toLowerCase()}`}>{STATUS_LABEL[status]}</span>
}

/** Three dots: asked, decided, applied. The label says it in words, for a screen reader and a tooltip. */
export function Pips({ status }: { status: RequestStatus }) {
  const second = status === 'Pending' ? 'now' : status === 'Rejected' ? 'no' : status === 'Cancelled' ? '' : 'on'
  const third = status === 'Approved' ? 'on' : ''
  const label = status === 'Pending' ? 'Operatorun qərarını gözləyir'
    : status === 'Approved' ? 'Təsdiqlənib, işçi hesabı yaradılıb'
    : status === 'Rejected' ? 'Operator rədd edib' : 'Müraciət ləğv edilib'
  return (
    <span className="apx-pips" role="img" aria-label={label} title={label}>
      <i className="on" /><i className={second} /><i className={third} />
    </span>
  )
}

export function RouteLine({ stops }: { stops: RouteStop[] }) {
  return (
    <ol className="apx-route" aria-label="Müraciətin yolu">
      {stops.map((s) => (
        <li key={s.title} className={`apx-stop apx-stop-${s.state}`}>
          <span className="apx-stop-dot" aria-hidden />
          <div className="apx-stop-t">{s.title}</div>
          <div className="apx-stop-who">{s.who}</div>
          <div className="apx-stop-when">{s.when ? when(s.when) : s.state === 'now' ? 'gözləyir' : ''}</div>
        </li>
      ))}
    </ol>
  )
}

export function ErrorState({ what, message, onRetry }: { what: string; message: string; onRetry: () => void }) {
  return (
    <div className="apx-state" role="alert">
      <IconAlert width={22} height={22} style={{ color: 'var(--clay)' }} />
      <h3>{what} yüklənmədi</h3>
      <p>{message}</p>
      <button type="button" className="apx-btn apx-btn-sm" onClick={onRetry}>
        <IconRefresh /> Yenidən yüklə
      </button>
    </div>
  )
}

export function RoleChips({ roles }: { roles: ApprovalRole[] }) {
  return (
    <>
      {roles.map((r) => <span key={r} className={`apx-role apx-role-${r}`}>{ROLE_NAME[r]}</span>)}
    </>
  )
}

/** The section's own tabs: the screens this person has, the queue's count on the operator's. */
export function SectionTabs() {
  const { view } = useApprovals()
  const screens = screensFor(view)
  if (screens.length === 0) return null
  const tab = { operator: 'Operator paneli', center: 'Təsdiq mərkəzi', branch: 'Mənim filialım' } as const
  const to = { operator: '/admin/approvals/operator', center: '/admin/approvals', branch: '/admin/approvals/branch' } as const
  return (
    <nav className="apx-tabs" aria-label="Təsdiq bölməsi">
      {screens.map((s) => (
        <NavLink key={s} to={to[s]} end>
          {tab[s]}
          {s === 'operator' && view.queueCount > 0 && <span className="apx-tabs-n" aria-label={`${view.queueCount} gözləyir`}>{view.queueCount}</span>}
        </NavLink>
      ))}
      <span className="apx-tabs-who"><RoleChips roles={view.roles} /></span>
    </nav>
  )
}

// ---------------------------------------------------------------------------------------------- toasts

export interface Toast { id: number; text: string; bad?: boolean }

export function useToasts() {
  const [toasts, setToasts] = useState<Toast[]>([])
  const next = useRef(0)
  const push = useCallback((text: string, bad = false) => {
    const id = ++next.current
    setToasts((t) => [...t, { id, text, bad }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), bad ? 9000 : 6000)
  }, [])
  const close = useCallback((id: number) => setToasts((t) => t.filter((x) => x.id !== id)), [])
  return { toasts, push, close }
}

export function Toasts({ toasts, onClose }: { toasts: Toast[]; onClose: (id: number) => void }) {
  return (
    <div className="apx-toasts" role="status" aria-live="polite">
      {toasts.map((t) => (
        <div key={t.id} className={`apx-toast${t.bad ? ' bad' : ''}`}>
          <span>{t.text}</span>
          <button type="button" onClick={() => onClose(t.id)} aria-label="Bağla"><IconX width={14} height={14} /></button>
        </div>
      ))}
    </div>
  )
}

// ---------------------------------------------------------------------------------------------- the one-time PIN

/**
 * The new person's temporary PIN, shown once, right after the approval. It lives only in the state of the screen
 * that approved: nothing writes it to storage, and closing this dialog forgets it. The server keeps only its hash.
 */
export function PinReveal({ pin, name, onClose }: { pin: string; name: string; onClose: () => void }) {
  const ref = useSheet(onClose)
  const [copied, setCopied] = useState(false)
  async function copy() {
    try {
      await navigator.clipboard.writeText(pin)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }
  return (
    <div className="apx-modal-back">
      <div className="apx-modal" role="dialog" aria-modal="true" aria-labelledby="apx-pin-t" ref={ref} tabIndex={-1}>
        <h2 id="apx-pin-t">{name} əlavə olundu</h2>
        <p>Müvəqqəti PIN yalnız indi göstərilir. İşçi ilk girişdə öz PIN-ini təyin edəcək.</p>
        <div className="apx-pin" aria-label={`Müvəqqəti PIN: ${pin.split('').join(' ')}`}>{pin}</div>
        <p className="apx-quiet">
          PIN-i işçiyə və ya tabelçiyə şifahi çatdırın. Bağladıqdan sonra yenidən görmək mümkün deyil: lazım olsa
          «PIN sıfırla» ilə yeni PIN verilir.
        </p>
        <div className="apx-modal-f">
          <button type="button" className="apx-btn" onClick={() => void copy()}>{copied ? 'Kopyalandı' : 'Kopyala'}</button>
          <button type="button" className="apx-btn apx-btn-go" onClick={onClose}>Bağla</button>
        </div>
      </div>
    </div>
  )
}
