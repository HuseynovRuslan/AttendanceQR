// PROFİL ŞƏKLİ — the picture an employee chooses for themselves.
//
// Not the check-in selfie and not the face-audit reference: nothing here is compared to anything, and
// none of it reaches the face-match worker. It exists because a crew phone holding thirty accounts
// shows thirty pairs of initials, and in Azerbaijani names those collide constantly — "Məmmədov
// Elçin" and "Məmmədov Elvin" are both ME. Tapping the wrong row files the wrong person's day.
//
// Two jobs: shrink a photo to something worth sending, and keep what came back so the list still has
// faces on it where there is no signal.

/**
 * The stored square, in CSS pixels. Displayed at 80px on the profile and 44px in the switcher, so
 * 192 covers a 2× screen with room to spare. It is deliberately small: thirty of these live in
 * localStorage on a shared handset, and a camera original would fill it several times over.
 */
const SIZE = 192
const QUALITY = 0.75

const CACHE_KEY = 'attendanceqr.avatars'

interface CachedAvatar {
  /** JPEG data URL, already squared and shrunk. */
  dataUrl: string
  /** The server's avatarUpdatedAtUtc this copy belongs to — how we know it went stale. */
  stamp: string
}

type Cache = Record<string, CachedAvatar>

function readCache(): Cache {
  try {
    const raw = localStorage.getItem(CACHE_KEY)
    return raw ? (JSON.parse(raw) as Cache) : {}
  } catch {
    return {}
  }
}

function writeCache(cache: Cache): void {
  try {
    localStorage.setItem(CACHE_KEY, JSON.stringify(cache))
  } catch {
    // Quota, or private browsing. Faces are a convenience; initials still identify well enough to
    // keep the app working, so this is never allowed to throw into a caller.
  }
}

/** The picture we hold for this employee, or null. */
export function cachedAvatar(employeeId: string | null | undefined): string | null {
  if (!employeeId) return null
  return readCache()[employeeId]?.dataUrl ?? null
}

/** True when what we hold is missing or belongs to an older version than the server's. */
export function avatarIsStale(employeeId: string, stamp: string | null | undefined): boolean {
  const held = readCache()[employeeId]
  if (!held) return true
  return held.stamp !== (stamp ?? '')
}

export function putAvatar(employeeId: string, dataUrl: string, stamp: string | null | undefined): void {
  const cache = readCache()
  cache[employeeId] = { dataUrl, stamp: stamp ?? '' }
  writeCache(cache)
}

export function dropAvatar(employeeId: string): void {
  const cache = readCache()
  delete cache[employeeId]
  writeCache(cache)
}

/**
 * Turns a chosen file into a centre-cropped square JPEG data URL.
 *
 * Centre crop rather than letterbox: a round frame shows the middle of the image whatever we do, so
 * fitting the whole photo inside the square only means the face ends up smaller and the corners are
 * cut off anyway. JPEG, not WebP — Safari's canvas.toBlob('image/webp') returns null, which is the
 * bug that once made iPhones silently take no check-in photo at all.
 */
export function fileToSquareJpeg(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file)
    const img = new Image()
    img.onload = () => {
      URL.revokeObjectURL(url)
      try {
        const side = Math.min(img.naturalWidth, img.naturalHeight)
        const canvas = document.createElement('canvas')
        canvas.width = SIZE
        canvas.height = SIZE
        const ctx = canvas.getContext('2d')
        if (!ctx) {
          reject(new Error('no-canvas'))
          return
        }
        ctx.drawImage(
          img,
          (img.naturalWidth - side) / 2,
          (img.naturalHeight - side) / 2,
          side,
          side,
          0,
          0,
          SIZE,
          SIZE,
        )
        resolve(canvas.toDataURL('image/jpeg', QUALITY))
      } catch (e) {
        reject(e instanceof Error ? e : new Error('encode-failed'))
      }
    }
    img.onerror = () => {
      URL.revokeObjectURL(url)
      reject(new Error('not-an-image'))
    }
    img.src = url
  })
}

/** The base64 payload without the `data:image/jpeg;base64,` prefix, which is what the API wants. */
export function stripDataUrl(dataUrl: string): string {
  const comma = dataUrl.indexOf(',')
  return comma >= 0 ? dataUrl.slice(comma + 1) : dataUrl
}
