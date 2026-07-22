/**
 * "Is there a face in this photo at all?" — asked on the phone, before the selfie is sent.
 *
 * This is deliberately NOT face recognition. It never asks who the person is, nothing leaves the
 * device, and it works with no connection (so an offline check-in gets the same warning). Deciding
 * whether the face is the right one stays on the server, where it already happens.
 *
 * Uses the browser's built-in FaceDetector where it exists (Chrome on Android — most of the phones
 * this runs on) and reports 'unknown' everywhere else rather than guessing. A wrong "your face isn't
 * visible" on a photo that plainly shows a face would train people to ignore the warning, which is
 * worse than not warning at all. Devices without it are still covered: the server flags the check-in
 * and the next scan warns before the camera opens.
 *
 * Brightness or uniformity heuristics were considered and rejected — they catch a pocket shot but
 * pass a photo of a gate or a QR poster, which is exactly what people actually point the camera at.
 */

type FaceCheck = 'face' | 'noface' | 'unknown'

interface FaceDetectorLike {
  detect(image: ImageBitmapSource): Promise<unknown[]>
}

type FaceDetectorCtor = new (options?: { fastMode?: boolean; maxDetectedFaces?: number }) => FaceDetectorLike

function detectorCtor(): FaceDetectorCtor | null {
  const ctor = (window as unknown as { FaceDetector?: FaceDetectorCtor }).FaceDetector
  return typeof ctor === 'function' ? ctor : null
}

export function faceCheckSupported(): boolean {
  return detectorCtor() !== null
}

/** Resolves 'unknown' on any failure — this check may never stand between someone and their check-in. */
export async function checkForFace(dataUrl: string): Promise<FaceCheck> {
  const ctor = detectorCtor()
  if (!ctor) return 'unknown'

  try {
    const blob = await (await fetch(dataUrl)).blob()
    const bitmap = await createImageBitmap(blob)
    try {
      // fastMode: a yes/no answer in a fraction of a second beats an accurate one nobody waits for.
      const faces = await new ctor({ fastMode: true, maxDetectedFaces: 1 }).detect(bitmap)
      return faces.length > 0 ? 'face' : 'noface'
    } finally {
      bitmap.close?.()
    }
  } catch {
    return 'unknown'
  }
}
