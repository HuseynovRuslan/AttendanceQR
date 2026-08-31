/**
 * Reads the equipment register's prose and says what kind of kit a line describes.
 *
 * The register is a spreadsheet maintained by hand, so what a person holds is written in words —
 * "1 ədəd masaüstü ofis kompüteri, 2 ədəd monitor HP 27\"" — and there are no inventory numbers to
 * key off. The card view needs something skimmable above that prose: a person's card shows chips,
 * and the full text is one click away.
 *
 * Everything here is DERIVED AT DISPLAY TIME and nothing is stored. That is the whole safety
 * argument: a wrong chip is a wrong hint on a card that shows the real text underneath it, whereas a
 * wrong number written into the database would quietly become the number somebody orders against.
 *
 * The rule that follows from that: **a count is shown only when the register actually wrote one.**
 * We never infer "2" from two lines of specification or from a plural. When no number is written the
 * chip says "Monitor", not "1 monitor" — the honest reading of "he has monitors, we don't know how
 * many" is the kind, without a number.
 */

export type KitKind = 'desktop' | 'laptop' | 'monitor' | 'printer' | 'scanner' | 'ups' | 'other'

export interface KitItem {
  kind: KitKind
  /** How many, when the register says so in words. Null means "present, count not written". */
  count: number | null
}

/** The columns a line's kit can be described in. */
export interface KitSource {
  equipment: string | null
  systemUnit: string | null
  monitor: string | null
  otherEquipment: string | null
}

export const KIT_LABEL: Record<KitKind, string> = {
  desktop: 'Sistem bloku',
  laptop: 'Noutbuk',
  monitor: 'Monitor',
  printer: 'Printer',
  scanner: 'Skaner',
  ups: 'UPS',
  other: 'Digər avadanlıq',
}

/** Display order — the big machine first, accessories after, "other" last. */
const ORDER: KitKind[] = ['desktop', 'laptop', 'monitor', 'printer', 'scanner', 'ups', 'other']

/**
 * Lowercase, Azerbaijani-safe.
 *
 * `'İ'.toLowerCase()` is NOT 'i' — it is 'i' followed by U+0307 COMBINING DOT ABOVE, which matches
 * none of the literals below. The backend's sheet importer carries the same guard for the same
 * reason (EquipmentSheet.NormalizeHeader); getting it wrong there lost a whole column silently, and
 * here it would lose every chip on a line that happens to start with İ.
 */
function az(text: string): string {
  return text.replace(/İ/g, 'i').replace(/I/g, 'ı').toLowerCase().replace(/̇/g, '')
}

/**
 * Which kind a fragment describes. Order matters: "noutbuk" is checked before the desktop words so
 * that a line reading "noutbuk kompüteri" is a laptop, not a desktop.
 */
const KINDS: [KitKind, string[]][] = [
  ['laptop', ['noutbuk', 'notbuk', 'notebook', 'laptop', 'ноутбук']],
  ['monitor', ['monitor', 'ekran', 'монитор']],
  ['printer', ['printer', 'çap qurğu', 'mfp', 'çoxfunksiyalı', 'принтер']],
  ['scanner', ['skaner', 'scanner', 'сканер']],
  ['ups', ['ups', 'nəfəsalma', 'qoruyucu blok', 'ибп']],
  ['desktop', ['masaüstü', 'sistem blok', 'sistem bloku', 'monoblok', 'stasionar', 'kompüter', 'komputer', 'компьютер']],
]

function kindOf(fragment: string): KitKind | null {
  const s = az(fragment)
  for (const [kind, words] of KINDS) if (words.some((w) => s.includes(az(w)))) return kind
  return null
}

/**
 * The count written in a fragment, or null.
 *
 * Only two shapes are accepted, both of which are somebody explicitly writing a quantity:
 * "2 ədəd monitor" and a bare leading "2 monitor". A number anywhere else in the text is a model or
 * a size — "monitor HP 27\"" is not 27 monitors, and "i5 11-ci nəsil" is not 11 machines — so an
 * unanchored digit search is exactly the wrong thing to do here.
 */
