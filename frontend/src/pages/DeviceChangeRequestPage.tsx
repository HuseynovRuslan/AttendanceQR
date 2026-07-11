import { useState } from 'react'
import { requestDeviceChange } from '../api/deviceChange'
import { getDeviceFingerprint } from '../lib/device'
import { SubPageHeader } from '../components/SubPageHeader'

type Result = { tone: 'green' | 'yellow' | 'red'; title: string; detail?: string }

export function DeviceChangeRequestPage() {
  const [loading, setLoading] = useState(false)
  const [result, setResult] = useState<Result | null>(null)

  async function onSubmit() {
    setLoading(true)
    setResult(null)
    try {
      const { status } = await requestDeviceChange(getDeviceFingerprint())
      if (status === 201) {
        setResult({
          tone: 'green',
          title: 'Tələbiniz göndərildi',
          detail: 'Admin təsdiqləyəndə bu telefondan skan edə biləcəksiniz.',
        })
      } else if (status === 409) {
        setResult({
          tone: 'yellow',
          title: 'Artıq gözləyən tələbiniz var',
          detail: 'Admin təsdiqləməyi gözləyin.',
        })
      } else {
        setResult({ tone: 'red', title: 'Tələb göndərilmədi', detail: 'Yenidən cəhd edin.' })
      }
    } catch {
      setResult({ tone: 'red', title: 'Şəbəkə xətası', detail: 'Serverə qoşulmaq mümkün olmadı.' })
    } finally {
      setLoading(false)
    }
  }

  const toneClass = result
    ? { green: 'bg-green-500 text-white', yellow: 'bg-yellow-400 text-slate-900', red: 'bg-red-500 text-white' }[
        result.tone
      ]
    : ''

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <SubPageHeader title="Yeni telefon" />

      <main className="mx-auto w-full max-w-sm p-4">
        <div className="rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
          <p className="mb-6 text-center text-base text-slate-500">
            Yeni telefondan sistemə daxil olmaq üçün admin təsdiqi lazımdır. Aşağıdakı düyməni basın
            — bu telefon admin təsdiqinə göndəriləcək.
          </p>

          {result && (
            <div className={`mb-4 rounded-2xl p-5 text-center ${toneClass}`}>
              <div className="mb-2 text-4xl font-bold">
                {result.tone === 'green' ? '✓' : result.tone === 'yellow' ? '!' : '✕'}
              </div>
              <h2 className="text-lg font-bold">{result.title}</h2>
              {result.detail && <p className="mt-1 text-base opacity-90">{result.detail}</p>}
            </div>
          )}

          {(!result || result.tone === 'red') && (
            <button
              onClick={onSubmit}
              disabled={loading}
              className="w-full rounded-2xl bg-blue-600 py-4 text-lg font-bold text-white transition hover:bg-blue-700 disabled:opacity-50"
            >
              {loading ? 'Göndərilir…' : 'Bu telefonu təsdiq üçün göndər'}
            </button>
          )}
        </div>
      </main>
    </div>
  )
}
