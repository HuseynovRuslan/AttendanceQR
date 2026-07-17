/** One employee read out of the paste box or an uploaded file. */
export interface ParsedRow {
  fullName: string
  phoneNumber?: string
  position?: string
  fatherName?: string
  birthYear?: number
  email?: string
  /** Per-row overrides, by name. Empty → the Rol / Filial selectors on the page. */
  roleName?: string
  locationName?: string
}

/**
 * Each line is one employee. Fields are separated by a comma, tab or semicolon, so a selection
 * pasted straight out of Excel (tab-separated) works. The order matches the .xlsx template:
 *
 *   Ad Soyad, Telefon, Vəzifə, Ata adı, Təvəllüd ili, Email, Rol, Filial
 *
 * Only the name is required; anything after it may be left out or left empty.
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
      // Free text: "bilinmir" is not a year, and must not become one.
      if (parts[4] && Number.isInteger(year) && year > 1900) row.birthYear = year
      if (parts[5]) row.email = parts[5]
      if (parts[6]) row.roleName = parts[6]
      if (parts[7]) row.locationName = parts[7]
      return row
    })
    .filter((r) => r.fullName.length > 0)
}
