/**
 * Reading what somebody TYPED into a number field — the other half of numberFormat.ts, which writes them.
 *
 * Why the fields that matter are `type="text"` and read here rather than `type="number"`: a number input
 * that holds something the browser cannot parse reports its value as "" — not an error, an empty string.
 * Safari, iOS, Firefox and an English-locale Chrome all read «850,5» and «1 200» that way, and those are
 * exactly how an Azerbaijani accountant writes 850.5 and 1200 (it is how this app itself prints them). A
 * form that trusts `.value` then sends «not set», says «yadda saxlandı», and the salary or the headcount
 * is gone without a word. A text field keeps what was typed, so a value can be refused out loud instead.
 */

/**
 * Drop the thousands spaces from a typed number: «1 200» → «1200», «12 500,50» → «12500,50». Any kind of
 * space counts — the no-break space numberFormat.ts prints is what a copied figure carries.
 *
 * Returns null when a space sits anywhere a thousands separator cannot: «850 5» is not how anyone writes
 * a number, and reading it as 8505 would be a silent tenfold error. Text with no space is returned
 * trimmed and otherwise untouched — whether it is a number at all is the caller's question.
 */
export function ungroup(raw: string): string | null {
  const s = raw.trim().replace(/\s+/g, ' ')
  if (!s.includes(' ')) return s
  return /^\d{1,3}( \d{3})+([.,]\d+)?$/.test(s) ? s.replace(/ /g, '') : null
}

/**
 * A whole, non-negative count — a headcount, a number of places — or null when the text is empty or is
 * not one. «12,5» and «12.5» are refused rather than rounded: a count with a fraction in it is a typo,
 * and quietly saving 12 hides it. Callers decide what empty means; null is never «0».
 */
export function parseCount(raw: string): number | null {
  const s = ungroup(raw)
  if (!s || !/^\d+$/.test(s)) return null
  const n = Number(s)
  return Number.isSafeInteger(n) ? n : null
}
