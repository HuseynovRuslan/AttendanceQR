const DEVICE_KEY = 'attendanceqr.device'
const IDB_NAME = 'attendanceqr'
const IDB_STORE = 'kv'

/**
 * The stable per-device fingerprint the backend binds a device to — a random UUID created once and
 * reused. The same value is sent at activation (binds the device) and at every scan (must match).
 *
 * The catch is durability. localStorage is script-writable storage, which iOS Safari EVICTS after
 * ~7 days of no first-party interaction (ITP), and which is per-browser-context. When it is wiped, a
 * new UUID is minted and the employee reads as a brand-new device — the "Cihaz uyğun deyil" churn we
 * saw across ~40% of staff. Two defences, both in initDevice():
 *   1) navigator.storage.persist() — ask the browser NOT to evict our storage.
 *   2) mirror the id into IndexedDB and self-heal from it if localStorage alone was cleared.
 * The installed PWA (standalone) gets durable storage and is the real fix; the app also nudges
 * employees to add it to the home screen (see InstallHint).
 *
 * getDeviceFingerprint stays SYNCHRONOUS and reads localStorage, so every existing caller is
 * unchanged — and an id already present is NEVER replaced, so no one is re-bound by this change.
 */
export function getDeviceFingerprint(): string {
  let id = localStorage.getItem(DEVICE_KEY)
  if (!id) {
    id = crypto.randomUUID()
    localStorage.setItem(DEVICE_KEY, id)
    void idbSet(DEVICE_KEY, id).catch(() => {})
  }
  return id
}

/**
 * Run once at boot, before any scan. Requests persistent storage and reconciles localStorage with the
 * IndexedDB mirror: localStorage stays authoritative (so existing ids are untouched), IndexedDB is
 * kept in sync with it, and if localStorage was cleared while IndexedDB survived, the id is restored.
 * Entirely best-effort — any failure just falls back to the plain localStorage behaviour.
 */
export async function initDevice(): Promise<void> {
  try {
    // Ask the browser to exempt our storage from eviction. Granted freely on installed PWAs and on
    // Chrome/Android; a no-op elsewhere. Never hurts.
    if (navigator.storage?.persist) {
      await navigator.storage.persist().catch(() => {})
    }

    const ls = localStorage.getItem(DEVICE_KEY)
    const idb = await idbGet(DEVICE_KEY).catch(() => null)

    if (ls) {
      // localStorage is the source of truth; keep the mirror aligned to it.
      if (idb !== ls) await idbSet(DEVICE_KEY, ls).catch(() => {})
    } else if (idb) {
      // localStorage was wiped but the mirror lived — recover the SAME id, no re-bind.
      localStorage.setItem(DEVICE_KEY, idb)
    } else {
      // Genuinely first run on this context.
      const fresh = crypto.randomUUID()
      localStorage.setItem(DEVICE_KEY, fresh)
      await idbSet(DEVICE_KEY, fresh).catch(() => {})
    }
  } catch {
    /* best-effort — getDeviceFingerprint still works on plain localStorage */
  }
}

/** True when the app runs as an installed PWA (its storage is durable, immune to ITP eviction). */
export function isStandalone(): boolean {
  const iosStandalone = (navigator as unknown as { standalone?: boolean }).standalone === true
  return iosStandalone || window.matchMedia?.('(display-mode: standalone)').matches === true
}

// --- minimal IndexedDB key/value (no dependency) ---------------------------------------------------

function openIdb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(IDB_NAME, 1)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(IDB_STORE)) req.result.createObjectStore(IDB_STORE)
    }
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

function idbGet(key: string): Promise<string | null> {
  return openIdb().then(
    (db) =>
      new Promise((resolve, reject) => {
        const tx = db.transaction(IDB_STORE, 'readonly').objectStore(IDB_STORE).get(key)
        tx.onsuccess = () => resolve((tx.result as string) ?? null)
        tx.onerror = () => reject(tx.error)
      }),
  )
}

function idbSet(key: string, value: string): Promise<void> {
  return openIdb().then(
    (db) =>
      new Promise((resolve, reject) => {
        const tx = db.transaction(IDB_STORE, 'readwrite').objectStore(IDB_STORE).put(value, key)
        tx.onsuccess = () => resolve()
        tx.onerror = () => reject(tx.error)
      }),
  )
}

/**
 * Best-effort human-readable device name from the User-Agent, captured once at activation and shown
 * in the admin employee list. Purely cosmetic — never used for any security decision.
 */
export function getFriendlyDeviceName(): string {
  const ua = navigator.userAgent
  if (/iPhone/.test(ua)) return 'iPhone'
  if (/iPad/.test(ua)) return 'iPad'
  if (/Samsung/.test(ua)) return 'Samsung Galaxy'
  if (/Xiaomi|MIUI/.test(ua)) return 'Xiaomi'
  if (/Huawei/.test(ua)) return 'Huawei'
  if (/Android/.test(ua)) return 'Android cihaz'
  if (/Windows/.test(ua)) return 'Windows PC'
  if (/Macintosh/.test(ua)) return 'Mac'
  return 'Naməlum cihaz'
}
