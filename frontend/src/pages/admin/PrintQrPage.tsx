import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { jsPDF } from 'jspdf'
import { QRCodeCanvas } from 'qrcode.react'
import { generateStaticQr, invalidateLocationQr, type StaticQrResult } from '../../api/admin'
import { useBranding } from '../../branding/BrandingContext'
import { IconCheck, IconDownload, IconQr, IconX } from '../../components/icons'

// The QR is rendered at print resolution (large), shown small on screen. Its data is unchanged by size.
const QR_RENDER = 1000
const NAVY = '#0F1B2D'
const INK = '#122536'
const MUTED = '#5B6B7C'
const LINE = '#DCE6F1'
const MIST = '#F2F6FB'
// The QRLog wordmark (NOT the square "QR" mark — see the branding rule). Used when a tenant has no
// custom raster logo of its own; a real tenant logo (e.g. a leaf) is used as-is.
const QRLOG_WORDMARK = '/brand/qrlog-logo.png'

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    img.crossOrigin = 'anonymous'
    img.onload = () => resolve(img)
    img.onerror = reject
    img.src = src
  })
}

function roundRectPath(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
  const rr = Math.min(r, w / 2, h / 2)
  ctx.beginPath()
  ctx.moveTo(x + rr, y)
  ctx.arcTo(x + w, y, x + w, y + h, rr)
  ctx.arcTo(x + w, y + h, x, y + h, rr)
  ctx.arcTo(x, y + h, x, y, rr)
  ctx.arcTo(x, y, x + w, y, rr)
  ctx.closePath()
}

/** Largest font size (px) at which `text` fits in `maxW`, between min and max. */
function fitFont(ctx: CanvasRenderingContext2D, text: string, weight: number, maxW: number, max: number, min: number) {
  let size = max
  while (size > min) {
    ctx.font = `${weight} ${size}px Manrope, system-ui, sans-serif`
    if (ctx.measureText(text).width <= maxW) break
    size -= 1
  }
  return size
}

function drawClock(ctx: CanvasRenderingContext2D, cx: number, cy: number, r: number, color: string) {
  ctx.strokeStyle = color
  ctx.lineWidth = r * 0.16
  ctx.lineCap = 'round'
  ctx.beginPath()
  ctx.arc(cx, cy, r, 0, Math.PI * 2)
  ctx.stroke()
  ctx.beginPath()
  ctx.moveTo(cx, cy)
  ctx.lineTo(cx, cy - r * 0.55)
  ctx.moveTo(cx, cy)
  ctx.lineTo(cx + r * 0.42, cy + r * 0.18)
  ctx.stroke()
}

function drawBuilding(ctx: CanvasRenderingContext2D, cx: number, cy: number, s: number, color: string) {
  // A small building glyph: a block with a pitched top and two window dots.
  ctx.strokeStyle = color
  ctx.fillStyle = color
  ctx.lineWidth = s * 0.13
  ctx.lineJoin = 'round'
  const w = s * 0.8
  const h = s
  const x = cx - w / 2
  const y = cy - h / 2
  ctx.beginPath()
  ctx.moveTo(x, y + h)
  ctx.lineTo(x, y + h * 0.32)
  ctx.lineTo(cx, y)
  ctx.lineTo(x + w, y + h * 0.32)
  ctx.lineTo(x + w, y + h)
  ctx.stroke()
  const d = s * 0.09
  ctx.beginPath()
  ctx.arc(cx - w * 0.18, y + h * 0.55, d, 0, Math.PI * 2)
  ctx.arc(cx + w * 0.18, y + h * 0.55, d, 0, Math.PI * 2)
  ctx.fill()
}

