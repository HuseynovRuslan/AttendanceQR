/**
 * What an employee is told when the poster refuses a phone their account does not know.
 *
 * The server answers DeviceMismatch — NoDeviceBound when the account has no phone at all — and, since
 * 03.10.2026, says why in `cause` beside it. Before that everyone read «Bu cihaz hesabınıza bağlı
 * deyil» whatever had happened. Eleven people at Bakı Abadlıq kept tapping at that card, one of them
 * 56 times, while the real story was that their phone's browser kept losing the app's storage: each
 * time it came back as a new phone, and the allowance for new phones had run out. The card could not
 * say so, so nobody told them to open the app the same way every time.
 *
 * `error` keeps the old code on purpose. An installed app can run an older bundle for days and that
 * copy matches on it; a cause this copy has never heard of falls back to the plain card for the code.
 */

/** The words on the card. ScanPage adds the colour and marks it as nothing left to retry. */
export interface DeviceRefusalCopy {
  title: string
  detail?: string
  note?: string
  /** Offer «Bu mənim yeni telefonumdur» — an admin approving that request is the way back in. */
  showDeviceChangeLink: boolean
}

export function deviceRefusalCopy(
  error: 'DeviceMismatch' | 'NoDeviceBound',
  cause: string | null | undefined,
): DeviceRefusalCopy {
  if (cause === 'DeviceBindLimit') {
    // The poster has stopped adopting new phones for this person. An approval reopens it; the lasting
    // fix is in their own hands, so the card says what it is.
    return {
      title: 'Telefonunuz tətbiqi yadda saxlamır',
      detail: 'Bu telefon son vaxtlar bir neçə dəfə yeni cihaz kimi göründü.',
      note:
        '«Bu mənim yeni telefonumdur» düyməsini basın — admin təsdiqləyəndən sonra skan edə biləcəksiniz. '
        + 'Bundan sonra tətbiqi həmişə ana ekrandakı ikondan açın, gizli rejimdə açmayın.',
      showDeviceChangeLink: true,
    }
  }
  if (cause === 'DeviceRevoked') {
    return {
      title: 'Bu telefon hesabınızdan çıxarılıb',
      detail: 'Admin bu telefonu hesabınızdan ayırıb.',
      note: 'Telefon sizinkidirsə, «Bu mənim yeni telefonumdur» düyməsini basın. Təkrar skan kömək etməyəcək.',
      showDeviceChangeLink: true,
    }
  }
  return error === 'NoDeviceBound'
    ? { title: 'Cihaz hesabınıza bağlı deyil', detail: 'Admin ilə əlaqə saxlayın.', showDeviceChangeLink: false }
    : { title: 'Bu cihaz hesabınıza bağlı deyil', note: 'Yenidən skan etmək kömək etməyəcək.', showDeviceChangeLink: true }
}
