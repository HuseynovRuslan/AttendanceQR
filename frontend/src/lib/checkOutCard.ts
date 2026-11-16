/**
 * What the phone says after a check-out.
 *
 * A fixed shift's day ends with its check-out, so «Sabaha qədər!» is right there. A flexible one («Elastik
 * qrafik») does not: the person may start another session in ten minutes, and a card that sends them home
 * until tomorrow tells them the opposite of the rule. Theirs is neutral, names the session rather than the
 * day, and says how to start the next one. The fixed card is unchanged, word for word.
 */
export interface CheckOutCardText {
  title: string
  detail: string
  note: string
}

/** «3 saat 5 dəqiqə işlədiniz». */
export function formatDuration(minutes: number): string {
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return `${h} saat ${m} dəqiqə işlədiniz`
}

/**
 * @param minutes the closed session's length, when the phone could read it back; else the clock time is shown
 * @param at the check-out time, «HH:mm»
 */
export function checkOutCardText(o: {
  flexible: boolean
  qrless: boolean
  minutes?: number
  at: string
}): CheckOutCardText {
  if (!o.flexible) {
    return {
      title: 'Çıxış qeydə alındı',
      detail: o.minutes !== undefined ? formatDuration(o.minutes) : `Saat ${o.at}`,
      note: 'Sabaha qədər!',
    }
  }
  return {
    title: 'Çıxışınız qeydə alındı',
    detail: o.minutes !== undefined ? `Bu sessiyada ${formatDuration(o.minutes)}` : `Saat ${o.at}`,
    note: o.qrless
      ? 'Yenidən işə başlasanız, «Giriş et»ə toxunun.'
      : 'Yenidən işə başlasanız, giriş üçün yenidən skan edin.',
  }
}