function countIn(fragment: string): number | null {
  const s = az(fragment).trim()

  // `\d+`, not `\d{1,3}` — a bounded run lets the engine start mid-number and read "2024 ədəd"
  // as 024. Take the whole run and let clamp judge it; that is the one place the limit belongs.
  const withUnit = s.match(/(\d+)\s*(?:ədəd|eded|dənə|dene|əd\.|ed\.|шт)/)
  if (withUnit) return clamp(withUnit[1]!)

  const leading = s.match(/^(\d+)\s+\D/)
  if (leading) return clamp(leading[1]!)

  return null
}

/** A register line describes one desk, not a warehouse; a three-digit count is a parse, not a fact. */
function clamp(digits: string): number | null {
  const n = Number(digits)
  return n >= 1 && n <= 99 ? n : null
}

/** Splits a cell into the separate things it lists. */
function fragments(text: string | null): string[] {
  if (!text) return []
  return text
    .split(/[\n;,]+/)
    .map((f) => f.trim())
    .filter(Boolean)
}

/**
 * What this register line holds, as chips.
 *
 * Counts come only from the prose columns ("Avadanlıq", "Digər avadanlıq") where quantities are
 * actually written. The dedicated "Sistem bloku" and "Monitor" columns establish PRESENCE only:
 * their content is a specification, and the entity comment's "one line per machine" is a convention
 * nobody is bound by, so counting their lines would invent a number on the day someone wraps a spec.
 */
export function readKit(row: KitSource): KitItem[] {
  const counts = new Map<KitKind, number | null>()

  const add = (kind: KitKind, count: number | null) => {
    if (!counts.has(kind)) {
      counts.set(kind, count)
      return
    }
    const existing = counts.get(kind)!
    // Two fragments of the same kind: sum what is written, and let a written number win over a
    // fragment that carried none rather than dragging the pair back to "unknown".
    counts.set(kind, existing === null && count === null ? null : (existing ?? 0) + (count ?? 0))
  }

  for (const source of [row.equipment, row.otherEquipment]) {
    for (const fragment of fragments(source)) {
      const kind = kindOf(fragment)
      if (kind) add(kind, countIn(fragment))
    }
  }

  // Presence from the spec columns. `add` leaves an already-counted kind alone when it passes null.
  if (row.systemUnit?.trim()) add('desktop', null)
  if (row.monitor?.trim()) add('monitor', null)

  // A line that lists something none of the keywords know still has to show as holding something —
  // an empty card would read as "nothing issued", which is a different fact entirely.
  if (counts.size === 0 && fragments(row.equipment).concat(fragments(row.otherEquipment)).length > 0)
    add('other', null)

  return ORDER.filter((k) => counts.has(k)).map((kind) => ({ kind, count: counts.get(kind)! }))
}

/**
 * The chip's text: the kind, always, plus "×N" only where N is more than one.
 *
 * It used to be "2 monitor" when a number was written and "Monitor" when it was not, which put two
 * differently-shaped strings side by side on the same card — one starting with a digit, one with a
 * capital — and read as carelessness rather than as the distinction it is.
 *
 * Always leading with the kind fixes the shape without inventing anything. "Monitor (1)" would have
 * been the tidier-looking answer and is the one thing this module must not do: where the register
 * never wrote a number, the 1 would be ours. A written 1 and an unwritten count both render as plain
 * "Monitor" — they mean the same thing to a reader, and the register's own words are one click away.
 */
export function kitLabel(item: KitItem): string {
  const name = KIT_LABEL[item.kind]
  return item.count !== null && item.count > 1 ? `${name} ×${item.count}` : name
}

