import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { jsPDF } from 'jspdf'
import { QRCodeCanvas } from 'qrcode.react'
import { generateStaticQr, invalidateLocationQr, type StaticQrResult } from '../../api/admin'
import { IconCheck, IconDownload, IconQr, IconX } from '../../components/icons'

const QR_SIZE = 480

export function PrintQrPage() {
  const { locationId } = useParams()
  const navigate = useNavigate()
  const canvasRef = useRef<HTMLCanvasElement>(null)

  const [qr, setQr] = useState<StaticQrResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [invalidating, setInvalidating] = useState(false)

  async function load() {
    if (!locationId) return
    setLoading(true)
    setError(null)
    const { status, data } = await generateStaticQr(locationId)
    setLoading(false)
    if (status === 200 && data && 'token' in data) {
      setQr(data)
    } else {
      setError('Lokasiya tapılmadı')
    }
  }

  useEffect(() => {
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [locationId])

  function getCanvasPngDataUrl(): string | null {
    const canvas = canvasRef.current
    if (!canvas) return null
    return canvas.toDataURL('image/png')
  }

  function downloadPng() {
    const dataUrl = getCanvasPngDataUrl()
    if (!dataUrl || !qr) return
    const a = document.createElement('a')
    a.href = dataUrl
    a.download = `qr-${slug(qr.locationName)}.png`
    a.click()
  }

  function downloadPdf() {
    const dataUrl = getCanvasPngDataUrl()
    if (!dataUrl || !qr) return

    const doc = new jsPDF({ unit: 'mm', format: 'a4' })
    const pageWidth = doc.internal.pageSize.getWidth()
    const qrSizeMm = 120
    const x = (pageWidth - qrSizeMm) / 2

    doc.setFontSize(20)
    doc.text(qr.locationName, pageWidth / 2, 30, { align: 'center' })
    doc.setFontSize(12)
    doc.text('Davamiyyət üçün QR kodu skan edin', pageWidth / 2, 40, { align: 'center' })

    doc.addImage(dataUrl, 'PNG', x, 55, qrSizeMm, qrSizeMm)

    doc.setFontSize(10)
    doc.setTextColor(120)
    doc.text(
      `Etibarlıdır: ${new Date(qr.expiresAtUtc).toLocaleDateString('az-AZ')} tarixinə qədər`,
      pageWidth / 2,
      55 + qrSizeMm + 12,
      { align: 'center' },
    )

    doc.save(`qr-${slug(qr.locationName)}.pdf`)
  }

  async function onInvalidate() {
    if (!locationId) return
    if (
      !window.confirm(
        'Bu lokasiyanın BÜTÜN QR kodları (kiosk ekranı DAXİL) ləğv ediləcək və yeni kod yaradılacaq. Çap olunmuş köhnə posterlər artıq işləməyəcək. Davam edilsin?',
      )
    )
      return
    setInvalidating(true)
    setError(null)
    setOk(null)
    const { status } = await invalidateLocationQr(locationId)
    if (status === 200) {
      await load()
      setOk('Köhnə kodlar ləğv edildi — yeni kod aşağıdadır.')
    } else {
      setError('Ləğv edilmədi')
    }
    setInvalidating(false)
  }

  return (
    <div style={{ maxWidth: 620 }}>
      <button className="btn btn-sm" style={{ marginBottom: 16 }} onClick={() => navigate('/admin/locations')}>
        ← Lokasiyalara qayıt
      </button>

      <div className="fb fb-info" style={{ marginBottom: 16 }}>
        <IconQr />
        <span>
          Bu, kiosk ekranındakı QR-dan fərqlidir — <b>30 gün etibarlıdır</b>, çap edib divara/qapıya
          yapışdıra bilərsiniz. Kiosk QR-ı (60 saniyədə bir dəyişən) daha təhlükəsizdir; bunu yalnız
          çap üçün rahatlıq məqsədilə istifadə edin.
        </span>
      </div>

      {error && (
        <div className="fb fb-err" style={{ marginBottom: 16 }}>
          <IconX />
          <span>{error}</span>
        </div>
      )}
      {ok && (
        <div className="fb fb-ok" style={{ marginBottom: 16 }}>
          <IconCheck />
          <span>{ok}</span>
        </div>
      )}

      <div className="card card-pad" style={{ textAlign: 'center' }}>
        {loading && <p className="muted">Yüklənir…</p>}

        {qr && !loading && (
          <>
            <div style={{ fontWeight: 700, fontSize: 18, color: 'var(--c900)', marginBottom: 4 }}>
              {qr.locationName}
            </div>
            <div className="muted" style={{ fontSize: 12, marginBottom: 18 }}>
              Etibarlıdır: {new Date(qr.expiresAtUtc).toLocaleDateString('az-AZ')} tarixinə qədər
            </div>

            <div style={{ display: 'flex', justifyContent: 'center', marginBottom: 20 }}>
              <div style={{ background: '#fff', padding: 16, borderRadius: 16, border: '1px solid var(--c100)' }}>
                <QRCodeCanvas ref={canvasRef} value={qr.token} size={QR_SIZE} level="M" />
              </div>
            </div>

            <div style={{ display: 'flex', gap: 10, justifyContent: 'center', flexWrap: 'wrap' }}>
              <button className="btn btn-primary" onClick={downloadPng}>
                <IconDownload /> PNG endir
              </button>
              <button className="btn btn-primary" onClick={downloadPdf}>
                <IconDownload /> PDF endir
              </button>
              <button className="btn btn-danger" disabled={invalidating} onClick={onInvalidate}>
                {invalidating ? 'Ləğv edilir…' : 'Köhnə kodları ləğv et'}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  )
}

function slug(name: string): string {
  return name
    .toLowerCase()
    .replace(/[əöüğşçı]/g, (c) => ({ ə: 'e', ö: 'o', ü: 'u', ğ: 'g', ş: 's', ç: 'c', ı: 'i' })[c] ?? c)
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/(^-|-$)/g, '')
}
