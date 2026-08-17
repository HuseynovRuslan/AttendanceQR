/** One employee read out of the paste box or an uploaded file. */
export interface ParsedRow {
  fullName: string
  phoneNumber?: string
  position?: string
  fatherName?: string
  birthYear?: number
  /** "yyyy-MM-dd" — set when the birth field held a full date rather than a bare year. */
  birthDate?: string
  email?: string
  /** Per-row overrides, by name. Empty → the Rol / Filial selectors on the page. */
  roleName?: string
  locationName?: string
}

/**
 * Each line is one employee. Fields are separated by a comma, tab or semicolon, so a selection
 * pasted straight out of Excel (tab-separated) works. The paste order is:
 *
 *   Ad Soyad, Telefon, Vəzifə, Ata adı, Təvəllüd, Email, Rol, Filial
 *
 * (The .xlsx template shows its columns in a different, reader-friendly order — that file is read
 * by HEADER text on the server, so the two orders never need to agree.)
 *
 * The Təvəllüd slot takes either a bare year (1990) or a full date (15.03.1990) — the full date is
 * what the birthday features run on. Only the name is required; anything after it may be left out
 * or left empty.
 *
 * Two details are load-bearing, and both are pinned by tests:
 *
 *  - The first three fields keep their original order, and everything new is APPENDED. Putting
 *    "Ata adı" second — where the employee form shows it — would have read the phone number in every
 *    existing paste as a father's name, and the import would have looked like it worked.
 *
 *  - The split is on ONE separator, not a run of them. With /[,\t;]+/ the empty cells in
 *    "Əli, 0501234567, , , , ali@mail.az" collapsed, so the email landed in the position column and
 *    every field after a skipped one shifted left. Skipping a middle field is normal now.
 */
export function parseBulkText(text: string): ParsedRow[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => {
      const parts = line.split(/[,\t;]/).map((p) => p.trim())
      const year = Number(parts[4])
      const row: ParsedRow = { fullName: parts[0] ?? '' }
      if (parts[1]) row.phoneNumber = parts[1]
      if (parts[2]) row.position = parts[2]
      if (parts[3]) row.fatherName = parts[3]
      // "Ruslan Hüseynov Rasim oğlu" — the name field accepts the patronymic inline (that is how
      // every customer's own list is written) and gives it up to fatherName. Only on an explicit
      // oğlu/qızı suffix, and never over an Ata adı the line already provides.
      if (!row.fatherName) {
        const tokens = row.fullName.split(/\s+/).filter(Boolean)
        const tail = tokens[tokens.length - 1]?.toLowerCase()
        if (tokens.length >= 3 && ['oğlu', 'oglu', 'qızı', 'qizi'].includes(tail)) {
          row.fullName = tokens.slice(0, -2).join(' ')
          row.fatherName = tokens.slice(-2).join(' ')
        }
      }
      // The birth slot: a full date ("15.03.1990", "15/03/1990" or ISO) wins over a bare year, and
      // free text ("bilinmir") is neither — it must not become one.
      const dmy = parts[4]?.match(/^(\d{1,2})[./](\d{1,2})[./](\d{4})$/)
      const isoD = parts[4]?.match(/^\d{4}-\d{2}-\d{2}$/)
      if (dmy && Number(dmy[1]) >= 1 && Number(dmy[1]) <= 31 && Number(dmy[2]) >= 1 && Number(dmy[2]) <= 12) {
        row.birthDate = `${dmy[3]}-${dmy[2].padStart(2, '0')}-${dmy[1].padStart(2, '0')}`
        row.birthYear = Number(dmy[3])
      } else if (isoD) {
        row.birthDate = parts[4]
        row.birthYear = Number(parts[4].slice(0, 4))
      } else if (parts[4] && Number.isInteger(year) && year > 1900) row.birthYear = year
      if (parts[5]) row.email = parts[5]
      if (parts[6]) row.roleName = parts[6]
      if (parts[7]) row.locationName = parts[7]
      return row
    })
    .filter((r) => r.fullName.length > 0)
}
