// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest'
import type { ApprovalsMe, RequestDetail, RequestItem } from '../../../api/approvals'
import { AccessProvider } from '../../../auth/AccessContext'
import { ApprovalCenterPage } from './ApprovalCenterPage'
import { ApprovalsProvider } from './ApprovalsContext'
import { OperatorDashboardPage } from './OperatorDashboardPage'
import { TimekeeperWorkspacePage } from './TimekeeperWorkspacePage'

/**
 * The real screens, on the real router and providers, with /api/approvals answered by a fake server. What is asserted
 * is what a person sees and may press, and what reaches the server: one request per press, the PIN shown once and
 * nowhere else, the Viewer never handed a phone number.
 */

const s = vi.hoisted(() => ({
  companyAdmin: false,
  me: { enabled: true, branches: [], queueCount: 0 } as ApprovalsMe,
  items: [] as RequestItem[],
  details: {} as Record<string, RequestDetail>,
  create: null as unknown as Mock<(...a: unknown[]) => Promise<unknown>>,
  approve: null as unknown as Mock<(...a: unknown[]) => Promise<unknown>>,
  reject: null as unknown as Mock<(...a: unknown[]) => Promise<unknown>>,
}))

vi.mock('../../../auth/AuthContext', () => ({
  useAuth: () => ({
    token: 't', isAuthenticated: true, role: 'Manager', employeeId: 'me', email: 'sinaq@test.local',
    mustChangePin: false, saveToken: () => {}, logout: () => {}, profiles: [], switchProfile: () => {},
  }),
}))
vi.mock('../../../api/me', () => ({
  getMyAccess: vi.fn(async () => ({
    status: 200,
    data: { mode: 'Legacy', legacyRole: 'Manager', panel: true, companyAdmin: s.companyAdmin, permissions: [], assignments: [], branches: [] },
  })),
}))
vi.mock('../../../api/approvals', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/approvals')>()),
  getApprovalsMe: vi.fn(async () => ({ status: 200, data: s.me })),
  listRequests: vi.fn(async (view: string) => ({
    status: 200,
    data: { items: view === 'queue' ? s.items.filter((r) => r.status === 'Pending' && r.canDecide) : view === 'mine' ? s.items.filter((r) => r.requestedByMe) : s.items },
  })),
  getRequest: vi.fn(async (id: string) => (s.details[id] ? { status: 200, data: s.details[id] } : { status: 404, data: { error: 'RequestNotFound' } })),
  getFormOptions: vi.fn(async () => ({ status: 200, data: { positions: ['Bağban'], schedules: [{ id: 'sh1', name: 'Səhər', start: '08:00', end: '17:00', branchOnly: true }] } })),
  createEmployeeRequest: (...a: unknown[]) => s.create(...a),
  approveRequest: (...a: unknown[]) => s.approve(...a),
  rejectRequest: (...a: unknown[]) => s.reject(...a),
  cancelRequest: vi.fn(async () => ({ status: 200, data: { request: {} } })),
}))

const BRANCH = { locationId: 'b1', name: 'Aeroport filialı', structureId: 's1', structureName: 'Aeroport yolu', canRequest: false, canActDirectly: false, isApprover: false, standsIn: false, canView: true }

function item(over: Partial<RequestItem> = {}): RequestItem {
  return {
    id: 'r1', number: 1001, type: 'EmployeeCreate', status: 'Pending', direct: false, locationId: 'b1', locationName: 'Aeroport filialı',
    subject: 'Aysel Məmmədova', requestedBy: 'Günel Tabelçi', requestedByMe: false, createdAtUtc: new Date(Date.now() - 2 * 3_600_000).toISOString(),
    decidedBy: null, decidedAtUtc: null, decisionNote: null, waitingOver24h: false, canDecide: false, canCancel: false, ...over,
  }
}

function detail(r: RequestItem, hidden = false): RequestDetail {
  return {
    request: r,
    payload: {
      firstName: 'Aysel', lastName: 'Məmmədova', fatherName: 'Rəşid', phoneNumber: hidden ? null : '551234567',
      email: hidden ? null : 'aysel@example.az', position: 'Bağban', birthDate: hidden ? null : '1994-05-17', locationId: 'b1', scheduleId: null,
      fullName: 'Aysel Məmmədova',
    },
    contactsHidden: hidden, note: 'Sabahdan başlayır', decisionNote: null, employeeId: null,
    history: [{ kind: 'Created', atUtc: r.createdAtUtc, actor: 'Günel Tabelçi', note: 'Sabahdan başlayır', data: null }],
  }
}

function open(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AccessProvider>
        <ApprovalsProvider>
          <Routes>
            <Route path="/admin/approvals" element={<ApprovalCenterPage />} />
            <Route path="/admin/approvals/operator" element={<OperatorDashboardPage />} />
            <Route path="/admin/approvals/branch" element={<TimekeeperWorkspacePage />} />
          </Routes>
        </ApprovalsProvider>
      </AccessProvider>
    </MemoryRouter>,
  )
}

