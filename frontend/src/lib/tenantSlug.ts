/**
 * The hostname label a company is stored under.
 *
 * Nobody types this and nobody is shown it. It used to be the first thing the new-company form asked
 * for — a decision with no information behind it, made before the company even had a name — and the
 * operator's objection was the right one: staff sign in at app.qrlog.az, which finds the company from
 * the phone number, so a per-company address is not how anyone gets in.
 *
 * Being invisible is exactly why the rules below are not cosmetic. There is no field to correct a
 * rejected label in, so what comes out of here has to be something the server accepts on the first
 * try: 2–20 characters, starting with a letter or digit (SuperAdminController's SlugFormat).
 */

/** Azerbaijani letters have no place in a hostname; every one of them folds to its ASCII neighbour. */
const FOLD: Record<string, string> = { ə: 'e', ğ: 'g', ı: 'i', ö: 'o', ş: 's', ç: 'c', ü: 'u' }

/** The server's own limit, repeated here because this is the last chance to honour it. */
const MAX = 20

/** Nonspacing marks left behind by NFD decomposition — the dot above a decomposed İ, and its kin. */
const MARKS = /\p{Mn}/gu

/**
 * A company name as a label, or an empty string when the name leaves nothing usable.
 *
 * Empty is a real answer, not a failure: a name written entirely in characters that fold away (or one
 * single letter) cannot produce a valid label, and the caller substitutes a generic one. A form the
 * operator cannot fix must never be the outcome.
 */
export function slugify(name: string): string {
  const folded = (name ?? '')
    .toLowerCase()
    // Azerbaijani "İ" lowercases to "i" PLUS a combining dot (U+0307), which is not [a-z0-9] and so
    // became a dash: "İpək Yolu" came out as "i-pek-yolu". Decomposing and dropping the marks first
    // is the only way to keep the letter and lose the accent — and İ starts a great many company
    // names here.
    .normalize('NFD')
    .replace(MARKS, '')
    .replace(/[əğıöşçü]/g, (c) => FOLD[c] ?? c)
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')

  if (folded.length <= MAX) return folded.length >= 2 ? folded : ''

  // Too long. Cutting at exactly twenty characters lands mid-word — "beynelxalq-abadliq-v" — and this
  // label does surface in audit rows, so it steps back to the last whole word when one survives.
  const hard = folded.slice(0, MAX).replace(/-+$/, '')
  // lastIndexOf gives -1 for a label with no dash at all, and slice(0, -1) then eats a character —
  // "azerbaycanbeynelxalq" came back one letter short of the limit it was allowed.
  const lastDash = hard.lastIndexOf('-')
  const atWord = lastDash > 0 ? hard.slice(0, lastDash) : ''
  const label = atWord.length >= 2 ? atWord : hard
  return label.length >= 2 ? label : ''
}

/**
 * The same label with a counter, for the second company called the same thing.
 *
 * The counter is what makes the label safe to hide. A clash cannot be handed back to the operator to
 * resolve in a field that is no longer on screen, so the form takes the next free one by itself —
 * which also covers a name that folds onto a label the platform has reserved ("app", "admin").
 */
export function withSuffix(base: string, n: number): string {
  const tail = `-${n}`
  return base.slice(0, MAX - tail.length).replace(/-+$/, '') + tail
}
