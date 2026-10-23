import { useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { apiRequest } from '../api/client'
import { SubPageHeader } from '../components/SubPageHeader'
import { activeProfileId, listProfiles } from '../lib/profiles'
import { NETWORK_MESSAGE, cancelUrl, classifySignIn, readApp, readCode, readReturnUrl } from './externalSignInOutcome'

/** Long enough to read "Təsdiqləndi", short enough that nobody wonders whether it worked. */
const LEAVE_AFTER_MS = 1200

/**
 * Approving a sign-in to another application of ours (PRIZMA) from this phone — the Kitabxana approval, for any
 * registered app. The app sends the person here with a ticket code in the address; one deliberate tap, and the
 * server (which holds the shared secret and reads the name from the staff record) vouches for whoever is signed
 * in here. Asking is the point: the code arrives in a link, and a link can be sent by anyone, so signing in on
 * arrival would let a forwarded message sign an employee into the app without their noticing.
 */
export function ExternalSignInPage() {
  const { app: appParam } = useParams()
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const app = readApp(appParam)
  const code = readCode(params.get('code'))
  const returnUrl = readReturnUrl(params.get('return'), app)
  const name = listProfiles().find((p) => p.employeeId === activeProfileId())?.name ?? null

  const [phase, setPhase] = useState<'ask' | 'busy' | 'done'>('ask')
  const [error, setError] = useState<string | null>(null)

  // Approved on this phone for this phone: hand the screen back to the app that asked. Approved for a computer (a
  // scanned QR has no way back): stay here and say where the sign-in continues.
  useEffect(() => {
    if (phase !== 'done' || !returnUrl) return
    const timer = window.setTimeout(() => window.location.assign(returnUrl), LEAVE_AFTER_MS)
    return () => window.clearTimeout(timer)
  }, [phase, returnUrl])

  async function approve() {
    if (!code || !app) return
    setPhase('busy')
    setError(null)
    try {
      const { status, data } = await apiRequest(`/api/external-signin/${app.key}/confirm`, { method: 'POST', body: { code } })
      const outcome = classifySignIn(status, data, app.name)
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

  function decline() {
    if (returnUrl) window.location.assign(cancelUrl(returnUrl))
    else navigate('/menu', { replace: true })
  }

  const title = app ? `${app.name}-a giriş` : 'Giriş'

  return (
    <div className="min-h-screen bg-slate-50">
      <SubPageHeader title={title} back="/home" />
      <main className="mx-auto max-w-md p-4">
        {!app || !code ? (
          <section className="rounded-3xl border border-amber-200 bg-amber-50 p-6 text-center">
            <div className="text-5xl" aria-hidden="true">🔗</div>
            <h2 className="mt-3 text-lg font-bold text-slate-900">Giriş kodu yoxdur</h2>
            <p className="mt-2 text-slate-700">
              Bu səhifə {app?.name ?? 'tətbiq'} tərəfindən açılmalıdır. Orada giriş ekranında
              <strong> “QRLog ilə daxil ol” </strong> düyməsini basın.
            </p>
            {app && (
              <a
                href={app.homeUrl}
                className="mt-5 inline-flex min-h-12 items-center justify-center rounded-2xl bg-slate-900 px-6 font-semibold text-white"
              >
                {app.name}-ı aç
              </a>
            )}
          </section>
        ) : phase === 'done' ? (
          <section className="rounded-3xl border border-green-200 bg-green-50 p-6 text-center" aria-live="polite">
            <div className="text-5xl" aria-hidden="true">✅</div>
            <h2 className="mt-3 text-lg font-bold text-slate-900">Təsdiqləndi</h2>
            <p className="mt-2 text-slate-700">
              {returnUrl
                ? `${app.name} səhifəsinə qaytarılırsınız…`
                : `Kompüterinizə qayıdın — ${app.name}-da giriş orada avtomatik davam edəcək.`}
            </p>
            {returnUrl ? (
              <a
                href={returnUrl}
                className="mt-5 inline-flex min-h-12 items-center justify-center rounded-2xl bg-green-600 px-6 font-semibold text-white"
              >
                {app.name}-a keç
              </a>
            ) : (
              <button
                type="button"
                onClick={() => navigate('/menu', { replace: true })}
                className="mt-5 inline-flex min-h-12 items-center justify-center rounded-2xl bg-green-600 px-6 font-semibold text-white"
              >
                Xidmətlərə qayıt
              </button>
            )}
          </section>
        ) : (
          <section className="rounded-3xl border border-slate-200 bg-white p-6 text-center">
            <div className="text-5xl" aria-hidden="true">🎨</div>
            <h2 className="mt-3 text-lg font-bold text-slate-900">{app.name}-a daxil olmaq istəyirsiniz?</h2>
            <p className="mt-1 text-sm text-slate-500">{app.description}</p>
            <p className="mt-3 text-slate-700">
              {name ? <><strong>{name}</strong> adı ilə</> : 'Öz adınızla'} giriş ediləcək. Yalnız adınız və e-mail ünvanınız (varsa)
              göndəriləcək; telefon nömrəniz, şirkətiniz və vəzifəniz paylaşılmır.
            </p>
            {!returnUrl && (
              <p className="mt-3 rounded-2xl border border-amber-200 bg-amber-50 p-3 text-left text-sm text-amber-900">
                Yalnız <strong>öz kompüterinizin</strong> ekranında gördüyünüz QR kodu təsdiqləyin. Başqasının göstərdiyi kodu
                təsdiqləsəniz, o, sizin adınızla daxil olar.
              </p>
            )}

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
            <button
              type="button"
              onClick={decline}
              disabled={phase === 'busy'}
              className="mt-3 inline-flex min-h-12 w-full items-center justify-center rounded-2xl border border-slate-300 bg-white px-6 font-semibold text-slate-700 disabled:opacity-60"
            >
              İmtina et
            </button>
            <p className="mt-3 text-xs text-slate-500">Bu, davamiyyət qeydi deyil: selfi çəkilmir, məkan yoxlanmır.</p>
          </section>
        )}
      </main>
    </div>
  )
}
