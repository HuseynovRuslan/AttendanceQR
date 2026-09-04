import { useEffect, useState } from 'react'
import { sendPhotoWarning, voidFraudRecord } from '../api/attendance'
import { IconX } from './icons'
import { FaceFlagBadge } from './FaceFlagBadge'
import { fmtTime } from '../lib/format'

interface PhotoCompareModalProps {
  /** Header line, e.g. "Ad Soyad — 08.07.2026". */
  title: string
  /** Presigned URL of the employee's reference selfie (left), or null if none exists. */
  referenceUrl: string | null
  /** Presigned URL of this check-in's selfie (right), or null if the check-in had no photo. */
  checkInUrl: string | null
  /** When the check-in selfie was taken (UTC ISO), shown under the "Giriş" caption. */
  checkInTakenAtUtc: string | null
  /** Face-audit verdict for this check-in (optional). */
  faceMatchStatus?: string
  faceMatchScore?: number | null
  /** The record behind these photographs. Present → the modal can act on it; absent → read-only. */
  recordId?: string | null
  /** Whether the viewer may take a disciplinary action here. Admin-only, decided by the caller —
   *  a manager can already see the photos and must not be able to void a day from them. */
  canAct?: boolean
  /** Called after a successful void so the caller can refresh the board behind. */
  onActed?: () => void
  onClose: () => void
}

/**
 * Side-by-side photo comparison for the photo-audit feature: reference selfie (left) vs. the
 * check-in selfie (right). A manager compares them by eye — no automatic face matching. Shared by
 * the Today board and the employee profile — the two places a face-flagged row can be opened from.
 */
