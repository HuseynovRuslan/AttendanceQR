import type { ApiError, ApprovalBranch, ApprovalsMe, HistoryLine, RequestDetail, RequestStatus } from '../../../api/approvals'
import type { MyAccess } from '../../../api/me'
import { formatPhone, phoneError } from '../../../lib/phone'

/**
 * The rules the approval screens read, with no React in them, so each is pinned by a test.
 *
 * Who somebody is here comes from the server, never from the address: /api/me/access (the Company administrator) and
 * /api/approvals/me (what they may do at each branch inside an approval area). A role nobody gave them on the server
 * cannot be reached by editing the URL, and the server refuses whatever the screen might get wrong.
 */

export type ApprovalRole = 'companyAdmin' | 'operator' | 'timekeeper' | 'viewer'

/** Shown in English, as the owner asked: these are the names of the roles in the product. */
export const ROLE_NAME: Record<ApprovalRole, string> = {
  companyAdmin: 'Company Admin',
  operator: 'Operator',
  timekeeper: 'Timekeeper',
  viewer: 'Viewer',
}

export const STATUS_LABEL: Record<RequestStatus, string> = {
  Pending: 'Gözləyir',
  Approved: 'Təsdiqləndi',
  Rejected: 'Rədd edildi',
  Cancelled: 'Ləğv edildi',
}

/** The one kind the server applies today. The other eight are not offered anywhere. */
export const TYPE_LABEL = { EmployeeCreate: 'Yeni işçi' } as const

export const LONG_WAIT_HOURS = 24

export interface ApprovalsView {
  /** The deployment's switch is on. Off, the section does not exist for anybody. */
  enabled: boolean
  roles: ApprovalRole[]
  branches: ApprovalBranch[]
  /** Branches where this person sends requests (a Timekeeper's, or today's manager's in an area). */
  requestBranches: ApprovalBranch[]
  /** Branches whose requests wait for this person, their own structure's or one they stand in for. */
  approverBranches: ApprovalBranch[]
  queueCount: number
  /** Something in an approval area concerns this person — the section shows in the menu. */
  hasArea: boolean
}

export const NO_APPROVALS: ApprovalsView = {
  enabled: false, roles: [], branches: [], requestBranches: [], approverBranches: [], queueCount: 0, hasArea: false,
}

/**
 * Turns the two server answers into what the screens show. An Operator decides somewhere; a Timekeeper asks at a
 * branch where they do not decide; a Viewer only reads; the Company administrator is named as such. One person can
 * be more than one — an operator who is also somebody's timekeeper sees both screens.
 */
export function viewOf(access: MyAccess | null | undefined, me: ApprovalsMe | null | undefined): ApprovalsView {
  if (!me?.enabled) return NO_APPROVALS
  const branches = me.branches ?? []
  const approverBranches = branches.filter((b) => b.isApprover)
  const requestBranches = branches.filter((b) => b.canRequest && !b.isApprover && !b.canActDirectly)
  const companyAdmin = !!access?.companyAdmin
  const roles: ApprovalRole[] = []
  if (companyAdmin) roles.push('companyAdmin')
  if (approverBranches.length > 0) roles.push('operator')
  if (requestBranches.length > 0) roles.push('timekeeper')
  if (!companyAdmin && branches.some((b) => b.canView && !b.canRequest && !b.isApprover && !b.canActDirectly)) roles.push('viewer')
  return {
    enabled: true,
    roles,
    branches,
    requestBranches,
    approverBranches,
    queueCount: me.queueCount ?? 0,
    hasArea: branches.length > 0,
  }
}

/** The screens of the section this person opens, in the order the tabs show them. */
export function screensFor(view: ApprovalsView): ('operator' | 'center' | 'branch')[] {
  if (!view.enabled || !view.hasArea) return []
  const out: ('operator' | 'center' | 'branch')[] = []
  if (view.roles.includes('operator')) out.push('operator')
  out.push('center')
  if (view.roles.includes('timekeeper')) out.push('branch')
  return out
}

// ---------------------------------------------------------------------------------------------- errors

/** Network failure: the fetch itself threw (no signal, server unreachable, timed out). */
export const NETWORK = 0

const APPLY_REASON: Record<string, string> = {
  PhoneAlreadyExists: 'bu telefon nömrəsi artıq başqa işçidədir',
  EmailAlreadyExists: 'bu e-poçt artıq başqa işçidədir',
  ScheduleNotFound: 'seçilmiş növbə artıq yoxdur',
  ScheduleBelongsToOtherBranch: 'seçilmiş növbə artıq bu filiala aid deyil',
  NeedEmailOrPhone: 'telefon nömrəsi də, e-poçt da yoxdur',
}

/** Why an approval could not be applied, in words — for the history line «tətbiq olunmadı». */
export function applyReason(code: string | undefined): string {
  return (code && APPLY_REASON[code]) || 'işçi hesabı yazıla bilmədi'
}

