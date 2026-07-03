import { useState } from 'react'
import { requestDeviceChange } from '../api/deviceChange'
import { getDeviceFingerprint } from '../lib/device'
import { EmployeeNav } from '../components/EmployeeNav'

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
    <div className="min-h-screen flex flex-col bg-slate-900 text-white">
      <EmployeeNav title="Cihaz dəyişimi" />

      <main className="flex-1 flex flex-col items-center justify-center p-4">
        <div className="w-full max-w-sm">
          <h1 className="text-2xl font-bold text-center mb-2">Yeni telefon</h1>
          <p className="text-slate-300 text-center text-base mb-6">
            Yeni telefondan sistemə daxil olmaq üçün admin təsdiqi lazımdır. Aşağıdakı düyməni basın
            — bu telefon admin təsdiqinə göndəriləcək.
          </p>

          {result && (
            <div className={`rounded-2xl p-5 text-center mb-4 ${toneClass}`}>
              <div className="text-4xl font-bold mb-2">
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
              className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold rounded-2xl py-4 text-lg transition"
            >
              {loading ? 'Göndərilir…' : 'Bu telefonu təsdiq üçün göndər'}
            </button>
          )}
        </div>
      </main>
    </div>
  )
}
