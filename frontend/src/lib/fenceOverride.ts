/**
 * The per-person GPS wall, between the form's select and the stored field.
 *
 * The field is `requireGeofenceOverride` — «is the wall required for this person»: true rejects a scan
 * outside the radius, false only measures it, null follows the branch (CheckInMode.IsFenced). The select
 * says the same in words: «on» = «Radiusdan kənarda girişi rədd et», «off» = «Rədd etmə — yalnız ölç».
 *
 * Until 2026-10-15 the form mapped these the other way round, in both directions: choosing «rədd et» stored
 * false and took the wall AWAY, and a stored true was shown as «yalnız ölç». A round trip changed nothing,
 * which is why it went unnoticed — but every choice an admin made meant its opposite.
 */
export type FenceSelect = '' | 'on' | 'off'

export function fenceSelectFrom(requireGeofenceOverride: boolean | null | undefined): FenceSelect {
  return requireGeofenceOverride == null ? '' : requireGeofenceOverride ? 'on' : 'off'
}

export function requireGeofenceFrom(select: FenceSelect): boolean | null {
  return select === '' ? null : select === 'on'
}
