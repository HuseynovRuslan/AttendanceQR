import { useRef, useState } from 'react'
import { deleteMyAvatar, setMyAvatar } from '../api/attendance'
import { useAuth } from '../auth/AuthContext'
import { dropAvatar, fileToSquareJpeg, putAvatar, stripDataUrl } from '../lib/avatar'
import { Avatar } from './Avatar'
import { IconCamera, IconTrash, IconX } from './icons'

/**
 * Choosing your own profile picture.
 *
 * Two ways in, because they are genuinely different acts on a phone: the camera, which most people
 * mean, and the gallery, for the picture they already like of themselves. Both go through the same
 * file input — `capture="user"` is the only difference, and on a desktop browser it is ignored, which
 * is the right fallback rather than a broken button.
 *
 * The picture is squared and shrunk on the device before it is sent (lib/avatar.ts), so what leaves
 * the phone is ~10 KB rather than a four-megapixel camera original over a rural uplink.
 */
export function AvatarPickerSheet({ onClose, onChanged }: { onClose: () => void; onChanged: () => void }) {
  const { employeeId } = useAuth()
  const cameraRef = useRef<HTMLInputElement | null>(null)
  const galleryRef = useRef<HTMLInputElement | null>(null)

  const [preview, setPreview] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function onPicked(file: File | undefined) {
    if (!file) return
    setError(null)
    try {
      setPreview(await fileToSquareJpeg(file))
    } catch {
      // A HEIC the browser cannot decode, or a file that is not an image at all. Say which, because
      // "try again" on the same file would fail the same way.
      setError('Bu fayl şəkil kimi açılmadı — başqasını seçin')
    }
  }

  async function save() {
    if (!preview || !employeeId) return
    setBusy(true)
    setError(null)
    try {
      const { status, data } = await setMyAvatar(stripDataUrl(preview))
      if (status !== 200 || !data || !('ok' in data)) {
        setError('Şəkil yüklənmədi — yenidən cəhd edin')
        return
      }
      // Cached against the stamp the server just gave back, so nothing re-downloads a picture this
      // device already has.
      putAvatar(employeeId, preview, data.avatarUpdatedAtUtc)
      onChanged()
      onClose()
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!employeeId) return
    setBusy(true)
    setError(null)
    try {
      const { status } = await deleteMyAvatar()
      if (status !== 200) {
        setError('Silinmədi — yenidən cəhd edin')
        return
      }
      dropAvatar(employeeId)
      onChanged()
      onClose()
    } catch {
      setError('Serverə qoşulmaq mümkün olmadı')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-slate-900/50 backdrop-blur-sm" onClick={busy ? undefined : onClose} />
      <div className="absolute inset-x-0 bottom-0 max-h-[92vh] overflow-y-auto rounded-t-3xl bg-white p-5 pb-[max(2rem,env(safe-area-inset-bottom))] shadow-2xl">
        <div className="mx-auto mb-4 h-1 w-10 rounded-full bg-slate-200" />
        <div className="mb-3 font-bold text-slate-900">Profil şəkli</div>

        {error && (
          <div className="fb fb-err mb-3">
            <IconX />
            <span>{error}</span>
          </div>
        )}

        {preview ? (
          <div className="flex flex-col items-center gap-4">
            <img src={preview} alt="" className="h-32 w-32 rounded-full object-cover" />
            <div className="flex w-full gap-2">
              <button type="button" disabled={busy} onClick={() => setPreview(null)} className="btn btn-bl flex-1">
                Başqasını seç
              </button>
              <button type="button" disabled={busy} onClick={save} className="btn btn-primary btn-bl flex-1">
                {busy ? 'Yüklənir…' : 'Yadda saxla'}
              </button>
            </div>
          </div>
        ) : (
          <>
            <div className="mb-4 flex justify-center">
              <Avatar employeeId={employeeId} name={null} size={96} />
            </div>

            <div className="flex flex-col">
              <button
                type="button"
                onClick={() => cameraRef.current?.click()}
                className="flex items-center gap-3 rounded-2xl p-3 text-left font-semibold text-slate-800 transition active:bg-slate-50"
              >
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-slate-100">
                  <IconCamera className="h-5 w-5 text-slate-600" />
                </span>
                Şəkil çək
              </button>

              <button
                type="button"
                onClick={() => galleryRef.current?.click()}
                className="flex items-center gap-3 rounded-2xl p-3 text-left font-semibold text-slate-800 transition active:bg-slate-50"
              >
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-slate-100 text-lg">
                  🖼️
                </span>
                Qalereyadan seç
              </button>

              <button
                type="button"
                disabled={busy}
                onClick={remove}
                className="flex items-center gap-3 rounded-2xl p-3 text-left font-semibold text-red-600 transition active:bg-slate-50"
              >
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-red-50">
                  <IconTrash className="h-5 w-5" />
                </span>
                Şəkli sil
              </button>
            </div>

            <button onClick={onClose} className="mt-1 w-full py-2 text-sm text-slate-400">
              Bağla
            </button>
          </>
        )}

        {/* Two inputs rather than one: `capture` cannot be toggled reliably after the element exists,
            and a phone that ignores it simply opens its picker, which is the right fallback. */}
        <input
          ref={cameraRef}
          type="file"
          accept="image/*"
          capture="user"
          hidden
          onChange={(e) => void onPicked(e.target.files?.[0])}
        />
        <input
          ref={galleryRef}
          type="file"
          accept="image/*"
          hidden
          onChange={(e) => void onPicked(e.target.files?.[0])}
        />
      </div>
    </div>
  )
}
