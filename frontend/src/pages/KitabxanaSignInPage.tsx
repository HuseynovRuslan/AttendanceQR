import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { apiRequest } from '../api/client'
import { SubPageHeader } from '../components/SubPageHeader'
import { activeProfileId, listProfiles } from '../lib/profiles'
import { NETWORK_MESSAGE, classifySignIn, readCode, readReturnUrl } from './kitabxanaOutcome'

const KITABXANA_URL = 'https://book.qrlog.az'

/** Long enough to read "Təsdiqləndi", short enough that nobody wonders whether it worked. */
const LEAVE_AFTER_MS = 1200

/**
 * Approving a Kitabxana 2.0 sign-in from this phone.
 *
 * The other way in is the scanner: the quiz shows a QR, this app reads it, and the employee is signed
 * in. That only works when the QR and the camera are on different devices — on one phone it cannot,
 * because a screen cannot photograph itself. So the quiz sends people here instead, with the same code
 * in the address, and this screen does what a scan does: asks, then vouches.
 *
 * Asking is the point. The code arrives in a link, and a link can be sent by anyone; signing in on
 * arrival would let a message someone forwards sign an employee into the quiz without their noticing.
 * One deliberate tap, and the server (which holds the shared secret and reads the name and phone from
 * the staff record) does the rest.
 */
export function KitabxanaSignInPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const code = readCode(params.get('code'))
  // The exact screen the sign-in started on: the panel for an administrator, the game for a player.
  const returnUrl = readReturnUrl(params.get('return'))
  const name = listProfiles().find((p) => p.employeeId === activeProfileId())?.name ?? null

  const [phase, setPhase] = useState<'ask' | 'busy' | 'done'>('ask')
  const [error, setError] = useState<string | null>(null)

  // Whether the quiz opened this in a tab of its own. That tab is still there, still waiting, and it
  // finishes the sign-in by itself - so the thing to do is get out of the way, not open the quiz a
  // second time beside it.
  const openedByTheQuiz = typeof window !== 'undefined' && window.opener !== null

  // Approved: hand the screen back rather than leave the employee on a page with nothing left to do.
  useEffect(() => {
    if (phase !== 'done') return
    const timer = window.setTimeout(() => {
      if (openedByTheQuiz) {
        // Only a window a script opened may close itself; if the browser refuses, the message below stays.
        window.close()
        return
      }
      if (returnUrl) window.location.assign(returnUrl)
      else navigate('/home', { replace: true })
    }, LEAVE_AFTER_MS)
    return () => window.clearTimeout(timer)
  }, [phase, returnUrl, navigate, openedByTheQuiz])

  async function approve() {
    if (!code) return
    setPhase('busy')
    setError(null)
    try {
      const { status, data } = await apiRequest('/api/kitabxana/sign-in', { method: 'POST', body: { code } })
      const outcome = classifySignIn(status, data)
      if (outcome.kind === 'signed-in') {
        setPhase('done')
        return
      }
      setPhase('ask')
      setError(outcome.message)
    } catch {
      // apiRequest only throws when the request never got an answer at all.
      setPhase('ask')
      setError(NETWORK_MESSAGE)
    }
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <SubPageHeader title="Kitabxana 2.0" back="/home" />
      <main className="mx-auto max-w-md p-4">
        {!code ? (
          <section className="rounded-3xl border border-amber-200 bg-amber-50 p-6 text-center">
            <div className="text-5xl" aria-hidden="true">🔗</div>
            <h2 className="mt-3 text-lg font-bold text-slate-900">Giriş kodu yoxdur</h2>
            <p className="mt-2 text-slate-700">
              Bu səhifə Kitabxana 2.0-dan açılmalıdır. Orada giriş ekranında
              <strong> “QRLog tətbiqi ilə təsdiqlə” </strong> düyməsini basın.
            </p>
            <a
              href={KITABXANA_URL}
              className="mt-5 inline-flex min-h-12 items-center justify-center rounded-2xl bg-slate-900 px-6 font-semibold text-white"
            >
              Kitabxana 2.0-ı aç
            </a>
          </section>
        ) : phase === 'done' ? (
          <section className="rounded-3xl border border-green-200 bg-green-50 p-6 text-center" aria-live="polite">
            <div className="text-5xl" aria-hidden="true">✅</div>
            <h2 className="mt-3 text-lg font-bold text-slate-900">Təsdiqləndi</h2>
            <p className="mt-2 text-slate-700">
              {openedByTheQuiz
                ? 'Kitabxana səhifəsinə qayıdın — giriş orada özü davam edir.'
                : returnUrl
                  ? 'Kitabxana ekranına qaytarılırsınız…'
                  : 'Kitabxana səhifəsinə qayıdın — giriş orada özü davam edir.'}
            </p>
            <a
              href={returnUrl ?? KITABXANA_URL}
              className="mt-5 inline-flex min-h-12 items-center justify-center rounded-2xl bg-green-600 px-6 font-semibold text-white"
            >
              Kitabxana 2.0-a keç
            </a>
          </section>
        ) : (
          <section className="rounded-3xl border border-slate-200 bg-white p-6 text-center">
            <div className="text-5xl" aria-hidden="true">📚</div>
            <h2 className="mt-3 text-lg font-bold text-slate-900">Kitabxana 2.0-a giriş təsdiqlənsin?</h2>
            <p className="mt-2 text-slate-700">
              {name ? <><strong>{name}</strong> adı ilə</> : 'Öz adınızla'} giriş ediləcək. Adınız və mobil nömrəniz
              yarışa göndəriləcək; başqa heç nə paylaşılmır.
            </p>

            {error && (
              <p role="alert" className="mt-4 rounded-2xl border border-red-200 bg-red-50 p-3 text-left text-sm font-medium text-red-800">
                {error}
              </p>
            )}

            <button
              type="button"
              onClick={() => void approve()}
              disabled={phase === 'busy'}
              className="mt-5 inline-flex min-h-12 w-full items-center justify-center rounded-2xl bg-slate-900 px-6 font-semibold text-white disabled:opacity-60"
            >
              {phase === 'busy' ? 'Təsdiqlənir…' : 'Təsdiqlə'}
            </button>
            <p className="mt-3 text-xs text-slate-500">
              Bu, davamiyyət qeydi deyil: selfi çəkilmir, məkan yoxlanmır.
            </p>
          </section>
        )}
      </main>
    </div>
  )
}
