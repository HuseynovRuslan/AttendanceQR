/**
 * "Is there a face in this photo at all?" — asked before the selfie is sent with the check-in.
 *
 * Deliberately NOT face recognition: it never asks who the person is. Deciding whether the face is
 * the RIGHT one stays in the background audit, where it already happens.
 *
 * The answer comes from the server. An on-device check was tried first and turned out to be a
 * no-op in practice — Chrome's FaceDetector is behind an experimental flag and Safari has nothing —
 * so it silently answered "unknown" on every real phone and nobody was ever warned. It is still
 * used when present, purely because it costs nothing and saves a round trip.
 *
 * Brightness or uniformity heuristics were considered and rejected — they catch a pocket shot but
 * pass a photo of a wall, a gate or a QR poster, which is exactly what people point the camera at.
 */

import { apiRequest } from '../api/client'

type FaceCheck = 'face' | 'noface' | 'unknown'

/** Someone is standing at the scanner waiting. Past this, skip the check rather than hold them up. */
const SERVER_CHECK_TIMEOUT_MS = 6000

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

/**
 * Resolves 'unknown' on any failure — this check may never stand between someone and their check-in.
 * Tries the device first (free, instant), then the server.
 */
export async function checkForFace(dataUrl: string): Promise<FaceCheck> {
  const local = await checkOnDevice(dataUrl)
  if (local !== 'unknown') return local
  // The endpoint accepts a data URL as-is, the same way the scan itself sends the photo.
  return await checkOnServer(dataUrl)
}

/** The server sees every photo the same way, on every phone. -1 means it could not tell. */
async function checkOnServer(photoBase64: string): Promise<FaceCheck> {
  try {
    const result = await Promise.race([
      apiRequest<{ faces: number }>('/api/attendance/me/photo-check', {
        method: 'POST',
        body: { photoBase64 },
      }),
      new Promise<null>((resolve) => setTimeout(() => resolve(null), SERVER_CHECK_TIMEOUT_MS)),
    ])
    if (!result || result.status !== 200 || !result.data || !('faces' in result.data)) return 'unknown'
    // Offline, disabled or a service error all arrive as -1 — never as zero. A network blip must
    // not accuse someone of hiding their face.
    return result.data.faces < 0 ? 'unknown' : result.data.faces === 0 ? 'noface' : 'face'
  } catch {
    return 'unknown'
  }
}

async function checkOnDevice(dataUrl: string): Promise<FaceCheck> {
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