export function PrintQrPage() {
  const { locationId } = useParams()
  const navigate = useNavigate()
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const branding = useBranding()

  const [qr, setQr] = useState<StaticQrResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
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

  /**
   * Draws the full A4 poster (QRLog-branded, tenant-adaptive) onto an offscreen canvas at print
   * resolution and returns it. Layout mirrors the approved design: logo → company → branch → title →
   * QR card → footer. Nothing overlaps; the company name auto-fits.
   */
  async function buildPoster(): Promise<HTMLCanvasElement | null> {
    const qrCanvas = canvasRef.current
    if (!qrCanvas || !qr) return null

    // A tenant's own raster logo is used as-is; otherwise (QRLog default, or the square QR-mark svg we
    // must never use) fall back to the QRLog wordmark.
    const logoUrl = branding.logoUrl
    const logoSrc = logoUrl && !logoUrl.endsWith('.svg') ? logoUrl : QRLOG_WORDMARK
    let logo: HTMLImageElement
    try {
      logo = await loadImage(logoSrc)
    } catch {
      logo = await loadImage(QRLOG_WORDMARK)
    }
    // Manrope must be ready or canvas text falls back to a different face (and Azerbaijani ə would shift).
    if (document.fonts?.ready) await document.fonts.ready

    const accent = branding.color && /^#[0-9a-fA-F]{3,8}$/.test(branding.color) ? branding.color : '#1E70C8'
    const company = (branding.displayName || qr.locationName || '').trim()

    const W = 1654
    const H = Math.round(W * Math.SQRT2) // A4 portrait
    const u = W / 100 // 1 "cqw"
    const cx = W / 2
    const canvas = document.createElement('canvas')
    canvas.width = W
    canvas.height = H
    const ctx = canvas.getContext('2d')
    if (!ctx) return null

    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, W, H)

    // Top gradient bar
    const grad = ctx.createLinearGradient(0, 0, W, 0)
    grad.addColorStop(0, NAVY)
    grad.addColorStop(1, accent)
    ctx.fillStyle = grad
    ctx.fillRect(0, 0, W, 1.7 * u)

    // Inset frame
    roundRectPath(ctx, 3 * u, 3 * u, W - 6 * u, H - 6 * u, 1.5 * u)
    ctx.lineWidth = 0.2 * u
    ctx.strokeStyle = LINE
    ctx.stroke()

    ctx.textAlign = 'center'
    ctx.textBaseline = 'alphabetic'

    let y = 9 * u

    // Logo
    const logoH = 11 * u
    const logoW = (logo.width / logo.height) * logoH
    ctx.drawImage(logo, cx - logoW / 2, y, logoW, logoH)
    y += logoH + 5 * u

    // Company name (auto-fit to one line)
    const compSize = fitFont(ctx, company, 700, W - 20 * u, 9 * u, 4.6 * u)
    ctx.font = `700 ${compSize}px Manrope, system-ui, sans-serif`
    ctx.fillStyle = NAVY
    y += compSize
    ctx.fillText(company, cx, y)
    y += 4.2 * u

    // Branch chip
    const branch = (qr.locationName || '').trim()
    if (branch) {
      const chipFont = 3.4 * u
      ctx.font = `600 ${chipFont}px Manrope, system-ui, sans-serif`
      const tw = ctx.measureText(branch).width
      const iconS = 3.6 * u
      const gap = 1.6 * u
      const padX = 3.4 * u
      const chipW = padX * 2 + iconS + gap + tw
      const chipH = 6.6 * u
      const chipX = cx - chipW / 2
      const chipY = y
      roundRectPath(ctx, chipX, chipY, chipW, chipH, chipH / 2)
      ctx.fillStyle = MIST
      ctx.fill()
      ctx.lineWidth = 1
      ctx.strokeStyle = LINE
      ctx.stroke()
      drawBuilding(ctx, chipX + padX + iconS / 2, chipY + chipH / 2, iconS, accent)
      ctx.fillStyle = INK
      ctx.textAlign = 'left'
      ctx.textBaseline = 'middle'
      ctx.fillText(branch, chipX + padX + iconS + gap, chipY + chipH / 2 + chipFont * 0.05)
      ctx.textAlign = 'center'
      ctx.textBaseline = 'alphabetic'
      y += chipH + 5 * u
    }

    // Divider with a diamond
    ctx.strokeStyle = LINE
    ctx.lineWidth = 1.5
    ctx.beginPath()
    ctx.moveTo(cx - 20 * u, y)
    ctx.lineTo(cx + 20 * u, y)
    ctx.stroke()
    ctx.save()
    ctx.translate(cx, y)
    ctx.rotate(Math.PI / 4)
    ctx.fillStyle = accent
    ctx.fillRect(-1.2 * u, -1.2 * u, 2.4 * u, 2.4 * u)
    ctx.restore()
    y += 6 * u

    // Title
    const titleSize = 6.2 * u
    ctx.font = `700 ${titleSize}px Manrope, system-ui, sans-serif`
    ctx.fillStyle = accent
    y += titleSize
    ctx.fillText('QR Davamiyyət', cx, y)
    y += 3.4 * u

    // Subtitle (two lines)
    const subSize = 3.6 * u
    ctx.font = `400 ${subSize}px Manrope, system-ui, sans-serif`
    ctx.fillStyle = MUTED
    y += subSize
    ctx.fillText('Davamiyyəti qeyd etmək üçün', cx, y)
    y += subSize * 1.42
    ctx.fillText('QR kodu telefonla skan edin', cx, y)
    const subBottom = y + 2 * u

    // ---- Footer (pinned to the bottom) ----
    const waveTop = H - 13 * u
    const noteFont = 3.5 * u
    const noteBaseline = waveTop - 4 * u

    // QR card: centered in the space between the subtitle and the footer note, capped.
    const noteTop = noteBaseline - noteFont
    const avail = noteTop - subBottom
    const cardSize = Math.min(46 * u, avail - 4 * u)
    const cardY = subBottom + (avail - cardSize) / 2
    const cardX = cx - cardSize / 2
    ctx.save()
    ctx.shadowColor = 'rgba(15,27,45,0.14)'
    ctx.shadowBlur = 6 * u
    ctx.shadowOffsetY = 2.5 * u
    roundRectPath(ctx, cardX, cardY, cardSize, cardSize, 4.5 * u)
    ctx.fillStyle = '#ffffff'
    ctx.fill()
    ctx.restore()
    roundRectPath(ctx, cardX, cardY, cardSize, cardSize, 4.5 * u)
    ctx.lineWidth = 1.5
    ctx.strokeStyle = LINE
    ctx.stroke()
    // The real QR, kept pixel-sharp (no smoothing) inside the card's quiet zone.
    const quiet = cardSize * 0.09
    const qrDim = cardSize - quiet * 2
    ctx.imageSmoothingEnabled = false
    ctx.drawImage(qrCanvas, cardX + quiet, cardY + quiet, qrDim, qrDim)
    ctx.imageSmoothingEnabled = true

    // Footer note (clock + text)
    ctx.font = `600 ${noteFont}px Manrope, system-ui, sans-serif`
    const noteText = 'İşə giriş və çıxış zamanı QR kodu oxudun'
    const ntw = ctx.measureText(noteText).width
    const clockR = noteFont * 0.62
    const noteGap = 2 * u
    const totalW = clockR * 2 + noteGap + ntw
    const startX = cx - totalW / 2
    drawClock(ctx, startX + clockR, noteBaseline - noteFont * 0.35, clockR, accent)
    ctx.fillStyle = NAVY
    ctx.textAlign = 'left'
    ctx.fillText(noteText, startX + clockR * 2 + noteGap, noteBaseline)
    ctx.textAlign = 'center'

    // Bottom wave
    ctx.fillStyle = accent
    ctx.globalAlpha = 0.9
    ctx.beginPath()
    ctx.moveTo(0, waveTop + 3 * u)
    ctx.bezierCurveTo(W * 0.22, waveTop - 2 * u, W * 0.38, waveTop + 7 * u, W * 0.6, waveTop + 3.5 * u)
    ctx.bezierCurveTo(W * 0.78, waveTop + 1 * u, W * 0.9, waveTop + 5 * u, W, waveTop + 3 * u)
    ctx.lineTo(W, H)
    ctx.lineTo(0, H)
    ctx.closePath()
    ctx.fill()
    ctx.globalAlpha = 1
    ctx.fillStyle = NAVY
    ctx.beginPath()
    ctx.moveTo(0, waveTop + 6 * u)
    ctx.bezierCurveTo(W * 0.2, waveTop + 2 * u, W * 0.4, waveTop + 9.5 * u, W * 0.62, waveTop + 6.5 * u)
    ctx.bezierCurveTo(W * 0.8, waveTop + 4 * u, W * 0.92, waveTop + 8 * u, W, waveTop + 6.5 * u)
    ctx.lineTo(W, H)
    ctx.lineTo(0, H)
    ctx.closePath()
    ctx.fill()

    // powered by QRLog
    ctx.font = `600 ${2.9 * u}px Manrope, system-ui, sans-serif`
    ctx.fillStyle = '#ffffff'
    ctx.fillText('powered by QRLog', cx, H - 2.6 * u)

    return canvas
  }

  async function downloadPng() {
    if (!qr) return
    setBusy(true)
    setError(null)
    try {
      const poster = await buildPoster()
      if (!poster) return
      const a = document.createElement('a')
      a.href = poster.toDataURL('image/png')
      a.download = `qr-${slug(qr.locationName)}.png`
      a.click()
    } catch {
      setError('Afişa yaradıla bilmədi')
    } finally {
      setBusy(false)
    }
  }

  async function downloadPdf() {
    if (!qr) return
    setBusy(true)
    setError(null)
    try {
      const poster = await buildPoster()
      if (!poster) return
      const doc = new jsPDF({ unit: 'mm', format: 'a4' })
      const pw = doc.internal.pageSize.getWidth()
      const ph = doc.internal.pageSize.getHeight()
      doc.addImage(poster.toDataURL('image/png'), 'PNG', 0, 0, pw, ph)
      doc.save(`qr-${slug(qr.locationName)}.pdf`)
    } catch {
      setError('PDF yaradıla bilmədi')
    } finally {
      setBusy(false)
    }
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
                {/* Rendered large for print sharpness, shown small. Navy modules match the poster. */}
                <QRCodeCanvas
                  ref={canvasRef}
                  value={qr.token}
                  size={QR_RENDER}
                  level="M"
                  fgColor={NAVY}
                  style={{ width: 220, height: 220 }}
                />
              </div>
            </div>

            <div className="muted" style={{ fontSize: 12, marginBottom: 14 }}>
              Aşağıdakı düymə hazır afişanı (QRLog dizaynı, şirkət adı + filial ilə) endirir.
            </div>

            <div style={{ display: 'flex', gap: 10, justifyContent: 'center', flexWrap: 'wrap' }}>
              <button className="btn btn-primary" disabled={busy} onClick={() => void downloadPng()}>
                <IconDownload /> {busy ? 'Hazırlanır…' : 'PNG afişa'}
              </button>
              <button className="btn btn-primary" disabled={busy} onClick={() => void downloadPdf()}>
                <IconDownload /> {busy ? 'Hazırlanır…' : 'PDF afişa'}
              </button>
              <button className="btn btn-danger" disabled={invalidating} onClick={() => void onInvalidate()}>
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
