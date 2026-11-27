import { IconAlarmClock, IconBell, IconLogIn, IconLogout } from './icons'
import { lookOf, TONE_COLORS } from '../lib/notificationKinds'

/**
 * The type's icon in its tinted circle — green for a check-in, orange for a check-out, amber for a late arrival. The
 * same mark on the settings page and in the center, so a type is recognised at a glance in both. Decorative beside
 * its visible text (aria-hidden); pass `label` where it stands alone.
 */
export function NotificationKindIcon({ typeKey, size = 36, label }: { typeKey: string; size?: number; label?: string }) {
  const look = lookOf(typeKey)
  const colors = TONE_COLORS[look.tone]
  const Icon = look.tone === 'in' ? IconLogIn : look.tone === 'out' ? IconLogout : look.tone === 'late' ? IconAlarmClock : IconBell
  const glyph = Math.round(size * 0.5)
  return (
    <span
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      style={{
        width: size,
        height: size,
        borderRadius: 999,
        background: colors.bg,
        color: colors.fg,
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        flex: 'none',
      }}
    >
      <Icon width={glyph} height={glyph} />
    </span>
  )
}
