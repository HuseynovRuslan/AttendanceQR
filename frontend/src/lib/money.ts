/**
 * Parse a user-typed AZN amount to a non-negative number, or null when it is empty or ambiguous.
 *
 * Deliberately strict: digits with an OPTIONAL 1–2 digit decimal (either "." or "," as the point). This
 * rejects a fat-finger like "1.2.3" AND a thousands-formatted "1.250" (which a naive Number() would read
 * as 1.25 — a silent 1000× under-price). Callers must treat null as an error, never as "clear" or 0, so a
 * mistyped price surfaces instead of silently wiping or zeroing a bill.
 */
export function parseMoney(raw: string): number | null {
  const s = raw.trim().replace(',', '.')
  if (!s) return null
  if (!/^\d+(\.\d{1,2})?$/.test(s)) return null
  const n = Number(s)
  return isFinite(n) && n >= 0 ? n : null
}

/** Live input sanitiser: keep digits and a single decimal point (comma → point), so the field can never
 *  accumulate a second separator. Pair with parseMoney() on submit for the 1–2 decimal-place rule. */
export function moneyInputFilter(value: string): string {
  const cleaned = value.replace(',', '.').replace(/[^\d.]/g, '')
  const dot = cleaned.indexOf('.')
  if (dot === -1) return cleaned
  return cleaned.slice(0, dot + 1) + cleaned.slice(dot + 1).replace(/\./g, '')
}

/** "1250 ₼" — Azerbaijani-grouped, up to 2 decimals. */
export function formatMoney(n: number): string {
  return `${n.toLocaleString('az-AZ', { maximumFractionDigits: 2 })} ₼`
}
