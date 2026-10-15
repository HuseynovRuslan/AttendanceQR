/**
 * What a list's empty row says when the list never came.
 *
 * Several screens read a failed request as an empty answer and drew «Qeyd yoxdur» — a confident zero, on
 * registers that decide pay. A refusal or a failure a person can read is one somebody reports; an empty
 * list is one nobody questions (docs/strukturlar-ve-selahiyyetler.md §5, «403 ≠ boş»).
 */

/** A refusal, as opposed to «nothing here», a dropped connection or a session that ended (401 — that one
 *  is the login screen's question, not a permission). */
export const isRefused = (status: number) => status === 403

export function listFailureText(status: number): string {
  return isRefused(status)
    ? 'Bu siyahıya icazəniz yoxdur — lazımdırsa, şirkətin adminindən xahiş edin.'
    : 'Siyahı yüklənmədi — səhifəni yeniləyin.'
}
