// Checklist ticks that have not been acknowledged by the server yet.
//
// Ticking must feel like flipping a switch, not like submitting a form: the worker is standing in a
// yard with one thumb and a bad signal. So the tap flips local state immediately and the POST is
// fire-and-forget — but a tick that never reached the server must NOT quietly reappear as unticked
// when the screen reloads, because the worker already believes it is done. That is what this is for.
//
// It is a convenience layer, not the safety net: the real guarantee is that the check-out carries the
// ABSOLUTE set of ticked ids, so even a whole afternoon of failed ticks lands with the departure.

const KEY = 'qrlog:fieldtasks'

type PendingMap = Record<string, Record<string, boolean>>

function readAll(): PendingMap {
  try {
    const raw = localStorage.getItem(KEY)
    return raw ? (JSON.parse(raw) as PendingMap) : {}
  } catch {
    return {}
  }
}

function writeAll(map: PendingMap): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(map))
  } catch {
    /* a full or disabled storage must never break ticking */
  }
}

/** The unacknowledged ticks for one visit, as {itemId: isDone}. */
export function readPendingTicks(visitId: string): Record<string, boolean> {
  return readAll()[visitId] ?? {}
}

/** Records a tick as pending; call again with `acknowledged` once the server confirms it. */
export function writePendingTick(visitId: string, itemId: string, isDone: boolean, acknowledged = false): void {
  const all = readAll()
  const forVisit = { ...(all[visitId] ?? {}) }
  if (acknowledged) delete forVisit[itemId]
  else forVisit[itemId] = isDone
  if (Object.keys(forVisit).length === 0) delete all[visitId]
  else all[visitId] = forVisit
  writeAll(all)
}

/** Drops a finished visit's entries — its ticks are settled by the check-out payload. */
export function clearPendingTicks(visitId: string): void {
  const all = readAll()
  if (!(visitId in all)) return
  delete all[visitId]
  writeAll(all)
}