const CODE: Record<string, string> = {
  ApprovalsDisabled: 'Təsdiq sistemi bu şirkətdə aktiv deyil.',
  RequestNotFound: 'Müraciət tapılmadı və ya ona baxmaq icazəniz yoxdur.',
  LocationNotFound: 'Filial tapılmadı.',
  NotInApprovalArea: 'Bu filial təsdiq sahəsində deyil: dəyişiklik köhnə qaydada, birbaşa edilir.',
  RequestNotAllowed: 'Bu filial üçün müraciət göndərmək icazəniz yoxdur.',
  NotApprover: 'Bu müraciətə qərar vermək icazəniz yoxdur.',
  SelfDecisionNotAllowed: 'Öz müraciətinizə qərar verə bilməzsiniz.',
  OnlyRequesterCanCancel: 'Müraciəti yalnız onu göndərən ləğv edə bilər.',
  IdempotencyKeyReused: 'Bu müraciət artıq göndərilib. Siyahını yeniləyin.',
  NameRequired: 'Ad və soyad yazılmalıdır.',
  NameTooLong: 'Ad, soyad və ata adı ən çox 100 hərf ola bilər.',
  NeedEmailOrPhone: 'Telefon nömrəsi və ya e-poçt yazın: işçi bununla daxil olacaq.',
  PhoneAlreadyExists: 'Bu telefon nömrəsi ilə artıq işçi var.',
  EmailAlreadyExists: 'Bu e-poçt ilə artıq işçi var.',
  PositionNotInCatalogue: 'Bu vəzifə kataloqda yoxdur. Siyahıdan seçin.',
  PositionTooLong: 'Vəzifə adı çox uzundur.',
  EmailTooLong: 'E-poçt çox uzundur.',
  ScheduleNotFound: 'Növbə tapılmadı.',
  ScheduleBelongsToOtherBranch: 'Seçilmiş növbə başqa filiala aiddir.',
  NoteTooLong: 'Qeyd ən çox 500 hərf ola bilər.',
  LocationRequired: 'Filialı seçin.',
  RejectReasonRequired: 'Rədd səbəbini yazın (ən azı 3 hərf).',
  PinChangeRequired: 'Davam etmək üçün əvvəl öz PIN-inizi təyin edin.',
  ViewOnlySession: 'Bu sessiya yalnız baxış üçündür: dəyişiklik etmək olmaz.',
  NotDuringImpersonation: 'Dəstək sessiyasında müraciət göndərmək və qərar vermək olmaz.',
  TryAgain: 'Server bir anlıq məşğul idi. Yenidən cəhd edin.',
  UnknownStatus: 'Naməlum status.',
  UnknownView: 'Naməlum siyahı.',
}

/** What to tell a person when the server said no — in their language, and what to do next. */
export function errorText(status: number, body?: unknown): string {
  if (status === NETWORK) return 'Serverə qoşulmaq alınmadı. İnternet bağlantısını yoxlayıb yenidən cəhd edin.'
  if (status === 401) return 'Sessiyanın vaxtı bitib. Yenidən daxil olun.'
  const e = body && typeof body === 'object' && 'error' in body ? (body as ApiError) : null
  if (e?.error === 'AlreadyDecided')
    return `Bu müraciətə artıq qərar verilib${e.status ? ` (${STATUS_LABEL[e.status] ?? e.status})` : ''}. Məlumat yeniləndi.`
  if (e?.error === 'DuplicatePendingRequest')
    return `Bu şəxs üçün artıq gözləyən müraciət var${e.number ? ` (R-${e.number})` : ''}.`
  if (e?.error === 'ApplyRefused') {
    const why = applyReason(e.reason)
    return `Təsdiq tətbiq olunmadı: ${why}. Müraciət gözləyir qalır, səbəb yazaraq rədd edə bilərsiniz.`
  }
  if (e?.error === 'ApprovalRequired')
    return e.canRequest
      ? 'Bu filialda yeni işçi təsdiqlə əlavə olunur. «Yeni işçi müraciəti» göndərin.'
      : 'Bu filialda yeni işçi təsdiqlə əlavə olunur, sizin isə müraciət göndərmək icazəniz yoxdur.'
  if (e && CODE[e.error]) return CODE[e.error]
  if (status === 403) return 'Bu əməliyyat üçün icazəniz yoxdur.'
  if (status === 404) return 'Tapılmadı.'
  if (status === 409) return 'Vəziyyət dəyişib. Siyahını yeniləyin.'
  if (status >= 500) return 'Serverdə xəta baş verdi. Bir az sonra yenidən cəhd edin.'
  return 'Əməliyyat alınmadı.'
}

// ---------------------------------------------------------------------------------------------- the form

export interface NewEmployeeDraft {
  firstName: string
  lastName: string
  fatherName: string
  /** Digits only, the local form 0XX XXX XX XX (lib/phone). */
  phone: string
  email: string
  position: string
  birthDate: string
  scheduleId: string
  note: string
}

