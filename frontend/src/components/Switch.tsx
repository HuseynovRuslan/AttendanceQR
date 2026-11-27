import type { ReactNode } from 'react'

/**
 * A settings row with an on/off switch. The whole row is the control (≥ 56 px tall, far above the 44 px minimum), the
 * label and description name it for a screen reader (role="switch" + aria-checked), and the state is shown by the
 * knob's position AND the track's fill — never by colour alone.
 */
export function SwitchRow({
  checked,
  onChange,
  label,
  description,
  icon,
  disabled = false,
  id,
}: {
  checked: boolean
  onChange: (next: boolean) => void
  label: string
  description?: string
  icon?: ReactNode
  disabled?: boolean
  id: string
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-labelledby={`${id}-label`}
      aria-describedby={description ? `${id}-desc` : undefined}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className="flex min-h-14 w-full items-center gap-3 px-4 py-3 text-left transition-colors active:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-blue-600"
    >
      {icon}
      <span className="min-w-0 flex-1">
        <span id={`${id}-label`} className="block font-semibold text-slate-900">{label}</span>
        {description && (
          <span id={`${id}-desc`} className="mt-0.5 block text-sm leading-snug text-slate-600">{description}</span>
        )}
      </span>
      <span
        aria-hidden="true"
        className={`relative inline-flex h-7 w-12 shrink-0 items-center rounded-full transition-colors duration-200 motion-reduce:transition-none ${
          checked ? 'bg-blue-600' : 'bg-slate-300'
        }`}
      >
        <span
          className={`inline-block h-6 w-6 rounded-full bg-white shadow-sm ring-1 ring-black/5 transition-transform duration-200 motion-reduce:transition-none ${
            checked ? 'translate-x-[22px]' : 'translate-x-0.5'
          }`}
        />
      </span>
    </button>
  )
}
