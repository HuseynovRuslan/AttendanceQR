import { describe, expect, it } from 'vitest'
import { buildWorld, NOW, PERSONA_IDS } from './data'
import {
  approverIdFor, branchesInScope, canDecide, emptyDraft, reduce, validate, visibleRequests, type ChangeRequest, type Persona, type World,
} from './model'

const at = new Date(NOW.getTime() + 60_000).toISOString()
const persona = (w: World, id: string) => w.personas.find((p) => p.id === id)!
const keeperOf = (w: World, structureId: string) =>
  w.personas.find((p) => p.kind === 'timekeeper' && w.branches.find((b) => b.id === p.branchId)?.structureId === structureId)!
const pendingIn = (w: World, structureId: string) =>
  w.requests.find((r) => r.status === 'pending' && w.branches.find((b) => b.id === r.branchId)?.structureId === structureId)!

describe('the demo company has the approved shape', () => {
  it('38 branches in «Aeroport yolu», 24 in «Şəhər parkları», 21 not placed anywhere — 83 in all', () => {
    const w = buildWorld()
    const count = (id: string | null) => w.branches.filter((b) => b.structureId === id).length
    expect(w.structures.map((s) => s.name)).toEqual(['Aeroport yolu', 'Şəhər parkları'])
    expect([count('s-aero'), count('s-park'), count(null)]).toEqual([38, 24, 21])
    expect(w.branches).toHaveLength(83)
  })

  it('every structure has an Operator and a Viewer; a branch Viewer sees one branch only', () => {
    const w = buildWorld()
    for (const s of w.structures) {
      expect(persona(w, s.operatorId).kind).toBe('operator')
      expect(s.viewerIds.map((id) => persona(w, id).kind)).toEqual(['viewer'])
    }
    expect(branchesInScope(w, persona(w, PERSONA_IDS.viewerBranch))).toHaveLength(1)
  })

  it('is the same company every time (seeded)', () => {
    expect(JSON.stringify(buildWorld())).toBe(JSON.stringify(buildWorld()))
  })
})

describe('who decides', () => {
  it('the Operator of the structure — not a Timekeeper, not a Viewer, not the other Operator', () => {
    const w = buildWorld()
    const r = pendingIn(w, 's-aero')
    expect(canDecide(w, persona(w, PERSONA_IDS.opAero), r)).toBe(true)
    expect(canDecide(w, persona(w, PERSONA_IDS.opPark), r)).toBe(false)
    expect(canDecide(w, persona(w, PERSONA_IDS.viewerAero), r)).toBe(false)
    expect(canDecide(w, keeperOf(w, 's-aero'), r)).toBe(false)
    expect(canDecide(w, persona(w, PERSONA_IDS.admin), r)).toBe(false)
  })

  it('a branch in no structure goes to the Company administrator', () => {
    const w = buildWorld()
    const free = w.branches.find((b) => !b.structureId)!
    expect(approverIdFor(w, free.id)).toBe(PERSONA_IDS.admin)
  })

  it('nobody decides their own request', () => {
    const w = buildWorld()
    const op = persona(w, PERSONA_IDS.opAero)
    const own: ChangeRequest = { ...pendingIn(w, 's-aero'), createdById: op.id }
    expect(canDecide(w, op, own)).toBe(false)
    const out = reduce({ ...w, requests: [own] }, { type: 'approve', id: own.id, by: op, at })
    expect(out.error).toMatch(/Öz müraciətinizə/)
  })
})

describe('a decision', () => {
  it('happens once: a second approve or reject is refused, not repeated', () => {
    const w = buildWorld()
    const op = persona(w, PERSONA_IDS.opAero)
    const r = pendingIn(w, 's-aero')
    const once = reduce(w, { type: 'approve', id: r.id, by: op, at })
    expect(once.error).toBeUndefined()
    const done = once.world.requests.find((x) => x.id === r.id)!
    expect(done.status).toBe('approved')
    expect(done.events.map((e) => e.kind)).toEqual(['created', 'approved', 'applied'])
    expect(reduce(once.world, { type: 'approve', id: r.id, by: op, at }).error).toMatch(/ikinci qərar verilmir/)
    expect(reduce(once.world, { type: 'reject', id: r.id, by: op, at, reason: 'gec' }).error).toMatch(/ikinci qərar verilmir/)
  })

  it('a refusal needs a reason, and the reason is kept', () => {
    const w = buildWorld()
    const op = persona(w, PERSONA_IDS.opAero)
    const r = pendingIn(w, 's-aero')
    expect(reduce(w, { type: 'reject', id: r.id, by: op, at, reason: '  ' }).error).toMatch(/Rədd səbəbini/)
    const out = reduce(w, { type: 'reject', id: r.id, by: op, at, reason: 'Arayış yoxdur' })
    const rejected = out.world.requests.find((x) => x.id === r.id)!
    expect(rejected.status).toBe('rejected')
    expect(rejected.decisionNote).toBe('Arayış yoxdur')
  })

  it('an approved deactivation switches the person off; the roster is otherwise untouched', () => {
    const w = buildWorld()
    const r = w.requests.find((x) => x.type === 'employee_activation' && x.status === 'pending')!
    const s = w.branches.find((b) => b.id === r.branchId)!.structureId!
    const op = persona(w, w.structures.find((x) => x.id === s)!.operatorId)
    const out = reduce(w, { type: 'approve', id: r.id, by: op, at })
    expect(out.world.employees.find((e) => e.id === r.employeeId)!.active).toBe(false)
    expect(out.world.employees).toHaveLength(w.employees.length)
  })
})

