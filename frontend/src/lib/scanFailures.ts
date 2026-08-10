// A scan that never became a record — the camera wouldn't open, GPS refused, or the request never
// reached the server — is invisible to the admin unless the phone says so. reportFailure sends it
// now; if the phone is offline (the very reason it often fails), it is kept and flushed on the next
// connection, so "I couldn't check out" shows up on the Problems screen instead of only as a phone call.
import { reportScanFailure } from '../api/attendance'

const KEY = 'attendanceqr.pendingFailures'
interface Pending {
  reason: string
  accuracy?: number
  /** When the scan was taken, for an offline report sent days later — the lost DAY. */
  scanAtUtc?: string
}

function readQueue(): Pending[] {
  try {
    const raw = localStorage.getItem(KEY)
    return raw ? (JSON.parse(raw) as Pending[]) : []
  } catch {
    return []
  }
}

function writeQueue(q: Pending[]): void {
  // Keep the last 20 only — one stuck phone shouldn't grow this without bound.
  try {
    localStorage.setItem(KEY, JSON.stringify(q.slice(-20)))
  } catch {
    /* private mode — best effort */
  }
}

/** Report a client-side scan failure. Fire-and-forget; queued for retry only on a network error
 *  (a 4xx like an unknown reason will never succeed, so it isn't kept). */
export function reportFailure(reason: string, accuracy?: number, scanAtUtc?: string): void {
  // apiRequest RESOLVES for every HTTP status — it only rejects on a transport failure or a
  // non-JSON body. So a 401 mid-drain, a 403 or a 500 used to lose the report entirely while this
  // .catch never ran. Queue on any non-2xx as well.
  reportScanFailure(reason, accuracy, scanAtUtc)
    .then((r) => {
      if (r.status < 200 || r.status >= 300) writeQueue([...readQueue(), { reason, accuracy, scanAtUtc }])
    })
    .catch(() => writeQueue([...readQueue(), { reason, accuracy, scanAtUtc }]))
}

/** Send anything queued while offline. Call on app/scan-screen load; keeps whatever still won't send. */
export async function flushFailures(): Promise<void> {
  const q = readQueue()
  if (q.length === 0) return
  const remaining: Pending[] = []
  for (const p of q) {
    try {
      const res = await reportScanFailure(p.reason, p.accuracy, p.scanAtUtc)
      // The endpoint answers 202 Accepted, not 200. Testing for 200 kept every report in this queue
      // FOREVER, re-sent on every scan-screen open — and once past the 5-minute dedupe each one minted
      // a fresh blocking Problems row for an incident that had already been recorded.
      if (res.status < 200 || res.status >= 300) remaining.push(p) // transient — try again next time
    } catch {
      remaining.push(p) // still offline
    }
  }
  writeQueue(remaining)
}
