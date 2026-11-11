import type { Branch, ChangeRequest, Employee, Persona, RequestType, Structure, World } from './model'

/**
 * Demo data for the prototype. Every person and branch name here is invented: the repository is public, and a
 * design prototype has no business carrying a client's staff list. The SHAPE is the approved one — «Aeroport yolu»
 * with 38 branches, «Şəhər parkları» with 24, an Operator and a Viewer on each, a Timekeeper per branch — and the
 * 21 branches whose structure is not decided yet stay outside any structure rather than being guessed into one.
 *
 * Seeded, so every screenshot and test sees the same company.
 */

/** «Now» in the prototype: requests age against this, not the machine clock, so «24 saatdan çox» is stable. */
export const NOW = new Date('2026-11-11T10:00:00+04:00')

function rng(seed: number) {
  let a = seed >>> 0
  return () => {
    a = (a + 0x6d2b79f5) >>> 0
    let t = a
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

const FIRST_M = ['Elvin', 'Rəşad', 'Tural', 'Nicat', 'Kamal', 'Ramil', 'Samir', 'Fərid', 'Anar', 'Ülvi', 'Zaur', 'Elnur', 'Mübariz', 'Coşqun', 'Taleh', 'Emin', 'Vüsal', 'Ruslan', 'Ceyhun', 'Ilkin']
const FIRST_F = ['Günel', 'Aysel', 'Nərmin', 'Səbinə', 'Lalə', 'Könül', 'Aytən', 'Fidan', 'Gülnar', 'Sevinc', 'Nigar', 'Ülkər']
const LAST = ['Məmmədov', 'Əliyev', 'Həsənov', 'Hüseynov', 'Quliyev', 'Abbasov', 'Rzayev', 'Kərimov', 'Süleymanov', 'Nəsirov', 'Cəfərov', 'Babayev', 'Orucov', 'Qasımov', 'Mustafayev', 'Bağırov', 'Zeynalov', 'İsmayılov']
/** The demo's job-title catalogue — titles are picked from a catalogue, never typed (the product's own rule). */
export const POSITIONS = ['Bağban', 'Təmizlik işçisi', 'Fəhlə', 'Sürücü', 'Mühafizəçi', 'Usta', 'Briqadir', 'Suvarma operatoru']
export const SHIFTS = ['08:00–17:00', '07:00–16:00', '09:00–18:00', '20:00–08:00']

const AERO_PLACES = ['Şərq qovşağı', 'Dairəvi yol', 'Körpüaltı', 'Yaşıl zolaq', 'Xidmət yolu', 'Dayanacaq', 'Giriş meydanı', 'Səkilər', 'Ağaclıq', 'Qəsəbə yolu', 'Yan yol', 'Kanal boyu', 'Zolaqarası', 'Döngə']
const PARK_PLACES = ['Çinar', 'Şam', 'Lalə', 'Bulvar', 'Fəvvarə', 'Gül', 'Nərgiz', 'Bənövşə', 'Yasəmən', 'Sərv', 'Akasiya', 'Dəniz', 'Göl', 'Bağça', 'Səhnə', 'Çay']
const OTHER_PLACES = ['Anbar', 'Emalatxana', 'Texniki baza', 'Ofis', 'Qazon sahəsi', 'Yeni sahə', 'Kənd yolu', 'Qəsəbə mərkəzi', 'Sahil zolağı', 'Meşə zolağı', 'Bazar ətrafı']

function personName(r: () => number): string {
  const female = r() < 0.22
  const first = female ? FIRST_F[Math.floor(r() * FIRST_F.length)] : FIRST_M[Math.floor(r() * FIRST_M.length)]
  const last = LAST[Math.floor(r() * LAST.length)]
  return `${female ? last.replace(/ov$/, 'ova').replace(/ev$/, 'eva') : last} ${first}`
}

function phone(r: () => number): string {
  const d = () => Math.floor(r() * 10)
  return `50 ${d()}${d()}${d()} ${d()}${d()} ${d()}${d()}`
}

export const PERSONA_IDS = {
  admin: 'p-admin',
  opAero: 'p-op-aero',
  viewerAero: 'p-vw-aero',
  opPark: 'p-op-park',
  viewerPark: 'p-vw-park',
  viewerBranch: 'p-vw-branch',
} as const

export function buildWorld(): World {
  const r = rng(20261111)
  const structures: Structure[] = [
    { id: 's-aero', name: 'Aeroport yolu', operatorId: PERSONA_IDS.opAero, viewerIds: [PERSONA_IDS.viewerAero] },
    { id: 's-park', name: 'Şəhər parkları', operatorId: PERSONA_IDS.opPark, viewerIds: [PERSONA_IDS.viewerPark] },
  ]
  const personas: Persona[] = [
    { id: PERSONA_IDS.admin, name: 'Aynur Quliyeva', kind: 'companyAdmin' },
    { id: PERSONA_IDS.opAero, name: 'Elşən Məmmədov', kind: 'operator', structureId: 's-aero' },
    { id: PERSONA_IDS.viewerAero, name: 'Fuad Əliyev', kind: 'viewer', structureId: 's-aero' },
    { id: PERSONA_IDS.opPark, name: 'Orxan Hüseynzadə', kind: 'operator', structureId: 's-park' },
    { id: PERSONA_IDS.viewerPark, name: 'Kamran Abbaslı', kind: 'viewer', structureId: 's-park' },
  ]

  const branches: Branch[] = []
  const used = new Set<string>()
  const uniqueName = (pool: string[], suffix: (base: string, n: number) => string) => {
    for (let n = 1; ; n++) {
      const base = pool[Math.floor(r() * pool.length)]
      const name = suffix(base, n)
      if (!used.has(name)) { used.add(name); return name }
    }
  }
  const addBranches = (count: number, structureId: string | null, pool: string[], suffix: (b: string, n: number) => string) => {
    for (let i = 0; i < count; i++) {
      const id = `b-${structureId ?? 'free'}-${String(i + 1).padStart(2, '0')}`
      // One Timekeeper per branch is the plan; a few are not appointed yet, and the screens must say so.
      const hasKeeper = structureId !== null && r() > 0.12
      let timekeeperId: string | null = null
      if (hasKeeper) {
        timekeeperId = `p-tk-${id}`
        personas.push({ id: timekeeperId, name: personName(r), kind: 'timekeeper', branchId: id })
      }
      branches.push({ id, name: uniqueName(pool, suffix), structureId, timekeeperId, viewerIds: [] })
    }
  }
  addBranches(38, 's-aero', AERO_PLACES, (b, n) => (n <= 1 ? b : `${b} ${n}`))
  addBranches(24, 's-park', PARK_PLACES, (b, n) => (n <= 1 ? `${b} parkı` : `${b} parkı ${n}`))
  addBranches(21, null, OTHER_PLACES, (b, n) => `${b} ${n}`)

  // A Viewer appointed to one branch only — the narrow end of the Viewer role.
  const watched = branches.find((b) => b.structureId === 's-park' && b.timekeeperId)!
  watched.viewerIds.push(PERSONA_IDS.viewerBranch)
  personas.push({ id: PERSONA_IDS.viewerBranch, name: 'Səbinə Rəsulova', kind: 'viewer', branchId: watched.id })

  const employees: Employee[] = []
  for (const b of branches) {
    const count = 3 + Math.floor(r() * 10)
    for (let i = 0; i < count; i++) {
      employees.push({
        id: `e-${b.id}-${i + 1}`, name: personName(r), position: POSITIONS[Math.floor(r() * POSITIONS.length)], branchId: b.id,
        phone: phone(r), shift: SHIFTS[Math.floor(r() * SHIFTS.length)], active: r() > 0.06, hasHistory: r() > 0.12,
      })
    }
  }

  const world: World = { companyName: 'Demo Abadlıq MMC', structures, branches, employees, personas, requests: [] }
  world.requests = seedRequests(world, r)
  return world
}

function hoursAgo(h: number): string {
  return new Date(NOW.getTime() - h * 3_600_000).toISOString()
}

function day(offset: number): string {
  const d = new Date(NOW.getTime() + offset * 86_400_000)
  return d.toISOString().slice(0, 10)
}

/** A believable morning's queue: a few waiting (two of them for over a day), and a history of decisions. */
function seedRequests(w: World, r: () => number): ChangeRequest[] {
  const out: ChangeRequest[] = []
  const keeperBranches = w.branches.filter((b) => b.timekeeperId && b.structureId)
  const pick = <T,>(xs: T[]) => xs[Math.floor(r() * xs.length)]
  let n = 0
  const make = (
    type: RequestType, status: ChangeRequest['status'], age: number,
    opts: Partial<ChangeRequest> & { branch?: typeof keeperBranches[number]; employeeIndex?: number } = {},
  ) => {
    const branch = opts.branch ?? pick(keeperBranches)
    const staff = w.employees.filter((e) => e.branchId === branch.id && e.active)
    const emp = type === 'employee_create' ? null : staff[(opts.employeeIndex ?? 0) % staff.length]
    const operatorId = w.structures.find((s) => s.id === branch.structureId)!.operatorId
    const createdAt = hoursAgo(age)
    const id = `R-${String(1040 + ++n)}`
    const req: ChangeRequest = {
      id, type, status, employeeId: emp?.id ?? null, branchId: branch.id, createdById: branch.timekeeperId!, createdAt,
      note: opts.note ?? '', changes: opts.changes ?? [], dateFrom: opts.dateFrom, dateTo: opts.dateTo, timeFrom: opts.timeFrom,
      timeTo: opts.timeTo, events: [{ at: createdAt, actorId: branch.timekeeperId!, kind: 'created', note: opts.note }],
    }
    if (status === 'approved' || status === 'rejected') {
      const decidedAt = hoursAgo(Math.max(0.2, age - 1.5 - r() * 3))
      req.decidedById = operatorId
      req.decidedAt = decidedAt
      if (status === 'rejected') req.decisionNote = opts.decisionNote
      req.events.push({ at: decidedAt, actorId: operatorId, kind: status, note: opts.decisionNote })
      if (status === 'approved') req.events.push({ at: decidedAt, actorId: operatorId, kind: 'applied' })
    }
    if (status === 'cancelled') req.events.push({ at: hoursAgo(age - 0.5), actorId: branch.timekeeperId!, kind: 'cancelled' })
    out.push(req)
    return req
  }

  const aero = keeperBranches.filter((b) => b.structureId === 's-aero')
  const park = keeperBranches.filter((b) => b.structureId === 's-park')
  const empOf = (b: typeof keeperBranches[number], i = 0) => w.employees.filter((e) => e.branchId === b.id && e.active)[i]

  // Waiting — two of them for more than a day.
  make('vacation', 'pending', 31, { branch: aero[0], note: 'İllik məzuniyyət, ərizə ofisə verilib', dateFrom: day(3), dateTo: day(16) })
  make('permission', 'pending', 27, { branch: aero[1], note: 'Həkim qəbulu', dateFrom: day(1), dateTo: day(1), timeFrom: '10:00', timeTo: '13:00' })
  make('employee_update', 'pending', 5, {
    branch: aero[0], employeeIndex: 1, note: 'Nömrəsini dəyişib, köhnə nömrə bağlıdır',
    changes: [{ field: 'phone', label: 'Telefon', before: empOf(aero[0], 1).phone, after: '55 214 08 63' }],
  })
  make('employee_create', 'pending', 3, {
    branch: aero[2], note: 'Briqadaya yeni işçi qəbul olunub, sənədləri tamdır',
    changes: [
      { field: 'name', label: 'Ad, soyad', before: null, after: 'Kərimov Tural' },
      { field: 'position', label: 'Vəzifə', before: null, after: 'Bağban' },
      { field: 'phone', label: 'Telefon', before: null, after: '70 318 22 41' },
    ],
  })
  make('sick', 'pending', 2, { branch: aero[3], note: 'Xəstəlik vərəqəsi təqdim olunub', dateFrom: day(-2), dateTo: day(2) })
  make('shift_change', 'pending', 1.2, {
    branch: park[0], note: 'Gecə gözətçiliyinə keçir',
    changes: [{ field: 'shift', label: 'Növbə', before: empOf(park[0]).shift, after: '20:00–08:00' }], dateFrom: day(2), dateTo: day(2),
  })
  make('attendance_fix', 'pending', 0.6, { branch: park[1], note: 'Telefonu xarab olub, çıxışı vura bilməyib — 17:05-də işdən çıxıb', dateFrom: day(-1), dateTo: day(-1) })
  make('employee_activation', 'pending', 4, {
    branch: park[2], note: 'İşdən çıxıb, ərizəsi var',
    changes: [{ field: 'active', label: 'Status', before: 'Aktiv', after: 'Deaktiv' }],
  })

  // Decided.
  make('vacation', 'approved', 70, { branch: aero[4], note: 'Ailə vəziyyəti ilə əlaqədar', dateFrom: day(-10), dateTo: day(-3) })
  make('permission', 'approved', 50, { branch: park[0], note: 'Övladının məktəb tədbiri', dateFrom: day(-1), dateTo: day(-1), timeFrom: '14:00', timeTo: '16:00' })
  make('attendance_fix', 'rejected', 46, {
    branch: aero[1], note: 'Girişi vurmağı unudub', dateFrom: day(-3), dateTo: day(-3),
    decisionNote: 'Həmin gün kamerada görünmür; briqadirin yazılı izahatı lazımdır',
  })
  make('employee_delete', 'rejected', 40, {
    branch: aero[2], employeeIndex: 2, note: 'Artıq işləmir',
    changes: [{ field: 'exists', label: 'Hesab', before: 'Var', after: 'Silinir' }],
    decisionNote: 'Tarixçəsi var — silmək olmaz. Deaktivasiya müraciəti göndərin.',
  })
  make('sick', 'approved', 22, { branch: park[3], note: 'Xəstəxana arayışı', dateFrom: day(-4), dateTo: day(-1) })
  make('permission', 'cancelled', 18, { branch: park[1], note: 'Səhvən göndərilib', dateFrom: day(0), dateTo: day(0) })
  make('shift_change', 'approved', 12, {
    branch: aero[5], note: 'Səhər növbəsinə keçir',
    changes: [{ field: 'shift', label: 'Növbə', before: empOf(aero[5]).shift, after: '07:00–16:00' }], dateFrom: day(1), dateTo: day(1),
  })

  // An Operator acting directly in their own structure: applied at once, on the record.
  const direct = make('employee_update', 'approved', 8, {
    branch: aero[6], note: 'Vəzifə dəyişib — briqadir təyin olunub',
    changes: [{ field: 'position', label: 'Vəzifə', before: empOf(aero[6]).position, after: 'Briqadir' }],
  })
  const operator = w.structures[0].operatorId
  direct.createdById = operator
  direct.direct = true
  direct.events = [
    { at: direct.createdAt, actorId: operator, kind: 'created', note: direct.note },
    { at: direct.createdAt, actorId: operator, kind: 'direct' },
    { at: direct.createdAt, actorId: operator, kind: 'applied' },
  ]
  direct.decidedAt = direct.createdAt
  return out.sort((a, b) => b.createdAt.localeCompare(a.createdAt))
}
