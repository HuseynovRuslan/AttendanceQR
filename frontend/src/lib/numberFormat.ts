/**
 * Numbers as an Azerbaijani reader writes them: «1 250,50», «92,2%».
 *
 * Not `toLocaleString('az-AZ')`. Chrome ships without Azerbaijani number data and falls back to the
 * English form without a word — «1,250.50» — which an accountant here reads as one-point-something.
 * Node does have the data, so a unit test of toLocaleString passes while every phone shows the
 * English form. The separators are therefore placed by hand: a no-break space between thousands
 * (so «1 250» can never wrap across two lines of a table cell) and a comma as the decimal point.
 *
 * Display only. Excel files are built on the server and keep their own number format.
 */

const NBSP = ' '

/**
 * `decimals` digits after the comma. With `trim`, trailing zeros go (and the comma with them):
 * 160.0 → «160», 91.3 → «91,3» — for counts that are usually whole. Without it the width is fixed,
 * which is what money needs: «1 000,00».
 */
export function formatNumber(n: number, decimals = 0, { trim = false }: { trim?: boolean } = {}): string {
  if (!Number.isFinite(n)) return '—'
  const fixed = Math.abs(n).toFixed(decimals)
  const [int, rawFrac = ''] = fixed.split('.')
  const frac = trim ? rawFrac.replace(/0+$/, '') : rawFrac
  const grouped = int.replace(/\B(?=(\d{3})+(?!\d))/g, NBSP)
  // No «-0»: a value that rounds to zero is zero.
  const sign = n < 0 && /[1-9]/.test(fixed) ? '-' : ''
  return `${sign}${grouped}${frac ? `,${frac}` : ''}`
}

/** «92,2%», «100%». One decimal at most; a missing value is a dash, never «0%». */
export function formatPercent(v: number | null | undefined): string {
  if (v == null || !Number.isFinite(v)) return '—'
  return `${formatNumber(v, 1, { trim: true })}%`
}
