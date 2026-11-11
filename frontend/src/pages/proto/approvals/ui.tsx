import type { ReactNode } from 'react'
import {
  IconAlert, IconBriefcase, IconCalendar, IconCalendarCheck, IconClock, IconPencil, IconPower, IconRefresh,
  IconThermometer, IconTrash, IconUser,
} from '../../../components/icons'
import {
  ROLE_LABEL, STATUS_LABEL, TYPE_LABEL, type ChangeRequest, type Persona, type RequestStatus, type RequestType, type RoleKind,
  type World,
} from './model'
import { day, when } from '../../approvals/shared'

// The generic pieces live in pages/approvals/shared, which the real screens use as well.
export { day, EmptyState, initials, Skeleton, useSheet, waited, when } from '../../approvals/shared'

export function personName(w: World, id: string | undefined): string {
  return (id && w.personas.find((p) => p.id === id)?.name) || '—'
}

export function employeeLabel(w: World, r: ChangeRequest): string {
  if (r.type === 'employee_create') return r.changes.find((c) => c.field === 'name')?.after ?? 'Yeni işçi'
  return w.employees.find((e) => e.id === r.employeeId)?.name ?? 'Silinmiş işçi'
}

export function periodLabel(r: ChangeRequest): string | null {
  if (!r.dateFrom) return null
  const span = r.dateTo && r.dateTo !== r.dateFrom ? `${day(r.dateFrom)} – ${day(r.dateTo)}` : day(r.dateFrom)
  return r.timeFrom && r.timeTo ? `${span}, ${r.timeFrom}–${r.timeTo}` : span
}

export function StatusMark({ status }: { status: RequestStatus }) {
  return <span className={`apx-st apx-st-${status}`}>{STATUS_LABEL[status]}</span>
}

export function RoleMark({ kind, children }: { kind: RoleKind; children?: ReactNode }) {
  return <span className={`apx-role apx-role-${kind}`}>{children ?? ROLE_LABEL[kind]}</span>
}

const TYPE_ICON: Record<RequestType, (p: { 'aria-hidden'?: boolean }) => ReactNode> = {
  employee_create: (p) => <IconUser {...p} />,
  employee_update: (p) => <IconPencil {...p} />,
  employee_activation: (p) => <IconPower {...p} />,
  employee_delete: (p) => <IconTrash {...p} />,
  permission: (p) => <IconClock {...p} />,
  vacation: (p) => <IconCalendar {...p} />,
  sick: (p) => <IconThermometer {...p} />,
  attendance_fix: (p) => <IconCalendarCheck {...p} />,
  shift_change: (p) => <IconBriefcase {...p} />,
}

export function TypeIcon({ type }: { type: RequestType }) {
  return <>{TYPE_ICON[type]({ 'aria-hidden': true })}</>
}

export function typeLabel(t: RequestType) {
  return TYPE_LABEL[t]
}

/** The three stops every request passes — compact, for a list row. */
export function Pips({ r }: { r: ChangeRequest }) {
  const second = r.status === 'pending' ? 'now' : r.status === 'rejected' ? 'no' : r.status === 'cancelled' ? '' : 'on'
  const third = r.status === 'approved' ? 'on' : ''
  const label = r.status === 'pending' ? 'Operatorun qərarını gözləyir'
    : r.status === 'approved' ? 'Təsdiqlənib və tətbiq olunub'
    : r.status === 'rejected' ? 'Operator rədd edib' : 'Müraciət ləğv edilib'
  return (
    <span className="apx-pips" role="img" aria-label={label} title={label}>
      <i className="on" /><i className={second} /><i className={third} />
    </span>
  )
}

/** The route in full: who asked, who decides, when it took effect. */
export function Route({ w, r, approverId }: { w: World; r: ChangeRequest; approverId?: string }) {
  const created = r.events.find((e) => e.kind === 'created')
  const decided = r.events.find((e) => e.kind === 'approved' || e.kind === 'rejected' || e.kind === 'direct')
  const applied = r.events.find((e) => e.kind === 'applied')
  const cancelled = r.events.find((e) => e.kind === 'cancelled')
  const decideState = r.status === 'pending' ? 'now' : r.status === 'rejected' ? 'stopped' : r.status === 'cancelled' ? 'off' : 'done'
  return (
    <ol className="apx-route" aria-label="Müraciətin yolu">
      <li className="apx-stop apx-stop-done">
        <span className="apx-stop-dot" aria-hidden />
        <div className="apx-stop-t">{r.direct ? 'Başladan' : 'Müraciət edən'}</div>
        <div className="apx-stop-who">{personName(w, created?.actorId)}</div>
        <div className="apx-stop-when">{created ? when(created.at) : ''}</div>
      </li>
      <li className={`apx-stop apx-stop-${decideState}`}>
        <span className="apx-stop-dot" aria-hidden />
        <div className="apx-stop-t">{r.direct ? 'Birbaşa, öz strukturunda' : 'Qərar'}</div>
        <div className="apx-stop-who">
          {r.status === 'cancelled' ? 'Ləğv edildi' : r.status === 'pending' ? personName(w, approverId) : personName(w, decided?.actorId)}
        </div>
        <div className="apx-stop-when">
          {r.status === 'pending' ? 'gözləyir' : r.status === 'cancelled' ? when(cancelled!.at)
            : decided ? `${r.status === 'rejected' ? 'Rədd' : 'Təsdiq'}, ${when(decided.at)}` : ''}
        </div>
      </li>
      <li className={`apx-stop apx-stop-${applied ? 'done' : 'off'}`}>
        <span className="apx-stop-dot" aria-hidden />
        <div className="apx-stop-t">Tətbiq</div>
        <div className="apx-stop-who">{applied ? 'Tətbiq olundu' : r.status === 'pending' ? 'Qərardan sonra' : 'Tətbiq olunmadı'}</div>
        <div className="apx-stop-when">{applied ? when(applied.at) : ''}</div>
      </li>
    </ol>
  )
}

export function ErrorState({ onRetry, what }: { onRetry: () => void; what: string }) {
  return (
    <div className="apx-state" role="alert">
      <IconAlert width={22} height={22} style={{ color: 'var(--clay)' }} />
      <h3>{what} yüklənmədi</h3>
      <p>Serverlə əlaqə alınmadı. Bu prototipdə xəta «Xəta rejimi» açıq olduğu üçün göstərilir.</p>
      <button type="button" className="apx-btn apx-btn-sm" onClick={onRetry}>
        <IconRefresh /> Yenidən yüklə
      </button>
    </div>
  )
}

/** A persona's role and the area it covers, as two plain lines. */
export function personaRole(w: World, p: Persona): { role: string; area: string } {
  const s = p.structureId ? w.structures.find((x) => x.id === p.structureId) : undefined
  const b = p.branchId ? w.branches.find((x) => x.id === p.branchId) : undefined
  switch (p.kind) {
    case 'companyAdmin':
      return { role: ROLE_LABEL.companyAdmin, area: 'Bütün şirkət' }
    case 'operator':
      return { role: ROLE_LABEL.operator, area: `${s?.name ?? ''} strukturu` }
    case 'viewer':
      return { role: ROLE_LABEL.viewer, area: s ? `${s.name} strukturu` : `${b?.name ?? ''} filialı` }
    case 'timekeeper':
      return { role: ROLE_LABEL.timekeeper, area: `${b?.name ?? ''} filialı` }
  }
}
