import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { initials } from '../lib/att'
import { clearProfiles, listProfiles, removeProfile } from '../lib/profiles'
import { SubPageHeader } from '../components/SubPageHeader'
import { IconCheck, IconLogout, IconX } from '../components/icons'

/**
 * Managing the accounts saved on one handset — the crew phone.
 *
 * Switching between them does NOT happen here; that lives in the sheet behind the identity card,
 * because it is done thirty times in the ten minutes before a shift and cannot cost a screen each
 * time. What is left is the slow, deliberate half: taking somebody off the phone. It is kept apart
 * on purpose — a delete control sitting beside a switch control, tapped in a hurry by somebody
 * holding a phone for a queue of people, is how the wrong one gets hit.
 */
export function ProfilesPage() {
  const { employeeId, switchProfile } = useAuth()
  const [profiles, setProfiles] = useState(() => listProfiles())
  const navigate = useNavigate()

  return (
    <div className="min-h-screen bg-slate-50">
      <SubPageHeader title="Telefondakı hesablar" />

      <div className="flex flex-col gap-4 p-4">
        <p className="text-sm text-slate-500">
          Bu telefonda saxlanmış hesablar. Silmək heç nəyi itirmir — işçinin hesabı, qeydləri və cihaz
          bağlaması yerində qalır, nömrə və PIN ilə yenidən əlavə etmək olar.
        </p>

        <div className="divide-y divide-slate-100 overflow-hidden rounded-3xl border border-slate-100 bg-white shadow-sm">
          {profiles.map((p) => {
            const active = p.employeeId === employeeId
            return (
              <div key={p.employeeId} className="flex items-center gap-3 p-3">
                <button
                  type="button"
                  onClick={() => !active && switchProfile(p.employeeId)}
                  className="flex min-w-0 flex-1 items-center gap-3 text-left"
                >
                  <span
                    className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-full text-sm font-bold ${
                      active ? 'bg-blue-600 text-white' : 'bg-slate-100 text-slate-600'
                    }`}
                  >
                    {initials(p.name)}
                  </span>
                  <span className="min-w-0">
                    <span className="block truncate font-semibold text-slate-900">{p.name}</span>
                    <span className="block text-xs text-slate-400">
                      {active ? 'Bu hesabdasınız' : 'Keçmək üçün toxunun'}
                    </span>
                  </span>
                </button>

                {active ? (
                  <span className="flex h-8 w-8 items-center justify-center rounded-full bg-blue-50 text-blue-600">
                    <IconCheck className="h-5 w-5" />
                  </span>
                ) : (
                  <button
                    type="button"
                    aria-label={`${p.name} hesabını sil`}
                    onClick={() => {
                      // No confirm dialog: nothing on the server is touched, and re-adding costs
                      // twenty seconds. A modal here would be ceremony over a cheap mistake.
                      removeProfile(p.employeeId)
                      setProfiles(listProfiles())
                    }}
                    className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-slate-300 transition active:bg-slate-100"
                  >
                    <IconX className="h-5 w-5" />
                  </button>
                )}
              </div>
            )
          })}

          {profiles.length === 0 && (
            <div className="p-4 text-sm text-slate-400">Hələ saxlanmış hesab yoxdur.</div>
          )}
        </div>

        {profiles.length > 1 && (
          <button
            type="button"
            onClick={() => {
              // This one DOES ask. Clearing the list on a crew phone means every worker signs in by
              // PIN again, one at a time, and the PINs are the thing the holder does not have.
              if (!window.confirm('Bütün saxlanmış hesablar silinsin? Hər kəs yenidən PIN ilə daxil olmalı olacaq.')) return
              clearProfiles()
              navigate('/menu')
            }}
            className="flex items-center gap-3 rounded-3xl border border-red-100 bg-white p-4 font-semibold text-red-600 shadow-sm transition active:scale-[0.99]"
          >
            <span className="flex h-9 w-9 items-center justify-center rounded-full bg-red-50">
              <IconLogout className="h-5 w-5" />
            </span>
            Bütün hesabları sil
          </button>
        )}
      </div>
    </div>
  )
}
