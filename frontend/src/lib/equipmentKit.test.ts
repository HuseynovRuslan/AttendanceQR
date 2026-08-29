import { describe, expect, it } from 'vitest'
import { countKit, groupByArea, hasKind, kitLabel, readKit, type KitSource } from './equipmentKit'

/**
 * The chips on an equipment card are read out of prose somebody typed into a spreadsheet, so the
 * question these tests answer is not "does it parse" but "when it is unsure, does it stay quiet".
 *
 * A wrong KIND is a cosmetic mistake — the card shows the real text one click away. A wrong COUNT is
 * not: it is a number an admin would repeat when ordering. So every case below that involves a digit
 * exists to check we do not invent one.
 */

const EMPTY: KitSource = { equipment: null, systemUnit: null, monitor: null, otherEquipment: null }
const row = (p: Partial<KitSource>): KitSource => ({ ...EMPTY, ...p })

describe('what a line holds', () => {
  it('reads the register\'s own sentence', () => {
    // The example from the entity's own documentation.
    const kit = readKit(row({ equipment: '1 ədəd masaüstü ofis kompüteri, 2 ədəd monitor HP 27"' }))

    expect(kit).toEqual([
      { kind: 'desktop', count: 1 },
      { kind: 'monitor', count: 2 },
    ])
  })

  it('puts the machine before its accessories', () => {
    const kit = readKit(row({ equipment: '1 ədəd monitor, 1 ədəd printer, 1 ədəd noutbuk' }))

    expect(kit.map((k) => k.kind)).toEqual(['laptop', 'monitor', 'printer'])
  })

  it('separates lines as well as commas', () => {
    const kit = readKit(row({ otherEquipment: 'Printer Canon\nSkaner HP' }))

    expect(kit.map((k) => k.kind)).toEqual(['printer', 'scanner'])
  })

  it('adds up two fragments of the same kind', () => {
    const kit = readKit(row({ equipment: '2 ədəd monitor Dell', otherEquipment: '1 ədəd monitor HP' }))

    expect(kit).toEqual([{ kind: 'monitor', count: 3 }])
  })
})

describe('numbers are only ever quoted, never inferred', () => {
  it('does not read a screen size as a quantity', () => {
    // "monitor HP 27" is one monitor of 27 inches. This is the mistake that would put "27 monitor"
    // on a card, and it is the reason the digit search is anchored rather than free.
    expect(readKit(row({ equipment: 'monitor HP 27"' }))).toEqual([{ kind: 'monitor', count: null }])
  })

  it('does not read a specification as a quantity', () => {
    expect(readKit(row({ equipment: 'kompüter i5 11-ci nəsil' }))).toEqual([{ kind: 'desktop', count: null }])
  })

  it('says the kind without a number when none is written', () => {
    const kit = readKit(row({ equipment: 'masaüstü kompüter, monitor' }))

    expect(kit).toEqual([
      { kind: 'desktop', count: null },
      { kind: 'monitor', count: null },
    ])
    expect(kitLabel(kit[0]!)).toBe('Sistem bloku')
    expect(kitLabel(kit[1]!)).toBe('Monitor')
  })

  it('labels a written count', () => {
    expect(kitLabel({ kind: 'monitor', count: 2 })).toBe('2 monitor')
  })

  it('refuses a count that is obviously not one', () => {
    // A four-digit run is a model number that happened to sit before a unit word.
    expect(readKit(row({ equipment: '2024 ədəd monitor' }))).toEqual([{ kind: 'monitor', count: null }])
  })
})

describe('the specification columns prove presence, not quantity', () => {
  it('counts nothing from a multi-line spec', () => {
    // "one line per machine" is a convention in the source file, not a rule anyone is held to — the
    // day somebody wraps a single spec across two lines, counting lines would report two computers.
    const kit = readKit(row({ systemUnit: 'i5 11-ci nəsil\n16 GB RAM\n512 SSD' }))

    expect(kit).toEqual([{ kind: 'desktop', count: null }])
  })

  it('still shows the kind when only the spec column is filled', () => {
    expect(readKit(row({ monitor: 'HP P27h' }))).toEqual([{ kind: 'monitor', count: null }])
  })

  it('keeps a count the prose gave it', () => {
    // Both columns describe the same monitors; the spec column must not erase the written number.
    const kit = readKit(row({ equipment: '2 ədəd monitor', monitor: 'HP P27h' }))

    expect(kit).toEqual([{ kind: 'monitor', count: 2 }])
  })
})

