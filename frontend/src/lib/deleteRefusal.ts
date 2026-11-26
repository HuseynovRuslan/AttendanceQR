// What a refused delete is told, for a person and for a branch. Kept out of the pages so the wording can be
// pinned by tests: a refusal that only said «Silinmədi» is what had admins retrying the same delete over
// and over, with nothing on the screen to say what held it.

/** The 409 the server sends when a person has history: how many rows of each kind. Role rows and
 *  PIN-reset requests are not history — they go with the person without a question. */
export interface EmployeeHistory {
  attendance?: number
  requests?: number
}

/** The 409 the server sends when something it has no rule for still names the person or branch. */
export interface Referenced {
  tables?: string[]
}

/** The 409 for a branch that cannot go: what holds it. */
export interface LocationInUse {
  employeeCount?: number
  historyCount?: number
  accessCount?: number
  approvalCount?: number
  otherTables?: string[]
}

function historyParts(d: EmployeeHistory): string[] {
  const parts: string[] = []
  if ((d.attendance ?? 0) > 0) parts.push(`${d.attendance} davamiyyət qeydi`)
  if ((d.requests ?? 0) > 0) parts.push(`${d.requests} telefon dəyişmə sorğusu`)
  return parts
}

/** The «wipe it all?» question. Names what goes, so a test account (3 scans) and a real person
 *  (a year of them) are not the same click. */
export function employeeHistoryPrompt(name: string, d: EmployeeHistory): string {
  const parts = historyParts(d)
  const what = parts.length > 0 ? `: ${parts.join(', ')}` : ''
  return `"${name}" işçisinin tarixçəsi var${what}. Hamısı daxil olmaqla TAM silinsin? Bu geri qaytarılmır.`
}

/** Something the delete has no rule for still names this person. Nothing was deleted. */
export function employeeReferencedText(d: Referenced): string {
  const tables = (d.tables ?? []).filter(Boolean)
  const which = tables.length > 0 ? ` (${tables.join(', ')})` : ''
  return `Bu işçiyə sistemdə başqa qeydlər bağlıdır${which} — heç nə silinmədi. ` +
    'Hesabı bağlamaq üçün işçini deaktiv edin.'
}

/** Why this branch will not delete, and what to do about it — the causes have different answers.
 *  Staff can be moved and the branch then deletes; history and records of decisions about the branch
 *  cannot, so that branch is deactivated instead. Saying only "it is in use" left an admin hunting for
 *  staff they could not see. */
export function locationInUseText(name: string, d: LocationInUse): string {
  const history = d.historyCount ?? 0
  if (history > 0) {
    return `«${name}» filialında ${history} davamiyyət qeydi var — silinsə tarixçə itərdi. ` +
      'Əvəzinə «Dayandır» ilə deaktiv edin: kiosk QR bağlanır, məlumat qalır.'
  }

  const records: string[] = []
  if ((d.accessCount ?? 0) > 0) records.push(`${d.accessCount} səlahiyyət təyinatı`)
  if ((d.approvalCount ?? 0) > 0) records.push(`${d.approvalCount} təsdiq sahəsi və ya sorğusu`)
  const other = (d.otherTables ?? []).filter(Boolean)
  if (other.length > 0) records.push(`başqa qeydlər (${other.join(', ')})`)
  if (records.length > 0) {
    return `«${name}» filialına bağlı qeydlər var: ${records.join(', ')} — silinsə onlar itərdi. ` +
      'Əvəzinə «Dayandır» ilə deaktiv edin: kiosk QR bağlanır, məlumat qalır.'
  }

  return `«${name}» filialında ${d.employeeCount ?? 0} işçi var. Əvvəlcə onları başqa filiala köçürün ` +
    '(İşçilər → redaktə → Filial), sonra silin. Özünüz də o filialda ola bilərsiniz.'
}
