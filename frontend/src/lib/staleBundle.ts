/**
 * Two small rules for a phone still running a bundle from before the last deploy.
 *
 * An installed PWA keeps whatever it launched with, and an employee who works with no signal can stay
 * on that copy for days. The build's own files are named by content hash, so once a deploy lands, the
 * chunks the old page asks for are gone: the QR scanner would not load for Sərdar Hüseynov on 23.09 —
 * three failures in ten minutes and a check-out typed in by hand.
 */

/**
 * Remember a dynamic import — but never its failure. A rejected promise left in the cache is answered
 * again, from memory, with no network touched: every retry fails identically for as long as the page
 * lives. That is what turned one bad fetch into an evening of «Skan proqramı yüklənmədi».
 */
export function memoizeModule<T>(load: () => Promise<T>): () => Promise<T> {
  let cached: Promise<T> | null = null
  return () => {
    if (!cached) {
      cached = load().catch((err) => {
        cached = null
        throw err
      })
    }
    return cached
  }
}

/**
 * May this tab reload itself for build `buildId`? True at most once per build, so a reload that lands
 * on the same stale copy again (a cached index.html would do it) cannot spin.
 *
 * Storage may throw or be empty in a private window — then the answer is no, and the caller falls back
 * to whatever it does without a reload. Never reload blindly: a scan mid-flight holds a selfie and a
 * position that only exist in memory.
 */
export function mayReloadOnce(key: string, buildId: string, storage: Storage | undefined = safeSession()): boolean {
  if (!storage) return false
  try {
    if (storage.getItem(key) === buildId) return false
    storage.setItem(key, buildId)
    return true
  } catch {
    return false
  }
}

function safeSession(): Storage | undefined {
  try {
    return typeof sessionStorage === 'undefined' ? undefined : sessionStorage
  } catch {
    return undefined
  }
}
