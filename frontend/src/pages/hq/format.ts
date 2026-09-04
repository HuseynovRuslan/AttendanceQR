import { COMPANY_TZ } from '../../lib/format'

/** Shared by the board and its drawer — one number/time format, so the two never disagree. */
export const fmt = new Intl.NumberFormat('az-AZ')

export function timeOf(iso: string): string {
  return new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit', timeZone: COMPANY_TZ })
}
