import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { appLogin, login } from '../api/auth'
import { getMyAvatar, getMyProfile } from '../api/attendance'
import { useAuth } from '../auth/AuthContext'
import { isAppMode } from '../lib/host'
import { decodeJwt } from '../lib/jwt'
import { putAvatar } from '../lib/avatar'
import { listProfiles, MAX_PROFILES, saveProfile } from '../lib/profiles'
import { Avatar } from './Avatar'
import { PinInput } from './PinInput'
import { IconCheck, IconUser, IconX } from './icons'

/**
 * The account switcher, as a sheet that rises over whatever is on screen.
 *
 * A separate settings page was the first shape this took, and it was the wrong one: on a crew phone
 * the switch happens THIRTY TIMES in the ten minutes before a shift, with a queue of people waiting
 * at the poster, and every extra screen in that loop is paid for thirty times over. Instagram solves
 * the same problem by hanging the whole thing off the name at the top of the profile — one tap to
 * open, one to switch — and there is no reason to invent a different answer here.
 *
 * Adding an account happens inside the sheet too, for the same reason. The credentials go to the SAME
 * login endpoint as the front door; nothing here is a way into an account that the login screen would
 * not also give.
 */
export function AccountSwitcherSheet({ onClose }: { onClose: () => void }) {
  const { employeeId, switchProfile } = useAuth()
  const [profiles, setProfiles] = useState(() => listProfiles())
  const [adding, setAdding] = useState(false)

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-slate-900/50 backdrop-blur-sm" onClick={onClose} />
      <div className="absolute inset-x-0 bottom-0 max-h-[92vh] overflow-y-auto rounded-t-3xl bg-white p-5 pb-[max(2rem,env(safe-area-inset-bottom))] shadow-2xl">
        {/* The grab handle is not decoration — it is what tells somebody who has never seen this that
            the panel came from the bottom edge and goes back there. */}
        <div className="mx-auto mb-4 h-1 w-10 rounded-full bg-slate-200" />

        {adding ? (
          <AddAccountForm
            onCancel={() => setAdding(false)}
            onAdded={(id) => {
              setProfiles(listProfiles())
              switchProfile(id)
            }}
          />
        ) : (
          <>
            <div className="mb-3 font-bold text-slate-900">Hesablar</div>

            <div className="flex flex-col">
              {profiles.map((p) => {
                const active = p.employeeId === employeeId
                return (
                  <button
                    key={p.employeeId}
                    type="button"
                    /* Switching to yourself would reload the app for no reason, and a reload that
                       changes nothing reads as a crash to the person holding the phone. */
                    onClick={() => (active ? onClose() : switchProfile(p.employeeId))}
                    className="flex items-center gap-3 rounded-2xl p-3 text-left transition active:bg-slate-50"
                  >
                    <Avatar
                      employeeId={p.employeeId}
                      name={p.name}
                      size={44}
                      className={active ? 'ring-2 ring-blue-600 ring-offset-2' : ''}
                    />
                    <span className="min-w-0 flex-1 truncate font-semibold text-slate-900">{p.name}</span>
                    {active && <IconCheck className="h-5 w-5 shrink-0 text-blue-600" />}
                  </button>
                )
              })}
            </div>

            <button
              type="button"
              disabled={profiles.length >= MAX_PROFILES}
              onClick={() => setAdding(true)}
              className="mt-1 flex w-full items-center gap-3 rounded-2xl p-3 text-left font-semibold text-blue-700 transition active:bg-slate-50 disabled:text-slate-400"
            >
              <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-blue-50">
                <IconUser className="h-5 w-5" />
              </span>
              {profiles.length >= MAX_PROFILES ? `Limit doldu (${MAX_PROFILES})` : 'Hesab əlavə et'}
            </button>

            {/* Removing an account is deliberately NOT a row here. This list is tapped in a hurry by
                somebody holding a phone for a queue of people, and a delete control beside a switch
                control is how the wrong one gets hit. It lives on its own screen. */}
            {profiles.length > 1 && (
              <Link
                to="/profiles"
                onClick={onClose}
                className="mt-2 block py-2 text-center text-sm font-semibold text-slate-500"
              >
                Hesabları idarə et
              </Link>
            )}

            <button onClick={onClose} className="mt-1 w-full py-2 text-sm text-slate-400">
              Bağla
            </button>
          </>
        )}
      </div>
    </div>
  )
}

