// A tiny IndexedDB queue for scans made with no connection. Each item is one check-in/out tap that
// couldn't reach the server; the sync drainer (offlineSync.ts) replays them when the app is online.
// Kept dependency-free and separate from the device-id store so neither can corrupt the other.

const DB_NAME = 'qrlog-offline'
const STORE = 'scans'
const VERSION = 1

/** One queued scan — everything the /api/attendance/scan call needs, captured at tap time. */
export interface QueuedScan {
  clientScanId: string
  qrToken: string
  deviceFingerprint: string
  latitude: number
  longitude: number
  photoBase64?: string
  /** The phone's clock (ISO) when the scan was taken — the server uses this as the record time. */
  clientTimestampUtc: string
  queuedAtMs: number
}

/** Fired on the window whenever the queue changes, so any badge can refresh its count. */
export const QUEUE_CHANGED = 'qrlog:queue-changed'

function notifyChanged() {
  if (typeof window !== 'undefined') window.dispatchEvent(new Event(QUEUE_CHANGED))
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, VERSION)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(STORE)) {
        req.result.createObjectStore(STORE, { keyPath: 'clientScanId' })
      }
    }
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

export async function enqueueScan(item: QueuedScan): Promise<void> {
  const db = await openDb()
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction(STORE, 'readwrite')
    tx.objectStore(STORE).put(item)
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error)
  })
  db.close()
  notifyChanged()
}

export async function allScans(): Promise<QueuedScan[]> {
  const db = await openDb()
  const items = await new Promise<QueuedScan[]>((resolve, reject) => {
    const req = db.transaction(STORE, 'readonly').objectStore(STORE).getAll()
    req.onsuccess = () => resolve(req.result as QueuedScan[])
    req.onerror = () => reject(req.error)
  })
  db.close()
  // Oldest first — replay in the order the taps happened.
  return items.sort((a, b) => a.queuedAtMs - b.queuedAtMs)
}

export async function removeScan(clientScanId: string): Promise<void> {
  const db = await openDb()
  await new Promise<void>((resolve, reject) => {
    const tx = db.transaction(STORE, 'readwrite')
    tx.objectStore(STORE).delete(clientScanId)
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error)
  })
  db.close()
  notifyChanged()
}

export async function scanCount(): Promise<number> {
  try {
    const db = await openDb()
    const n = await new Promise<number>((resolve, reject) => {
      const req = db.transaction(STORE, 'readonly').objectStore(STORE).count()
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
    db.close()
    return n
  } catch {
    return 0
  }
}