describe('deleting a person', () => {
  it('a person with history cannot be asked for deletion — the form points to deactivation', () => {
    const w = buildWorld()
    const keeper = keeperOf(w, 's-aero')
    const withHistory = w.employees.find((e) => e.branchId === keeper.branchId && e.hasHistory)!
    const errors = validate(w, { ...emptyDraft(keeper.branchId!), type: 'employee_delete', employeeId: withHistory.id, note: 'işdən çıxıb' })
    expect(errors.employee).toMatch(/deaktivasiya/)
  })

  it('and if such a request exists anyway, it is not approved', () => {
    const w = buildWorld()
    const keeper = keeperOf(w, 's-aero')
    const withHistory = w.employees.find((e) => e.branchId === keeper.branchId && e.hasHistory)!
    const req: ChangeRequest = {
      id: 'R-x', type: 'employee_delete', status: 'pending', employeeId: withHistory.id, branchId: keeper.branchId!,
      createdById: keeper.id, createdAt: NOW.toISOString(), note: 'işdən çıxıb', changes: [],
      events: [{ at: NOW.toISOString(), actorId: keeper.id, kind: 'created' }],
    }
    const out = reduce({ ...w, requests: [req] }, { type: 'approve', id: 'R-x', by: persona(w, PERSONA_IDS.opAero), at })
    expect(out.error).toMatch(/silinmə təsdiqlənmir/)
    expect(out.world.employees.some((e) => e.id === withHistory.id)).toBe(true)
  })
})

describe('making a request', () => {
  const draftFor = (w: World, keeper: Persona) => {
    const emp = w.employees.find((e) => e.branchId === keeper.branchId && e.active)!
    return { ...emptyDraft(keeper.branchId!), type: 'vacation' as const, employeeId: emp.id, dateFrom: '2026-12-01', dateTo: '2026-12-10', note: 'İllik məzuniyyət' }
  }

  it('a Timekeeper asks for their own branch only, and it waits for the Operator', () => {
    const w = buildWorld()
    const keeper = keeperOf(w, 's-aero')
    const out = reduce(w, { type: 'create', draft: draftFor(w, keeper), by: keeper, id: 'R-new', at })
    expect(out.world.requests[0]).toMatchObject({ id: 'R-new', status: 'pending', createdById: keeper.id })
    const other = w.branches.find((b) => b.structureId === 's-park')!
    expect(reduce(w, { type: 'create', draft: { ...draftFor(w, keeper), branchId: other.id }, by: keeper, id: 'R-2', at }).error)
      .toMatch(/hüququnuz yoxdur/)
  })

  it('an Operator acting in their own structure applies at once, and it is on the record as direct', () => {
    const w = buildWorld()
    const op = persona(w, PERSONA_IDS.opAero)
    const keeper = keeperOf(w, 's-aero')
    const out = reduce(w, { type: 'create', draft: draftFor(w, keeper), by: op, id: 'R-op', at })
    expect(out.world.requests[0]).toMatchObject({ status: 'approved', direct: true, decidedById: op.id })
    expect(out.world.requests[0].events.map((e) => e.kind)).toEqual(['created', 'direct', 'applied'])
  })

  it('a Viewer cannot make one at all', () => {
    const w = buildWorld()
    const keeper = keeperOf(w, 's-aero')
    expect(reduce(w, { type: 'create', draft: draftFor(w, keeper), by: persona(w, PERSONA_IDS.viewerAero), id: 'R-v', at }).error)
      .toMatch(/hüququnuz yoxdur/)
  })

  it('checks dates, hours and the note', () => {
    const w = buildWorld()
    const keeper = keeperOf(w, 's-aero')
    const base = draftFor(w, keeper)
    expect(validate(w, { ...base, note: 'x' }).note).toBeDefined()
    expect(validate(w, { ...base, dateFrom: '2026-12-10', dateTo: '2026-12-01' }).dates).toBeDefined()
    expect(validate(w, { ...base, type: 'permission', dateFrom: '2026-12-01', timeFrom: '14:00', timeTo: '13:00' }).times).toBeDefined()
    expect(validate(w, { ...base, type: 'permission', dateFrom: '2026-12-01', timeFrom: '14:00', timeTo: '' }).times).toBeDefined()
    expect(validate(w, base)).toEqual({})
  })

  it('only the creator cancels, and only while it waits', () => {
    const w = buildWorld()
    const r = pendingIn(w, 's-aero')
    const creator = persona(w, r.createdById)
    expect(reduce(w, { type: 'cancel', id: r.id, by: persona(w, PERSONA_IDS.opAero), at }).error).toMatch(/Yalnız özünüzün/)
    const out = reduce(w, { type: 'cancel', id: r.id, by: creator, at })
    expect(out.world.requests.find((x) => x.id === r.id)!.status).toBe('cancelled')
  })
})

describe('what each role sees', () => {
  it('an Operator sees their structure only; a branch Viewer their branch only', () => {
    const w = buildWorld()
    const aero = new Set(w.branches.filter((b) => b.structureId === 's-aero').map((b) => b.id))
    expect(visibleRequests(w, persona(w, PERSONA_IDS.opAero)).every((r) => aero.has(r.branchId))).toBe(true)
    const v = persona(w, PERSONA_IDS.viewerBranch)
    expect(visibleRequests(w, v).every((r) => r.branchId === v.branchId)).toBe(true)
    expect(visibleRequests(w, persona(w, PERSONA_IDS.admin))).toHaveLength(w.requests.length)
  })
})
