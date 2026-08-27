import { describe, expect, it } from 'vitest'
import { CAMERA_FAIL_REASON, cameraFailKind, type CameraFailKind } from './CameraHelp'

/**
 * Naming the reason a camera would not open.
 *
 * This used to be one answer for every failure: 'denied'. An employee at Dədə Qorqud was told eight
 * times in seventy minutes to grant a permission she had already granted, never checked in, and was
 * marked absent for a day she worked. The failing behaviour was not a crash — it was a confident
 * wrong answer, which is why the cases below assert the SPECIFIC kind and, above all, that an
 * unrecognised error comes back as 'unknown' rather than as the most likely guess.
 *
 * The strings are what browsers and html5-qrcode actually throw: DOMException-shaped objects on
 * Chrome and Safari, bare strings from the scanner library, and a plain TypeError when a chunk fetch
 * fails on a weak connection.
 */

/** A DOMException as the browser hands it over. */
const domError = (name: string, message = '') => ({ name, message })

describe('a permission refusal', () => {
  it('is read from the error name', () => {
    expect(cameraFailKind(domError('NotAllowedError', 'Permission denied'))).toBe('denied')
    expect(cameraFailKind(domError('PermissionDeniedError'))).toBe('denied')
  })

  it('is read from a bare string too — html5-qrcode rejects with one', () => {
    expect(cameraFailKind('NotAllowedError: Permission denied')).toBe('denied')
  })
})

describe('a camera held by something else', () => {
  it('is not reported as a permission problem', () => {
    // The distinction that matters most in practice: no settings screen fixes this one, and telling
    // someone to go and find one is how an hour disappears.
    expect(cameraFailKind(domError('NotReadableError', 'Could not start video source'))).toBe('inuse')
    expect(cameraFailKind(domError('TrackStartError'))).toBe('inuse')
    expect(cameraFailKind(domError('AbortError', 'Starting videoinput failed'))).toBe('inuse')
  })
})

describe('a device with no camera', () => {
  it('is named, so the answer can be "use another phone"', () => {
    expect(cameraFailKind(domError('NotFoundError'))).toBe('notfound')
    expect(cameraFailKind(domError('DevicesNotFoundError'))).toBe('notfound')
  })
})

describe('a scanner that never loaded', () => {
  it('is a connection problem, not a camera one', () => {
    // The QR library is imported on demand inside the same try as the camera, so this lands in the
    // camera catch. Reported as a camera fault it sends someone to fix a permission that is fine.
    expect(cameraFailKind(new TypeError('Failed to fetch dynamically imported module: /assets/x.js')))
      .toBe('loadfailed')
    expect(cameraFailKind(new TypeError('error loading dynamically imported module')))
      .toBe('loadfailed')
  })
})

describe('an error nobody recognises', () => {
  it('is unknown — NOT denied', () => {
    // The regression this whole file exists for. If this ever goes back to 'denied', the screen
    // resumes asserting a cause it does not know, and the person it is wrong about has no way to tell.
    expect(cameraFailKind(domError('WeirdVendorError', 'something went wrong'))).toBe('unknown')
    expect(cameraFailKind(new Error('boom'))).toBe('unknown')
    expect(cameraFailKind(null)).toBe('unknown')
    expect(cameraFailKind(undefined)).toBe('unknown')
    expect(cameraFailKind({})).toBe('unknown')
  })
})

describe('the reason filed against the employee', () => {
  it('is distinct for every kind', () => {
    const kinds: CameraFailKind[] = ['denied', 'inuse', 'notfound', 'insecure', 'loadfailed', 'unknown']
    const reasons = kinds.map((k) => CAMERA_FAIL_REASON[k])
    expect(new Set(reasons).size).toBe(kinds.length)
    expect(reasons.every(Boolean)).toBe(true)
  })

  it('matches the server allow-list exactly', () => {
    // reportScanFailure is an allow-list on the server: a reason it does not know is a 4xx, and the
    // report is dropped rather than queued — the failure would be invisible again, which is the exact
    // silence this change is undoing. Mirrors ClientFailureReasons in AttendanceController.cs.
    expect(Object.values(CAMERA_FAIL_REASON).sort()).toEqual(
      ['CameraDenied', 'CameraFailed', 'CameraInUse', 'CameraInsecure', 'CameraNotFound', 'ScannerLoadFailed'],
    )
  })
})
