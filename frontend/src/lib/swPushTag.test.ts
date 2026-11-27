import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { runInNewContext } from 'node:vm'
import { describe, expect, it } from 'vitest'

/**
 * The service worker's push handler, run as the browser runs it (public/sw.js is a classic script, not a module): which
 * tag a push gets decides what it replaces on the lock screen. The attendance feed must never replace a staff alert or
 * the person's own check-out reminder — and every push that existed before the feed keeps its old topic.
 */
function showPush(data: Record<string, unknown>) {
  const handlers: Record<string, (e: unknown) => void> = {}
  const shown: Array<{ title: string; options: { tag: string } }> = []
  const self = {
    addEventListener: (type: string, fn: (e: unknown) => void) => {
      handlers[type] = fn
    },
    registration: {
      showNotification: (title: string, options: { tag: string }) => {
        shown.push({ title, options })
        return Promise.resolve()
      },
    },
    location: { origin: 'https://app.test' },
    clients: { claim: () => Promise.resolve(), matchAll: () => Promise.resolve([]) },
    skipWaiting: () => {},
  }
  const code = readFileSync(fileURLToPath(new URL('../../public/sw.js', import.meta.url)), 'utf8')
  runInNewContext(code, { self, caches: {}, fetch: () => Promise.reject(new Error('offline')), URL, Response })
  handlers.push({ data: { json: () => data, text: () => '' }, waitUntil: () => {} })
  return shown[0].options.tag
}

describe('sw.js push tags', () => {
  it('puts the attendance feed in a topic of its own', () => {
    expect(showPush({ title: 'İşçi giriş etdi', url: '/admin/employees/e1' })).toBe('qrlog-attendance')
    expect(showPush({ title: 'Davamiyyət bildirişləri', url: '/admin/notifications' })).toBe('qrlog-attendance')
  })

  it('leaves a staff alert, an approval and the check-out reminder where they always were', () => {
    expect(showPush({ title: 'X', url: '/admin/problems' })).toBe('qrlog-checkout')
    expect(showPush({ title: 'X', url: '/admin/device-changes' })).toBe('qrlog-checkout')
    expect(showPush({ title: 'X', url: '/admin' })).toBe('qrlog-checkout')
    expect(showPush({ title: 'Çıxışı unutmayın', url: '/scan' })).toBe('qrlog-checkout')
    expect(showPush({ title: 'Elan', url: '/home' })).toBe('qrlog-checkout')
  })

  it('honours a tag the server names', () => {
    expect(showPush({ title: 'X', url: '/admin/employees/e1', tag: 'custom' })).toBe('custom')
  })
})
