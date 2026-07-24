import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { acceptConsent, getMyProfile } from '../api/attendance'

/**
 * The data-processing notice an employee sees once, before the app collects anything.
 *
 * The app stores a face photo, a GPS position and salary — personal data, and in the case of the
 * face, biometric. Collecting that without telling the person what is taken, why, and who sees it is
 * not defensible, and "the manager told them verbally" is not a record. This is the digital stand-in
 * for a signature: it blocks the app until they accept, and the acceptance is stamped server-side so
 * there is an actual answer to "did this employee agree, and when".
 *
 * Deliberately blocking rather than a dismissible banner — a notice you can swipe away has not been
 * given. It is shown once per employee, never again.
 */
export function ConsentGate({ children }: { children: React.ReactNode }) {
  const [needed, setNeeded] = useState<boolean | null>(null) // null = still checking
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState(false)

  useEffect(() => {
    void getMyProfile().then((r) => {
      // A failed lookup must not lock anyone out of recording their attendance — default to letting
      // them through and ask again next time.
      if (r.status === 200 && r.data && 'fullName' in r.data) setNeeded(r.data.consentRequired === true)
      else setNeeded(false)
    })
  }, [])

  async function accept() {
    setBusy(true)
    setErr(false)
    const { status } = await acceptConsent()
    setBusy(false)
    if (status === 200) setNeeded(false)
    else setErr(true)
  }

  // Still checking, or already accepted — get out of the way entirely.
  if (needed === null || needed === false) return <>{children}</>

  return (
    <div className="min-h-screen bg-slate-50 px-4 py-8">
      <div className="mx-auto max-w-md">
        <div className="rounded-3xl border border-slate-200 bg-white p-6 shadow-sm">
          <div className="text-4xl">🔒</div>
          <h1 className="mt-3 text-xl font-bold text-slate-900">Məlumatlarınız haqqında</h1>
          <p className="mt-2 text-sm text-slate-600">
            Bu tətbiq davamiyyətinizi qeyd etmək üçün aşağıdakı məlumatları emal edir.
            Davam etməzdən əvvəl razılığınızı istəyirik.
          </p>

          <div className="mt-5 space-y-3">
            <Item icon="📍" title="Yerləşdiyiniz yer (GPS)">
              Yalnız skan anında — iş yerində olduğunuzu təsdiqləmək üçün. Sizi gün ərzində izləmir.
            </Item>
            <Item icon="📸" title="Giriş şəkli">
              Girişin sizin tərəfinizdən edildiyini təsdiqləmək üçün. Yalnız iş yerinizin rəhbərliyi görür.
            </Item>
            <Item icon="🕒" title="İş məlumatları">
              Giriş-çıxış saatları, davamiyyət və əməkhaqqı hesablaması.
            </Item>
          </div>

          <p className="mt-5 rounded-2xl bg-slate-50 p-3 text-sm text-slate-600">
            Məlumatlarınız <b>yalnız işlədiyiniz şirkətə</b> göstərilir, başqa şirkətlərlə paylaşılmır
            və üçüncü tərəfə satılmır.
          </p>

          <Link to="/privacy" className="mt-3 block text-center text-sm font-semibold text-blue-600 underline underline-offset-4">
            Ətraflı məlumat
          </Link>

          {err && (
            <p className="mt-3 rounded-xl bg-red-50 p-3 text-sm text-red-700">
              Yadda saxlanılmadı. İnternet bağlantınızı yoxlayıb yenidən cəhd edin.
            </p>
          )}

          <button
            onClick={() => void accept()}
            disabled={busy}
            className="mt-5 w-full rounded-2xl bg-blue-600 py-4 text-base font-bold text-white disabled:opacity-50"
          >
            {busy ? 'Göndərilir…' : 'Razıyam, davam et'}
          </button>
          <p className="mt-3 text-center text-xs text-slate-400">
            Sualınız varsa rəhbərinizlə əlaqə saxlayın.
          </p>
        </div>
      </div>
    </div>
  )
}

function Item({ icon, title, children }: { icon: string; title: string; children: React.ReactNode }) {
  return (
    <div className="flex gap-3">
      <span className="text-xl leading-none">{icon}</span>
      <span className="min-w-0">
        <span className="block text-sm font-bold text-slate-900">{title}</span>
        <span className="block text-sm text-slate-600">{children}</span>
      </span>
    </div>
  )
}
