import { IconEye, IconEyeOff } from './icons'

/* Show / hide PIN */
export function PinEyeButton({ visible, onToggle }: { visible: boolean; onToggle: () => void }) {
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-label={visible ? 'PIN-i gizlət' : 'PIN-i göstər'}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        flexShrink: 0,
        width: 44,
        height: 44,
        background: 'none',
        border: 'none',
        borderRadius: 10,
        cursor: 'pointer',
        padding: 0,
        color: '#52607A',
      }}
    >
      {visible ? (
        <IconEyeOff width={22} height={22} aria-hidden="true" />
      ) : (
        <IconEye width={22} height={22} aria-hidden="true" />
      )}
    </button>
  )
}