/** Does this line hold anything of that kind? Backs the filter chips. */
export function hasKind(row: KitSource, kind: KitKind): boolean {
  return readKit(row).some((i) => i.kind === kind)
}

/** What the whole register holds, per kind — the headline band. */
export interface KitTotals {
  kind: KitKind
  /** How many register lines mention this kind. Exact: it is a count of rows, not of things. */
  people: number
  /**
   * How many actual devices, AS A LOWER BOUND.
   *
   * A line that writes "2 ədəd monitor" contributes 2; a line that just says "monitor" contributes 1,
   * because a mention without a number is still at least one device. So this is a floor, never an
   * estimate — the register may describe more, it cannot describe fewer. The screen says "ən azı"
   * next to it, and that wording is the reason this is allowed to exist at all: the same rule that
   * forbids inventing "1 monitor" on a card permits summing minimums, as long as the sum is labelled
   * as the minimum it is.
   */
  devices: number
}

export function countKit(rows: KitSource[]): KitTotals[] {
  const acc = new Map<KitKind, { people: number; devices: number }>()

  for (const row of rows)
    for (const item of readKit(row)) {
      const t = acc.get(item.kind) ?? { people: 0, devices: 0 }
      t.people += 1
      t.devices += item.count ?? 1
      acc.set(item.kind, t)
    }

  return ORDER.filter((k) => acc.has(k)).map((kind) => ({ kind, ...acc.get(kind)! }))
}

// --- Gathering the register by site ---------------------------------------------------------------

export interface AreaGroup<T> {
  area: string
  rows: T[]
  /** True for the "Digər ərazilər" bucket — those cards still have to name their own site. */
  merged: boolean
}

/** Sites smaller than this are gathered together rather than each getting a heading of its own. */
const SMALL_AREA = 3

/** Enough small sites to be worth gathering. Folding one two-person site into "Digər ərazilər" only
 *  renames it, and loses a real heading to gain nothing. */
const MIN_TO_MERGE = 3

const NO_AREA = 'Ərazi yazılmayıb'
const OTHER_AREAS = 'Digər ərazilər'

/**
 * The register's rows, gathered under the site they belong to.
 *
 * Sorted by SIZE, not by name. The alphabet is meaningless here and actively unhelpful: at 80 people
 * across 23 sites the first heading on the page would be whichever one-person site happens to start
 * with an A, and the main office would be somewhere in the middle. Biggest first is the order anyone
 * reading for an overview wants, and the long tail belongs at the bottom.
 *
 * And there IS a long tail — 23 sites for 80 people is three and a half people each. Left alone that
 * produces 23 headings over two cards apiece, which is not structure, it is confetti. Sites below
 * SMALL_AREA are gathered into one "Digər ərazilər" group at the end, and only when there are enough
 * of them for the gathering to be worth anything.
 *
 * Ties break alphabetically so the order is stable between renders and between people's screens.
 */
export function groupByArea<T extends { area: string | null }>(rows: T[]): AreaGroup<T>[] {
  const by = new Map<string, T[]>()
  for (const r of rows) {
    const key = r.area?.trim() || NO_AREA
    const list = by.get(key)
    if (list) list.push(r)
    else by.set(key, [r])
  }

  const all = [...by.entries()].map(([area, rows]) => ({ area, rows, merged: false }))
  const bySize = (a: AreaGroup<T>, b: AreaGroup<T>) =>
    b.rows.length - a.rows.length || a.area.localeCompare(b.area, 'az')

  // "Ərazi yazılmayıb" is not a place, so it joins the tail however big it is.
  const small = all.filter((g) => g.rows.length < SMALL_AREA || g.area === NO_AREA)
  if (small.length < MIN_TO_MERGE) return all.sort(bySize)

  const big = all.filter((g) => !small.includes(g)).sort(bySize)
  const tail = small.sort(bySize).flatMap((g) => g.rows)
  return [...big, { area: OTHER_AREAS, rows: tail, merged: true }]
}
