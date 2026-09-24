import { describe, expect, it, vi } from 'vitest'
import { mayReloadOnce, memoizeModule } from './staleBundle'

describe('a dynamic import that failed', () => {
  it('is tried again — the failure is not remembered', async () => {
    let calls = 0
    const load = memoizeModule(async () => {
      calls++
      if (calls === 1) throw new Error('Failed to fetch dynamically imported module')
      return 'qr-library'
    })

    await expect(load()).rejects.toThrow()
    await expect(load()).resolves.toBe('qr-library')
    expect(calls).toBe(2)
  })

  it('a successful one is fetched once, however many scans follow', async () => {
    const inner = vi.fn(async () => 'qr-library')
    const load = memoizeModule(inner)

    await Promise.all([load(), load(), load()])

    expect(inner).toHaveBeenCalledTimes(1)
  })
})

describe('reloading into the new build', () => {
  const store = () => {
    const m = new Map<string, string>()
    return {
      getItem: (k: string) => m.get(k) ?? null,
      setItem: (k: string, v: string) => void m.set(k, v),
      removeItem: (k: string) => void m.delete(k),
      clear: () => m.clear(),
      key: () => null,
      length: 0,
    } as unknown as Storage
  }

  it('happens once per build, so a reload cannot spin', () => {
    const s = store()
    expect(mayReloadOnce('k', 'build-2', s)).toBe(true)
    expect(mayReloadOnce('k', 'build-2', s)).toBe(false)
  })

  it('happens again for the NEXT build', () => {
    const s = store()
    mayReloadOnce('k', 'build-2', s)
    expect(mayReloadOnce('k', 'build-3', s)).toBe(true)
  })

  it('says no when storage is unavailable — a private window must not reload in a loop', () => {
    const broken = { getItem: () => { throw new Error('denied') } } as unknown as Storage
    expect(mayReloadOnce('k', 'build-2', broken)).toBe(false)
    expect(mayReloadOnce('k', 'build-2', undefined)).toBe(false)
  })
})