export function PhotoCompareModal({
  title, referenceUrl, checkInUrl, checkInTakenAtUtc, faceMatchStatus, faceMatchScore,
  recordId, canAct = false, onActed, onClose,
}: PhotoCompareModalProps) {
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)

  /**
   * Say it without taking anything away.
   *
   * Offered ABOVE the void and worded as the ordinary choice, because it usually is: a face audit
   * reads a cap, a low sun or a dark room as a mismatch, and taking a day's pay over a bad photograph
   * is a far worse mistake than a message that turns out to have been unnecessary. The void stays one
   * press further down for the case where the two photographs are plainly different people.
   */
  async function warn() {
    if (!recordId) return
    if (!window.confirm(
      'İşçiyə bildiriş göndərilsin?\n\n' +
      'Girişi silinmir, maaşına toxunulmur — sadəcə şəklin yoxlamadan keçmədiyini bilir. ' +
      'Bir gün üçün yalnız bir dəfə göndərilir.')) return

    setBusy(true)
    setFailed(false)
    const { status, data } = await sendPhotoWarning(recordId)
    setBusy(false)
    if (status === 200 && data && 'sent' in data) {
      setDone(data.notified > 0
        ? 'Bildiriş göndərildi — telefonuna çatdı'
        : 'Bildiriş yazıldı — telefonunda tətbiq quraşdırılmayıb, tətbiqin bildirişlər bölməsində görəcək')
      onActed?.()
      return
    }
    if (status === 409) { setDone('Bu gün üçün onsuz da göndərilib'); return }
    setFailed(true)
  }

  /**
   * Void this scan as fraudulent.
   *
   * It does NOT delete: the row and this photograph stay, because this photograph is the entire
   * evidence for an accusation against a named person, and destroying it while making the accusation
   * leaves nothing to stand behind. The day reads Qayıb because every computation skips a voided
   * record, and an admin who got the face wrong can undo it.
   */
  async function act(revokeDevice: boolean) {
    if (!recordId) return
    const question = revokeDevice
      ? 'Bu giriş ləğv edilsin (Qayıb yazılsın), işçiyə bildiriş getsin VƏ cihaz bağlaması silinsin?\n\n' +
        'Diqqət: ortaq briqada telefonunda bu, həmin telefondan istifadə edən BÜTÜN işçiləri çıxarır.'
      : 'Bu giriş ləğv edilsin (Qayıb yazılsın) və işçiyə bildiriş getsin?\n\n' +
        'Şəkil silinmir — sübut olaraq qalır, və qərar geri qaytarıla bilər.'
    if (!window.confirm(question)) return

    setBusy(true)
    setFailed(false)
    const { status, data } = await voidFraudRecord(recordId, { revokeDevice })
    setBusy(false)
    if (status === 200 && data && 'voided' in data) {
      setDone(revokeDevice
        ? `Giriş ləğv edildi · ${data.devicesRevoked} cihaz bağlaması silindi`
        : 'Giriş ləğv edildi — həmin gün Qayıb sayılır')
      onActed?.()
      return
    }
    setFailed(true)
  }

  // Close on Escape.
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed',
        inset: 0,
        background: 'rgba(0,0,0,.6)',
        // Above everything the app can put on screen except the impersonation warning (99999):
        // this is a modal, and it was 50 — under the HQ drawers (9999) and under Leaflet's own
        // panes, which is how a map ended up painted across a photograph.
        zIndex: 10000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 16,
      }}
    >
      <div
        className="card"
        onClick={(e) => e.stopPropagation()}
        style={{ maxWidth: 720, width: '100%', maxHeight: '90vh', overflow: 'auto' }}
      >
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 12,
            padding: '14px 18px',
            borderBottom: '1px solid var(--c100)',
          }}
        >
          <div style={{ minWidth: 0 }}>
            <div style={{ fontWeight: 700, color: 'var(--c900)' }}>{title}</div>
            {faceMatchStatus && faceMatchStatus !== 'NotChecked' && (
              <div style={{ marginTop: 4 }}>
                <FaceFlagBadge status={faceMatchStatus} score={faceMatchScore} />
              </div>
            )}
          </div>
          <button className="btn btn-sm" onClick={onClose} aria-label="Bağla">
            <IconX />
          </button>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, padding: 16 }}>
          <PhotoCell caption="Referans" url={referenceUrl} emptyText="Referans şəkli yoxdur" alt="Referans şəkli" />
          <PhotoCell
            caption={`Giriş${checkInTakenAtUtc ? ` · ${fmtTime(checkInTakenAtUtc)}` : ''}`}
            url={checkInUrl}
            emptyText="Foto yoxdur"
            alt="Giriş şəkli"
          />
        </div>

        {/* The action panel appears only when the viewer may act AND there is a record to act on.
            Shown for ANY check-in, not just a flagged one: the face audit misses as often as it
            catches — a photograph of a screen can score a clean match — and the person looking at
            the two pictures is the one who can actually tell. */}
        {canAct && recordId && (
          <div className="photo-audit-actions">
            {done ? (
              <div className="photo-audit-done">✓ {done}</div>
            ) : (
              <>
                <div className="photo-audit-lead">
                  Üz yoxlaması papağı, eynəyi, qaranlığı da «uyğunsuz» oxuyur — ona görə qərarı rəqəm
                  yox, <b>iki şəklə baxan siz</b> verirsiniz.
                </div>
                {/* The mild action first and visually primary: it is the one that fits most of these
                    photographs. The two destructive ones sit under it, quieter. */}
                <div className="photo-audit-row">
                  <button type="button" className="btn-warn" disabled={busy} onClick={() => void warn()}>
                    {busy ? '…' : 'İşçiyə bildiriş göndər'}
                  </button>
                </div>
                <div className="photo-audit-sep">və ya, şəkil açıq-aydın başqa adamdırsa:</div>
                <div className="photo-audit-row">
                  <button type="button" className="btn-fraud" disabled={busy} onClick={() => void act(false)}>
                    Girişi ləğv et (Qayıb yaz)
                  </button>
                  <button type="button" className="btn-fraud-device" disabled={busy} onClick={() => void act(true)}>
                    Cihazı da sil
                  </button>
                </div>
                {failed && <div className="photo-audit-fail">Alınmadı — yenidən cəhd edin.</div>}
              </>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

function PhotoCell({ caption, url, emptyText, alt }: { caption: string; url: string | null; emptyText: string; alt: string }) {
  return (
    <figure style={{ margin: 0 }}>
      <figcaption style={{ fontSize: 12, fontWeight: 700, color: 'var(--c500)', marginBottom: 6 }}>{caption}</figcaption>
      {url ? (
        <img
          src={url}
          alt={alt}
          style={{ width: '100%', aspectRatio: '3 / 4', objectFit: 'cover', borderRadius: 10, background: '#000', display: 'block' }}
        />
      ) : (
        <div
          className="muted"
          style={{
            aspectRatio: '3 / 4',
            border: '1px dashed var(--c200)',
            borderRadius: 10,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            textAlign: 'center',
            fontSize: 13,
            padding: 8,
          }}
        >
          {emptyText}
        </div>
      )}
    </figure>
  )
}

