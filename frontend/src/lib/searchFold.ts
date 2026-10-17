/**
 * The search rule, the same as the server's (src/AttendanceQR.Domain/SearchText.cs) — so a name the list's own
 * filter finds is a name the server's search finds, and the other way round.
 *
 * Neither `toLowerCase()` nor `toLocaleLowerCase('az')` is it. The first turns «İ» into 'i' plus a COMBINING
 * DOT ABOVE, so «ismayılov» typed in lower case does not occur in «İsmayılov» lowered. The second pairs «I»
 * with 'ı' — right for Azerbaijani, but people type names on keyboards that have no «ı» or «İ» («Ismayilov»),
 * and a search that misses a colleague over a dot is worse than one that also shows a namesake. So: the
 * Azerbaijani capitals are folded by hand, and «İ», «I», «ı», «i» all read as 'i'.
 */
const FROM = 'İIıƏŞÇĞÖÜ̇'
const TO = 'iiiəşçğöü'

export function searchFold(text: string | null | undefined): string {
  if (!text) return ''
  let out = ''
  for (const c of text) {
    const k = FROM.indexOf(c)
    if (k < 0) out += c.toLowerCase()
    else if (k < TO.length) out += TO[k]
  }
  return out
}

/** Does any of `fields` contain what was typed? An empty search matches everything. */
export function matchesSearch(typed: string, ...fields: (string | null | undefined)[]): boolean {
  const needle = searchFold(typed.trim())
  return !needle || fields.some((f) => searchFold(f).includes(needle))
}
