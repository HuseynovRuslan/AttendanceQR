import { describe, expect, it } from 'vitest'
import type { ApprovalBranch, RequestDetail } from '../../../api/approvals'
import type { MyAccess } from '../../../api/me'
import {
  EMPTY_DRAFT, NETWORK, errorText, isLongWait, newIdempotencyKey, phoneShown, routeOf, screensFor, validateDraft, viewOf,
} from './model'

const branch = (over: Partial<ApprovalBranch>): ApprovalBranch => ({
  locationId: 'b1', name: 'Filial 1', structureId: 's1', structureName: 'Aeroport yolu',
  canRequest: false, canActDirectly: false, isApprover: false, standsIn: false, canView: false, ...over,
})
const access = (companyAdmin = false) => ({ companyAdmin } as MyAccess)

describe('who somebody is, from the server', () => {
  it('nobody is anything while the switch is off — whatever the branches say', () => {
    const v = viewOf(access(true), { enabled: false, branches: [branch({ isApprover: true })] })
    expect(v.enabled).toBe(false)
    expect(v.roles).toEqual([])
    expect(screensFor(v)).toEqual([])
  })

  it('an Operator decides somewhere, and sees the operator screen and the centre', () => {
    const v = viewOf(access(), { enabled: true, queueCount: 3, branches: [branch({ isApprover: true, canActDirectly: true, canRequest: true, canView: true })] })
    expect(v.roles).toEqual(['operator'])
    expect(screensFor(v)).toEqual(['operator', 'center'])
    expect(v.queueCount).toBe(3)
  })

  it('a Timekeeper asks where they do not decide, and gets their branch screen', () => {
    const v = viewOf(access(), { enabled: true, branches: [branch({ canRequest: true, canView: true })] })
    expect(v.roles).toEqual(['timekeeper'])
    expect(screensFor(v)).toEqual(['center', 'branch'])
    expect(v.requestBranches.map((b) => b.locationId)).toEqual(['b1'])
  })

  it('a Viewer only reads: the centre, nothing to send, nothing to decide', () => {
    const v = viewOf(access(), { enabled: true, branches: [branch({ canView: true })] })
    expect(v.roles).toEqual(['viewer'])
    expect(screensFor(v)).toEqual(['center'])
  })

  it('the Company Admin is named as such and is not an operator by being an admin', () => {
    const v = viewOf(access(true), { enabled: true, branches: [branch({ canView: true, canActDirectly: true, canRequest: true })] })
    expect(v.roles).toEqual(['companyAdmin'])
    expect(screensFor(v)).toEqual(['center'])
  })

  it('no area that concerns them: no screens at all', () => {
    expect(screensFor(viewOf(access(), { enabled: true, branches: [] }))).toEqual([])
  })
})

describe('what to tell a person when the server says no', () => {
  it('names the network, an expired session, and every code the desks answer with', () => {
    expect(errorText(NETWORK)).toMatch(/İnternet bağlantısını/)
    expect(errorText(401)).toMatch(/Sessiyanın vaxtı bitib/)
    expect(errorText(409, { error: 'AlreadyDecided', status: 'Approved' })).toBe('Bu müraciətə artıq qərar verilib (Təsdiqləndi). Məlumat yeniləndi.')
    expect(errorText(409, { error: 'DuplicatePendingRequest', number: 1004 })).toBe('Bu şəxs üçün artıq gözləyən müraciət var (R-1004).')
    expect(errorText(409, { error: 'ApplyRefused', reason: 'PhoneAlreadyExists' })).toMatch(/telefon nömrəsi artıq başqa işçidədir.*gözləyir/)
    expect(errorText(403, { error: 'SelfDecisionNotAllowed' })).toBe('Öz müraciətinizə qərar verə bilməzsiniz.')
    expect(errorText(403, { error: 'ApprovalRequired', canRequest: true })).toMatch(/Yeni işçi müraciəti/)
    expect(errorText(404, { error: 'ApprovalsDisabled' })).toMatch(/aktiv deyil/)
  })

  it('falls back to the status when the code is unknown, never to an English word', () => {
    expect(errorText(403, { error: 'Something' })).toBe('Bu əməliyyat üçün icazəniz yoxdur.')
    expect(errorText(500)).toMatch(/Serverdə xəta/)
  })
})

