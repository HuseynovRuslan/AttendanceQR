import { useNavigate } from 'react-router-dom'
import { IconArrowLeft } from './icons'

/**
 * Header for employee sub-pages reached from the Menu (profile, device-change) that live OUTSIDE the
 * bottom-tab shell. A single back button — no logout. The old EmployeeNav put a "Çıxış" here and
 * employees kept tapping it by mistake; logout now lives only at the bottom of the Menu, on purpose.
 */
export function SubPageHeader({ title, back = '/menu' }: { title: string; back?: string }) {
  const navigate = useNavigate()
  return (
    <header className="sticky top-0 z-10 flex items-center gap-2 border-b border-slate-100 bg-white/90 px-2 py-2 backdrop-blur">
      <button
        type="button"
        onClick={() => navigate(back)}
        aria-label="Geri"
        className="flex h-10 w-10 items-center justify-center rounded-full text-slate-600 transition active:bg-slate-100"
      >
        <IconArrowLeft className="h-6 w-6" />
      </button>
      <h1 className="text-lg font-bold text-slate-900">{title}</h1>
    </header>
  )
}
