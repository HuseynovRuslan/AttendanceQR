import { formatNumber } from './numberFormat'

/**
 * The server's answer when a value does not fit its database column (backend: FieldLimitFilter / FieldLimits):
 *
 *   400 { error: 'TextTooLong', field: 'description', max: 200 }
 *   400 { error: 'NumberTooLarge', field: 'amount', max: 99999999.99 }
 *
 * It can come from any save, not only the forms that check their own lengths, so a screen that maps its error
 * codes asks this first. Before it existed, a text one letter too long was an empty 500 and the screen said only
 * «Yadda saxlanmadı» — the admin could not know that shortening it was the whole fix. `field` and `max` are absent
 * when the database itself refused (Postgres does not name the column), and the sentence still says what to do.
 */
export interface LimitError {
  error: 'TextTooLong' | 'NumberTooLarge'
  field?: string
  max?: number
}

export function isLimitError(data: unknown): data is LimitError {
  if (!data || typeof data !== 'object' || !('error' in data)) return false
  const code = (data as { error: unknown }).error
  return code === 'TextTooLong' || code === 'NumberTooLarge'
}

/**
 * The sentence for a limit error, or null when `data` is not one. `labels` names the screen's fields — the server
 * sends the property in camelCase («fullName»), the screen knows it as «Soyadı, adı, atasının adı».
 */
export function limitErrorText(data: unknown, labels: Record<string, string> = {}): string | null {
  if (!isLimitError(data)) return null
  const label = data.field ? labels[data.field] : undefined
  const max = typeof data.max === 'number' ? data.max : null
  if (data.error === 'TextTooLong') {
    const what = label ? `«${label}»` : 'Mətn'
    return max !== null
      ? `${what} çox uzundur — ən çox ${max} hərf ola bilər.`
      : `${what} çox uzundur — qısaldıb yenidən yoxlayın.`
  }
  const what = label ? `«${label}»` : 'Rəqəm'
  return max !== null
    ? `${what} çox böyükdür — ən çox ${formatNumber(max, 2)} ola bilər.`
    : `${what} çox böyükdür.`
}