describe('Azerbaijani text', () => {
  it('matches a word that starts with İ', () => {
    // 'İ'.toLowerCase() is 'i' + U+0307, which matches no literal in the keyword table. The backend
    // importer lost an entire column to exactly this; here it would silently drop the chip.
    expect(hasKind(row({ equipment: 'İki ədəd MONİTOR' }), 'monitor')).toBe(true)
  })

  it('is not confused by capitals or Russian', () => {
    expect(hasKind(row({ equipment: 'НОУТБУК Dell' }), 'laptop')).toBe(true)
    expect(hasKind(row({ equipment: 'NOUTBUK' }), 'laptop')).toBe(true)
  })

  it('calls a laptop a laptop even when the word "kompüter" is next to it', () => {
    expect(readKit(row({ equipment: 'noutbuk kompüteri' }))).toEqual([{ kind: 'laptop', count: null }])
  })
})

describe('the edges of the register', () => {
  it('shows nothing for a line with no equipment at all', () => {
    expect(readKit(EMPTY)).toEqual([])
  })

  it('shows something for a line describing kit we have no word for', () => {
    // An empty chip row would read as "nothing issued to this person", which is a different fact.
    expect(readKit(row({ equipment: 'Plansetli qurğu' }))).toEqual([{ kind: 'other', count: null }])
  })

  it('ignores whitespace-only cells', () => {
    expect(readKit(row({ equipment: '   ', systemUnit: '\n' }))).toEqual([])
  })
})

describe('the headline totals', () => {
  const rows = [
    row({ equipment: '1 ədəd masaüstü kompüter, 2 ədəd monitor' }),
    row({ equipment: 'masaüstü kompüter, monitor' }),   // no numbers written
    row({ otherEquipment: 'Printer Canon' }),
    row({}),                                            // nothing issued
  ]

  it('counts rows and devices separately', () => {
    const totals = countKit(rows)
    const by = (k: string) => totals.find((t) => t.kind === k)

    // Two lines mention a monitor; one of them says there are two of them.
    expect(by('monitor')).toEqual({ kind: 'monitor', people: 2, devices: 3 })
    expect(by('desktop')).toEqual({ kind: 'desktop', people: 2, devices: 2 })
    expect(by('printer')).toEqual({ kind: 'printer', people: 1, devices: 1 })
  })

  it('treats an unnumbered mention as one, so the total is a floor', () => {
    // This is the whole licence for a device count on a screen that refuses to print "1 monitor" on
    // a card: a mention is at least one, so the sum is a minimum and is labelled as one. It must
    // never round the other way.
    expect(countKit([row({ equipment: 'monitor' })])[0]!.devices).toBe(1)
  })

  it('leaves out kinds nobody holds', () => {
    // Zeros hidden — the same call the dashboard made. A tile reading 0 is a tile you read and
    // discard, every time.
    expect(countKit(rows).map((t) => t.kind)).toEqual(['desktop', 'monitor', 'printer'])
  })

  it('counts nothing for an empty register', () => {
    expect(countKit([])).toEqual([])
  })
})

describe('gathering the register by site', () => {
  const at = (area: string | null, n: number) => Array.from({ length: n }, () => ({ area }))

  it('puts the biggest site first, not the alphabet', () => {
    // The reason this exists: 23 sites for 80 people means the alphabet would open the page with
    // whichever one-person site starts with an A, and bury the main office in the middle.
    const groups = groupByArea([...at('Zəhmət', 9), ...at('Ambulator', 3), ...at('Mərkəz', 12)])

    expect(groups.map((g) => g.area)).toEqual(['Mərkəz', 'Zəhmət', 'Ambulator'])
  })

  it('gathers the long tail of one- and two-person sites', () => {
    const groups = groupByArea([
      ...at('Mərkəz', 12), ...at('Bərpa', 8),
      ...at('A', 1), ...at('B', 2), ...at('C', 1),
    ])

    expect(groups.map((g) => g.area)).toEqual(['Mərkəz', 'Bərpa', 'Digər ərazilər'])
    expect(groups[2]!.rows).toHaveLength(4)
    // Those cards have to name their own site — the heading no longer does it for them.
    expect(groups[2]!.merged).toBe(true)
    expect(groups[0]!.merged).toBe(false)
  })

  it('leaves a small site alone when there is nothing to gather it with', () => {
    // Folding a single two-person site into "Digər ərazilər" only renames it, and spends a real
    // heading to gain nothing.
    const groups = groupByArea([...at('Mərkəz', 12), ...at('Bərpa', 2)])

    expect(groups.map((g) => g.area)).toEqual(['Mərkəz', 'Bərpa'])
  })

  it('treats a blank site as tail rather than as a place', () => {
    const groups = groupByArea([...at('Mərkəz', 12), ...at(null, 9), ...at('A', 1), ...at('B', 1)])

    expect(groups.map((g) => g.area)).toEqual(['Mərkəz', 'Digər ərazilər'])
    expect(groups[1]!.rows).toHaveLength(11)
  })

  it('breaks ties by name so the order does not wander', () => {
    const groups = groupByArea([...at('Bərpa', 5), ...at('Anbar', 5)])

    expect(groups.map((g) => g.area)).toEqual(['Anbar', 'Bərpa'])
  })

  it('copes with an empty register', () => {
    expect(groupByArea([])).toEqual([])
  })
})
