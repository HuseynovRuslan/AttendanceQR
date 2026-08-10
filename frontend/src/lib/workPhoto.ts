// İŞ ŞƏKLİ capture helpers — a photo of the WORK (a swept yard, an emptied bin), taken with the REAR
// camera at check-out. Deliberately separate from the scan page's selfie helpers: no centre crop, no
// mirroring, no face framing. What is shared is the hard-won iOS Safari knowledge, repeated here
// because getting any of it wrong produces a black rectangle or a silently oversized upload:
//
//   • JPEG, never WebP — Safari's canvas.toBlob('image/webp') resolves null.
//   • Wait for a real frame — videoWidth stays 0 for a moment after play(), and a canvas drawn then
//     is blank.
//   • One camera at a time — iOS allows a single active capture, so the caller must stop tracks.

/** Waits until the element actually has pixels (or gives up), mirroring ScanPage's guard. */
export function waitForVideoFrame(video: HTMLVideoElement, timeoutMs = 2500): Promise<void> {
  return new Promise((resolve) => {
    const start = performance.now()
    const tick = () => {
      if (video.videoWidth > 0) {
        setTimeout(resolve, 200) // a frame or two of settle, or the first shot is dark
        return
      }
      if (performance.now() - start >= timeoutMs) {
        resolve()
        return
      }
      requestAnimationFrame(tick)
    }
    tick()
  })
}

function encode(canvas: HTMLCanvasElement, quality: number): Promise<string | null> {
  return new Promise((resolve) =>
    canvas.toBlob(
      (blob) => {
        if (!blob) {
          resolve(null)
          return
        }
        const reader = new FileReader()
        reader.onload = () => resolve(typeof reader.result === 'string' ? reader.result : null)
        reader.onerror = () => resolve(null)
        reader.readAsDataURL(blob)
      },
      'image/jpeg',
      quality,
    ),
  )
}

function drawScaled(source: CanvasImageSource, w: number, h: number, maxDim: number): HTMLCanvasElement {
  const scale = Math.min(1, maxDim / Math.max(w, h))
  const canvas = document.createElement('canvas')
  canvas.width = Math.round(w * scale)
  canvas.height = Math.round(h * scale)
  canvas.getContext('2d')?.drawImage(source, 0, 0, canvas.width, canvas.height)
  return canvas
}

/** Base64 inflates by ~33%, so this is the size of what actually goes over the wire. */
const MAX_DATA_URL = 1_400_000

/** Re-encodes once at a smaller size if the first pass is too heavy for a rural uplink. */
async function encodeWithinBudget(source: CanvasImageSource, w: number, h: number): Promise<string | null> {
  const first = await encode(drawScaled(source, w, h, 1280), 0.72)
  if (!first || first.length <= MAX_DATA_URL) return first
  return encode(drawScaled(source, w, h, 1024), 0.55)
}

/** Grabs the current frame — the WHOLE frame, uncropped and unmirrored: the evidence is the scene. */
export async function frameToWorkPhoto(video: HTMLVideoElement): Promise<string | null> {
  const w = video.videoWidth
  const h = video.videoHeight
  if (!w || !h) return null
  return encodeWithinBudget(video, w, h)
}

/** The fallback path: a file from the native camera app, re-encoded to the same budget. */
export async function fileToWorkPhoto(file: File): Promise<string | null> {
  const url = URL.createObjectURL(file)
  try {
    const img = await new Promise<HTMLImageElement | null>((resolve) => {
      const el = new Image()
      el.onload = () => resolve(el)
      el.onerror = () => resolve(null)
      el.src = url
    })
    if (!img) return null
    return await encodeWithinBudget(img, img.naturalWidth, img.naturalHeight)
  } finally {
    URL.revokeObjectURL(url)
  }
}
