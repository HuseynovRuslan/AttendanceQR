import { useEffect } from 'react'
import { IconX } from './icons'
import { FaceFlagBadge } from './FaceFlagBadge'

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
  onClose: () => void
}

/**
 * Side-by-side photo comparison for the photo-audit feature: reference selfie (left) vs. the
 * check-in selfie (right). A manager compares them by eye — no automatic face matching. Shared by
 * PhotoAuditPage and the Today board so both open the exact same view.
 */
export function PhotoCompareModal({ title, referenceUrl, checkInUrl, checkInTakenAtUtc, faceMatchStatus, faceMatchScore, onClose }: PhotoCompareModalProps) {
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
        zIndex: 50,
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

function fmtTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('az-AZ', { hour: '2-digit', minute: '2-digit' })
}
