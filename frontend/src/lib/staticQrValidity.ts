export const DEFAULT_STATIC_QR_VALIDITY_DAYS = 60
export const MIN_STATIC_QR_VALIDITY_DAYS = 1
export const MAX_STATIC_QR_VALIDITY_DAYS = 3650

export type StaticQrValidityMode = 'days' | 'permanent'

export type StaticQrValidityRequest =
  | { permanent: true }
  | { validityDays: number }

export function parseStaticQrValidity(
  mode: StaticQrValidityMode,
  rawDays: string,
): StaticQrValidityRequest | null {
  if (mode === 'permanent') return { permanent: true }

  const trimmed = rawDays.trim()
  if (!/^\d+$/.test(trimmed)) return null

  const validityDays = Number(trimmed)
  if (
    !Number.isSafeInteger(validityDays)
    || validityDays < MIN_STATIC_QR_VALIDITY_DAYS
    || validityDays > MAX_STATIC_QR_VALIDITY_DAYS
  ) {
    return null
  }

  return { validityDays }
}

export function staticQrValidityQuery(validity: StaticQrValidityRequest): string {
  const query = new URLSearchParams()
  if ('permanent' in validity) query.set('permanent', 'true')
  else query.set('validityDays', String(validity.validityDays))
  return query.toString()
}
