import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'

/** Where the person was about to go when they were asked. */
export type LeaveTarget = { to: string } | { back: true }

const MARK = '__leaveGuard'

function onSpareEntry(): boolean {
  const state = window.history.state as Record<string, unknown> | null
  return !!state && state[MARK] === true
}

/**
 * Keeps a person on a page that has unsaved changes until they say what to do with them.
 *
 * The app runs on <BrowserRouter>, and react-router's useBlocker works only under a data router, so the three ways off
 * the page are caught one by one:
 *   • a reload or a closed tab — the browser's own «leave this site?» (beforeunload; its wording is the browser's);
 *   • a link inside the app — the tab bar, «Profil», the side menu — caught before the router sees the click;
 *   • the back button, or Android's back gesture — while there are changes a spare history entry is kept, so «back»
 *     lands on it and is asked about before anything is lost.
 * The question is the page's own: `pending` says where they were going, `stay()` keeps them, `leave()` goes on.
 * Navigation the code starts itself (navigate()) is not stopped — only what the person does.
 */
export function useLeaveGuard(dirty: boolean) {
  const navigate = useNavigate()
  const [pending, setPending] = useState<LeaveTarget | null>(null)
  const pendingRef = useRef<LeaveTarget | null>(null)
  pendingRef.current = pending
  const dirtyRef = useRef(dirty)
  dirtyRef.current = dirty
  // A spare entry was pushed and not yet taken back.
  const armed = useRef(false)
  // The next popstate is our own going back, not the person's.
  const releasing = useRef(false)

  useEffect(() => {
    if (!dirty) return
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      e.preventDefault()
      e.returnValue = ''
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [dirty])

  useEffect(() => {
    if (!dirty) return
    const onClick = (e: MouseEvent) => {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return
      const link = (e.target as Element | null)?.closest?.('a[href]') as HTMLAnchorElement | null
      if (!link || (link.target && link.target !== '_self') || link.hasAttribute('download')) return
      const url = new URL(link.href, window.location.href)
      if (url.origin !== window.location.origin) return
      const to = url.pathname + url.search + url.hash
      if (to === window.location.pathname + window.location.search + window.location.hash) return
      e.preventDefault()
      e.stopPropagation()
      setPending({ to })
    }
    // Capture, on the document: before React's own listener on the root, so the router never starts the navigation.
    document.addEventListener('click', onClick, true)
    return () => document.removeEventListener('click', onClick, true)
  }, [dirty])

  useEffect(() => {
    if (!dirty) {
      // Saved or undone: take the spare entry back if we are still standing on it, so «back» is one press again.
      if (armed.current && onSpareEntry()) {
        releasing.current = true
        window.history.back()
      }
      armed.current = false
      return
    }
    if (!armed.current) {
      window.history.pushState({ ...(window.history.state ?? {}), [MARK]: true }, '', window.location.href)
      armed.current = true
    }
    const onPop = () => {
      if (releasing.current) {
        releasing.current = false
        return
      }
      if (!dirtyRef.current || !armed.current) return
      // «Back» stepped off the spare entry (same address, so the page stays): put it back and ask.
      window.history.pushState({ ...(window.history.state ?? {}), [MARK]: true }, '', window.location.href)
      setPending({ back: true })
    }
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [dirty])

  const stay = useCallback(() => setPending(null), [])

  const leave = useCallback(() => {
    const target = pendingRef.current
    setPending(null)
    if (!target) return
    const spare = armed.current && onSpareEntry()
    armed.current = false
    if ('to' in target) {
      // In place of the spare entry, so «back» from there returns here in one press.
      navigate(target.to, { replace: spare })
    } else {
      releasing.current = true
      window.history.go(spare ? -2 : -1)
    }
  }, [navigate])

  return { pending, stay, leave }
}