function AddAccountForm({ onAdded, onCancel }: { onAdded: (employeeId: string) => void; onCancel: () => void }) {
  const [identifier, setIdentifier] = useState('')
  const [pin, setPin] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const { status, data } = await (isAppMode() ? appLogin : login)(identifier, pin)
      if (status !== 200 || !data || !('token' in data)) {
        if (status === 429) {
          const m = data && 'minutes' in data && data.minutes ? data.minutes : 5
          setError(`Çox sayda cəhd — ${m} dəqiqə sonra yenidən cəhd edin`)
        } else {
          setError('Nömrə və ya PIN yanlışdır')
        }
        return
      }

      const token = data.token
      const sub = decodeJwt(token)?.sub
      if (!sub) {
        setError('Giriş alınmadı — yenidən cəhd edin')
        return
      }

      // The name for the row, asked with the NEW token explicitly: the active session still belongs
      // to whoever is signed in, so an ordinary call would answer about THEM and label this row with
      // the wrong person's name. Re-entering somebody already listed is not an error — it is how a
      // holder refreshes a token that stopped working after a PIN change, and saveProfile replaces
      // the dead entry in place.
      const { status: st, data: me } = await getMyProfile(token)
      const name = st === 200 && me && 'fullName' in me ? me.fullName : 'İşçi'
      saveProfile({ employeeId: sub, name, token, addedAtMs: Date.now() })

      // Their face, fetched ONCE — here, while there is still a connection, and never again. Thirty
      // accounts on a crew phone are thirty pairs of initials otherwise, and in these names they
      // collide: Məmmədov Elçin and Məmmədov Elvin are both ME, and the wrong tap files the wrong
      // person's day. Awaited rather than fired off, so the row that appears already has the face on
      // it; a failure is not an error worth stopping for, since initials still work.
      if (st === 200 && me && 'hasAvatar' in me && me.hasAvatar) {
        const a = await getMyAvatar(token)
        if (a.status === 200 && a.data && 'dataUrl' in a.data)
          putAvatar(sub, a.data.dataUrl, me.avatarUpdatedAtUtc)
      }

      onAdded(sub)
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setLoading(false)
    }
  }

  return (
    <form onSubmit={onSubmit} className="flex flex-col gap-3">
      <div className="font-bold text-slate-900">Hesab əlavə et</div>
      <p className="-mt-1 text-sm text-slate-500">
        İşçinin nömrəsi və PIN-i ilə. Hesab bu telefonda saxlanılır, sonra bir toxunuşla keçmək olur.
      </p>

      {error && (
        <div className="fb fb-err">
          <IconX />
          <span>{error}</span>
        </div>
      )}

      <div>
        <label className="form-label">Telefon nömrəsi</label>
        <input
          className="inp"
          type="tel"
          inputMode="tel"
          placeholder="0XX XXX XX XX"
          required
          autoFocus
          value={identifier}
          onChange={(e) => setIdentifier(e.target.value)}
        />
      </div>

      <div>
        <label className="form-label">PIN (4 rəqəm)</label>
        <PinInput value={pin} onChange={setPin} />
      </div>

      <div className="flex gap-2">
        <button type="button" onClick={onCancel} className="btn btn-bl flex-1">
          Geri
        </button>
        <button type="submit" disabled={loading} className="btn btn-primary btn-bl flex-1">
          {loading ? 'Yoxlanılır…' : 'Əlavə et'}
        </button>
      </div>
    </form>
  )
}
