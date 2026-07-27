// A scan that never became a record — the camera wouldn't open, GPS refused, or the request never
// reached the server — is invisible to the admin unless the phone says so. reportFailure sends it
// now; if the phone is offline (the very reason it often fails), it is kept and flushed on the next
// connection, so "I couldn't check out" shows up on the Problems screen instead of only as a phone call.
import { reportScanFailure } from '../api/attendance'

const KEY = 'attendanceqr.pendingFailures'
interface Pending {
  reason: string
  accuracy?: number
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
export function reportFailure(reason: string, accuracy?: number): void {
  reportScanFailure(reason, accuracy).catch(() => writeQueue([...readQueue(), { reason, accuracy }]))
}

/** Send anything queued while offline. Call on app/scan-screen load; keeps whatever still won't send. */
export async function flushFailures(): Promise<void> {
  const q = readQueue()
  if (q.length === 0) return
  const remaining: Pending[] = []
  for (const p of q) {
    try {
      const res = await reportScanFailure(p.reason, p.accuracy)
      if (res.status !== 200) remaining.push(p) // transient server issue — try again next time
    } catch {
      remaining.push(p) // still offline
    }
  }
  writeQueue(remaining)
}