export const EMPTY_DRAFT: NewEmployeeDraft = {
  firstName: '', lastName: '', fatherName: '', phone: '', email: '', position: '', birthDate: '', scheduleId: '', note: '',
}

export type DraftErrors = Partial<Record<'name' | 'father' | 'contact' | 'phone' | 'email' | 'note', string>>

/** The server's own rules, checked before anything is sent so the person sees the problem at the field. */
export function validateDraft(d: NewEmployeeDraft): DraftErrors {
  const errors: DraftErrors = {}
  if (!d.firstName.trim() || !d.lastName.trim()) errors.name = 'Ad və soyad yazılmalıdır.'
  else if (d.firstName.trim().length > 100 || d.lastName.trim().length > 100) errors.name = 'Ad və soyad ən çox 100 hərf ola bilər.'
  if (d.fatherName.trim().length > 100) errors.father = 'Ata adı ən çox 100 hərf ola bilər.'
  const phone = d.phone.trim()
  const email = d.email.trim()
  if (!phone && !email) errors.contact = 'Telefon nömrəsi və ya e-poçt yazın: işçi bununla daxil olacaq.'
  if (phone) {
    const bad = phoneError(phone, 'local')
    if (bad) errors.phone = bad
  }
  if (email && (email.length > 256 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email))) errors.email = 'E-poçtu düzgün yazın.'
  if (d.note.trim().length > 500) errors.note = 'Qeyd ən çox 500 hərf ola bilər.'
  return errors
}

/** A fresh key for one press of «Göndər». Kept for retries of that press; replaced once it has been answered. */
export function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  return `k-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`
}

// ---------------------------------------------------------------------------------------------- the route

export type StopState = 'done' | 'now' | 'stopped' | 'off'

export interface RouteStop {
  title: string
  who: string
  when: string | null
  state: StopState
}

/** The request's way, from its history: who asked, who decided (or who it waits for), and whether it took effect. */
export function routeOf(d: Pick<RequestDetail, 'request' | 'history'>): RouteStop[] {
  const r = d.request
  const line = (k: HistoryLine['kind']) => d.history.find((h) => h.kind === k)
  const created = r.direct ? line('Direct') : line('Created')
  const decided = line('Approved') ?? line('Rejected') ?? line('Direct')
  const cancelled = line('Cancelled')
  const applied = line('Applied') ?? (r.direct ? line('Direct') : undefined)
  const decide: RouteStop = r.status === 'Pending'
    ? { title: 'Qərar', who: 'Operatoru gözləyir', when: null, state: 'now' }
    : r.status === 'Cancelled'
      ? { title: 'Qərar', who: 'Müraciət ləğv edildi', when: cancelled?.atUtc ?? null, state: 'off' }
      : r.direct
        ? { title: 'Birbaşa', who: decided?.actor ?? '—', when: decided?.atUtc ?? null, state: 'done' }
        : {
            title: r.status === 'Rejected' ? 'Rədd' : 'Təsdiq',
            who: decided?.actor ?? r.decidedBy ?? '—',
            when: decided?.atUtc ?? r.decidedAtUtc,
            state: r.status === 'Rejected' ? 'stopped' : 'done',
          }
  return [
    { title: r.direct ? 'Başladan' : 'Müraciət edən', who: created?.actor ?? r.requestedBy ?? '—', when: created?.atUtc ?? r.createdAtUtc, state: 'done' },
    decide,
    applied
      ? { title: 'Tətbiq', who: 'İşçi hesabı yaradıldı', when: applied.atUtc, state: 'done' }
      : { title: 'Tətbiq', who: r.status === 'Pending' ? 'Təsdiqdən sonra' : 'Tətbiq olunmadı', when: null, state: 'off' },
  ]
}

export const EVENT_TEXT: Record<HistoryLine['kind'], string> = {
  Created: 'müraciəti göndərdi',
  Approved: 'təsdiqlədi',
  Rejected: 'rədd etdi',
  Cancelled: 'müraciəti ləğv etdi',
  Applied: 'işçi hesabı yaradıldı',
  ApplyRefused: 'təsdiqləmək istədi, amma tətbiq olunmadı',
  Direct: 'öz sahəsində birbaşa əlavə etdi',
}

/** Has waited a day or more for a decision — «24 saatdan çox gözləyən», never «gecikən». */
export function isLongWait(createdAtUtc: string, now: Date): boolean {
  return now.getTime() - new Date(createdAtUtc).getTime() >= LONG_WAIT_HOURS * 3_600_000
}

/**
 * A phone as a person reads it. The server keeps the nine digits after +994 («552220009»); the panel shows the number
 * the way it is dialled here, «055 222 00 09». Anything else is shown as it came.
 */
export function phoneShown(stored: string | null | undefined): string | null {
  if (!stored) return null
  return /^[1-9]\d{8}$/.test(stored) ? formatPhone(`0${stored}`, 'local') : stored
}