async function click(el: Element) {
  await act(async () => { fireEvent.click(el) })
}
async function type(el: Element, value: string) {
  await act(async () => { fireEvent.change(el, { target: { value } }) })
}

beforeEach(() => {
  s.companyAdmin = false
  s.items = []
  s.details = {}
  s.create = vi.fn<(...a: unknown[]) => Promise<unknown>>()
  s.approve = vi.fn<(...a: unknown[]) => Promise<unknown>>()
  s.reject = vi.fn<(...a: unknown[]) => Promise<unknown>>()
  localStorage.clear()
  sessionStorage.clear()
})
afterEach(cleanup)

describe('Timekeeper', () => {
  beforeEach(() => {
    s.me = { enabled: true, queueCount: 0, branches: [{ ...BRANCH, canRequest: true }] }
  })

  it('offers «Yeni işçi» only, and says the other eight are still done the old way', async () => {
    open('/admin/approvals/branch')
    expect(await screen.findByText('Hələ müraciət yoxdur')).toBeTruthy()
    expect(screen.getByText(/Hələlik təsdiqlə yalnız yeni işçi əlavə olunur/)).toBeTruthy()
    for (const other of ['Məzuniyyət', 'Xəstəlik', 'Davamiyyət düzəlişi', 'Növbə dəyişikliyi'])
      expect(screen.queryByRole('button', { name: new RegExp(other) })).toBeNull()
    // The roles are named in English.
    expect(screen.getAllByText('Timekeeper').length).toBeGreaterThan(0)
  })

  it('checks the form before sending, and one press is one request even when the connection drops', async () => {
    s.create
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce({ status: 201, data: { id: 'r9', number: 1009, status: 'Pending', locationName: 'Aeroport filialı', subject: 'Aysel Məmmədova', createdAtUtc: new Date().toISOString(), replayed: false } })
    open('/admin/approvals/branch')
    await click((await screen.findAllByRole('button', { name: 'Yeni işçi müraciəti' }))[0])
    const sheet = screen.getByRole('dialog')

    await click(within(sheet).getByRole('button', { name: 'Operatora göndər' }))
    expect(within(sheet).getByText('Ad və soyad yazılmalıdır.')).toBeTruthy()
    expect(within(sheet).getByText(/Telefon nömrəsi və ya e-poçt yazın/)).toBeTruthy()
    expect(s.create).not.toHaveBeenCalled()

    await type(within(sheet).getByLabelText('Ad'), 'Aysel')
    await type(within(sheet).getByLabelText('Soyad'), 'Məmmədova')
    await type(within(sheet).getByLabelText('Telefon (giriş üçün)'), '0551234567')
    await click(within(sheet).getByRole('button', { name: 'Operatora göndər' }))
    expect(await within(sheet).findByText(/İnternet bağlantısını yoxlayıb/)).toBeTruthy()

    await click(within(sheet).getByRole('button', { name: 'Operatora göndər' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(screen.getByText(/Müraciət R-1009 göndərildi/)).toBeTruthy()
    expect(s.create).toHaveBeenCalledTimes(2)
    // The retry of the dropped press carried the same key: the server answers with the request it already made.
    expect(s.create.mock.calls[0][1]).toBe(s.create.mock.calls[1][1])
    expect(s.create.mock.calls[0][0]).toMatchObject({ firstName: 'Aysel', lastName: 'Məmmədova', phoneNumber: '0551234567', locationId: 'b1' })
  })

  it('sends once when «Göndər» is pressed twice while the first is on its way', async () => {
    let answer!: (v: unknown) => void
    s.create.mockImplementation(() => new Promise((res) => { answer = res }))
    open('/admin/approvals/branch')
    await click((await screen.findAllByRole('button', { name: 'Yeni işçi müraciəti' }))[0])
    const sheet = screen.getByRole('dialog')
    await type(within(sheet).getByLabelText('Ad'), 'Aysel')
    await type(within(sheet).getByLabelText('Soyad'), 'Məmmədova')
    await type(within(sheet).getByLabelText('Telefon (giriş üçün)'), '0551234567')
    const send = within(sheet).getByRole('button', { name: 'Operatora göndər' })
    await click(send)
    await click(within(sheet).getByRole('button', { name: 'Göndərilir…' }))
    expect(s.create).toHaveBeenCalledTimes(1)
    await act(async () => { answer({ status: 201, data: { id: 'r9', number: 1009, status: 'Pending', locationName: '', subject: '', createdAtUtc: new Date().toISOString(), replayed: false } }) })
  })

  it('shows a rejection’s reason in the list', async () => {
    s.items = [item({ requestedByMe: true, status: 'Rejected', decidedBy: 'Rasim Operator', decidedAtUtc: new Date().toISOString(), decisionNote: 'Şəxsiyyət vəsiqəsi yoxdur' })]
    open('/admin/approvals/branch')
    expect(await screen.findByText('Rədd səbəbi: Şəxsiyyət vəsiqəsi yoxdur')).toBeTruthy()
  })
})

describe('Operator', () => {
  beforeEach(() => {
    s.me = { enabled: true, queueCount: 1, branches: [{ ...BRANCH, isApprover: true, canActDirectly: true, canRequest: true }] }
    const r = item({ canDecide: true })
    s.items = [r]
    s.details = { r1: detail(r) }
  })

  it('approves once, and the temporary PIN is shown once and kept nowhere', async () => {
    let answer!: (v: unknown) => void
    s.approve.mockImplementation(() => new Promise((res) => { answer = res }))
    open('/admin/approvals?id=r1')
    const panel = await screen.findByRole('region', { name: 'Müraciətin detalları' })
    await click(await within(panel).findByRole('button', { name: 'Təsdiqlə' }))
    await click(within(panel).getByRole('button', { name: 'Göndərilir…' }))
    expect(s.approve).toHaveBeenCalledTimes(1)

    s.details.r1 = { ...detail({ ...s.items[0], status: 'Approved', canDecide: false }), history: [] }
    await act(async () => { answer({ status: 200, data: { request: { id: 'r1' }, employeeId: 'e1', tempPin: '4821' } }) })
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('4821')).toBeTruthy()
    expect(JSON.stringify({ ...localStorage })).not.toContain('4821')
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('4821')

    await click(within(dialog).getByRole('button', { name: 'Bağla' }))
    expect(screen.queryByText('4821')).toBeNull()
  })

  it('does not reject without a reason', async () => {
    s.reject.mockResolvedValue({ status: 200, data: { request: { id: 'r1' } } })
    open('/admin/approvals?id=r1')
    const panel = await screen.findByRole('region', { name: 'Müraciətin detalları' })
    await click(await within(panel).findByRole('button', { name: 'Rədd et' }))
    await click(within(panel).getByRole('button', { name: 'Rəddi təsdiqlə' }))
    expect(within(panel).getByText('Rədd səbəbini yazın (ən azı 3 hərf)')).toBeTruthy()
    expect(s.reject).not.toHaveBeenCalled()
    await type(within(panel).getByLabelText(/Rədd səbəbi/), 'Telefon nömrəsi səhvdir')
    await click(within(panel).getByRole('button', { name: 'Rəddi təsdiqlə' }))
    expect(s.reject).toHaveBeenCalledWith('r1', 'Telefon nömrəsi səhvdir')
  })

  it('says so when somebody else decided first, and reads the request again', async () => {
    s.approve.mockResolvedValue({ status: 409, data: { error: 'AlreadyDecided', status: 'Approved' } })
    open('/admin/approvals?id=r1')
    const panel = await screen.findByRole('region', { name: 'Müraciətin detalları' })
    await click(await within(panel).findByRole('button', { name: 'Təsdiqlə' }))
    expect((await screen.findAllByText(/Bu müraciətə artıq qərar verilib \(Təsdiqləndi\)/)).length).toBeGreaterThan(0)
  })

  it('opens the operator screen with the queue oldest first', async () => {
    s.items = [
      item({ id: 'new', subject: 'Yeni gələn', canDecide: true, createdAtUtc: new Date(Date.now() - 3_600_000).toISOString() }),
      item({ id: 'old', subject: 'Çoxdan gözləyən', canDecide: true, createdAtUtc: new Date(Date.now() - 30 * 3_600_000).toISOString() }),
    ]
    open('/admin/approvals/operator')
    const queue = await screen.findByRole('region', { name: 'Gözləyən müraciətlər' })
    await waitFor(() => expect(within(queue).getAllByRole('button').length).toBe(2))
    const rows = within(queue).getAllByRole('button')
    expect(rows[0].textContent).toContain('Çoxdan gözləyən')
    expect(screen.getByText('24 saatdan çox gözləyir')).toBeTruthy()
  })
})

describe('Viewer', () => {
  it('reads the request without the phone, the e-mail or the birth date, and is offered no decision', async () => {
    s.me = { enabled: true, queueCount: 0, branches: [BRANCH] }
    const r = item()
    s.items = [r]
    s.details = { r1: detail(r, true) }
    open('/admin/approvals?id=r1')
    const panel = await screen.findByRole('region', { name: 'Müraciətin detalları' })
    expect((await within(panel).findAllByText('Gizlədilib')).length).toBe(3)
    expect(within(panel).queryByText('551234567')).toBeNull()
    expect(within(panel).queryByRole('button', { name: 'Təsdiqlə' })).toBeNull()
    expect(within(panel).getByText(/Viewer rolundasınız/)).toBeTruthy()
  })
})

describe('switched off', () => {
  it('shows nothing of the flow while the server says it is off', async () => {
    s.me = { enabled: false }
    open('/admin/approvals')
    expect(await screen.findByText('Təsdiq sistemi sizin sahənizdə aktiv deyil')).toBeTruthy()
  })
})