describe('the «Yeni işçi» form, before anything is sent', () => {
  it('asks for a name and a way to log in', () => {
    const e = validateDraft(EMPTY_DRAFT)
    expect(e.name).toBeTruthy()
    expect(e.contact).toBeTruthy()
  })

  it('accepts a name with a phone, or with an e-mail', () => {
    expect(validateDraft({ ...EMPTY_DRAFT, firstName: 'Aysel', lastName: 'Məmmədova', phone: '0551234567' })).toEqual({})
    expect(validateDraft({ ...EMPTY_DRAFT, firstName: 'Aysel', lastName: 'Məmmədova', email: 'a@b.az' })).toEqual({})
  })

  it('refuses a short phone, a broken e-mail and a note over 500 letters', () => {
    const e = validateDraft({ ...EMPTY_DRAFT, firstName: 'A', lastName: 'B', phone: '055123', email: 'x@', note: 'a'.repeat(501) })
    expect(e.phone).toBeTruthy()
    expect(e.email).toBeTruthy()
    expect(e.note).toBeTruthy()
  })

  it('gives every press its own key', () => {
    const keys = new Set(Array.from({ length: 50 }, newIdempotencyKey))
    expect(keys.size).toBe(50)
  })
})

describe('the request’s way', () => {
  const base = (over: Partial<RequestDetail['request']>, history: RequestDetail['history']) => ({
    request: {
      id: 'r', number: 1001, type: 'EmployeeCreate' as const, status: 'Pending' as const, direct: false, locationId: 'b1',
      locationName: 'Filial 1', subject: 'Aysel Məmmədova', requestedBy: 'Tabelçi', requestedByMe: false,
      createdAtUtc: '2026-11-11T06:00:00Z', decidedBy: null, decidedAtUtc: null, decisionNote: null,
      waitingOver24h: false, canDecide: true, canCancel: false, ...over,
    },
    history,
  })

  it('waits at the decision while pending', () => {
    const stops = routeOf(base({}, [{ kind: 'Created', atUtc: '2026-11-11T06:00:00Z', actor: 'Tabelçi', note: null, data: null }]))
    expect(stops.map((s) => s.state)).toEqual(['done', 'now', 'off'])
    expect(stops[0].who).toBe('Tabelçi')
  })

  it('shows the approver and the account once approved', () => {
    const stops = routeOf(base({ status: 'Approved' }, [
      { kind: 'Created', atUtc: '2026-11-11T06:00:00Z', actor: 'Tabelçi', note: null, data: null },
      { kind: 'Approved', atUtc: '2026-11-11T07:00:00Z', actor: 'Operator', note: null, data: null },
      { kind: 'Applied', atUtc: '2026-11-11T07:00:00Z', actor: 'Operator', note: null, data: null },
    ]))
    expect(stops.map((s) => s.state)).toEqual(['done', 'done', 'done'])
    expect(stops[1].who).toBe('Operator')
  })

  it('stops at a rejection and applies nothing', () => {
    const stops = routeOf(base({ status: 'Rejected' }, [
      { kind: 'Created', atUtc: '2026-11-11T06:00:00Z', actor: 'Tabelçi', note: null, data: null },
      { kind: 'Rejected', atUtc: '2026-11-11T07:00:00Z', actor: 'Operator', note: 'Sənəd yoxdur', data: null },
    ]))
    expect(stops.map((s) => s.state)).toEqual(['done', 'stopped', 'off'])
    expect(stops[2].who).toBe('Tətbiq olunmadı')
  })

  it('calls a day of waiting long, and an hour not', () => {
    const now = new Date('2026-11-12T07:00:00Z')
    expect(isLongWait('2026-11-11T06:00:00Z', now)).toBe(true)
    expect(isLongWait('2026-11-12T06:00:00Z', now)).toBe(false)
  })
})

describe('a phone, as it is read', () => {
  it('turns the stored nine digits into the number as it is dialled, and leaves anything else alone', () => {
    expect(phoneShown('552220009')).toBe('055 222 00 09')
    expect(phoneShown('0552220009')).toBe('0552220009')
    expect(phoneShown(null)).toBeNull()
  })
})
