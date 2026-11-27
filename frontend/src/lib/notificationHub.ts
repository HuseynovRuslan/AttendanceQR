import { API_BASE_URL } from '../api/client'
import { getStreamTicket, type CenterNotification } from '../api/notificationCenter'

/**
 * The live channel to the notification module (backend NotificationsHub at /hubs/notifications): an open panel hears
 * its own new notifications as they are written.
 *
 * - SignalR is imported only when somebody subscribes, so the employee screens never download it.
 * - It authenticates with a two-minute ticket from POST /api/notifications/stream-ticket, fetched again on every
 *   (re)connect — never the long-lived session JWT, which a WebSocket would carry in its URL.
 * - It reconnects on its own after a drop (0 s, 2 s, 5 s, 10 s, then every 30 s) and tells subscribers when it is back,
 *   so they can refetch what they may have missed. When the channel cannot be had at all (no ticket — a view-only or
 *   impersonated session — or the network refuses WebSockets and the fallbacks), the screens keep their own polling:
 *   live is an improvement, never a dependency.
 */
export type LiveItem = Pick<
  CenterNotification,
  'typeKey' | 'title' | 'body' | 'branchName' | 'occurredAtUtc' | 'wasOffline' | 'minutes' | 'url'
> & { id: string | null }

export interface LiveListener {
  onItems?: (items: LiveItem[]) => void
  /** The connection came back after a drop — refetch, something may have been missed. */
  onReconnected?: () => void
  onStatus?: (status: LiveStatus) => void
}

export type LiveStatus = 'connecting' | 'live' | 'reconnecting' | 'offline'

const listeners = new Set<LiveListener>()
let connection: { stop: () => Promise<void> } | null = null
let starting: Promise<void> | null = null
let status: LiveStatus = 'offline'
let retryTimer: ReturnType<typeof setTimeout> | null = null

function setStatus(next: LiveStatus) {
  status = next
  for (const l of listeners) l.onStatus?.(next)
}

export function liveStatus(): LiveStatus {
  return status
}

async function ticket(): Promise<string> {
  const { status: code, data } = await getStreamTicket()
  if (code !== 200 || !data?.ticket) throw new Error(`stream-ticket ${code}`)
  return data.ticket
}

async function start(): Promise<void> {
  if (connection || starting) return starting ?? Promise.resolve()
  starting = (async () => {
    setStatus('connecting')
    try {
      const signalR = await import('@microsoft/signalr')
      const conn = new signalR.HubConnectionBuilder()
        .withUrl(`${API_BASE_URL}/hubs/notifications`, {
          accessTokenFactory: ticket,
          // No cookies are used anywhere; the API's CORS policy does not allow credentials.
          withCredentials: false,
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000, 30000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build()

      conn.on('notifications', (payload: { items?: LiveItem[] }) => {
        const items = Array.isArray(payload?.items) ? payload.items : []
        if (items.length === 0) return
        for (const l of listeners) l.onItems?.(items)
      })
      conn.onreconnecting(() => setStatus('reconnecting'))
      conn.onreconnected(() => {
        setStatus('live')
        for (const l of listeners) l.onReconnected?.()
      })
      conn.onclose(() => {
        connection = null
        setStatus('offline')
        scheduleRetry()
      })

      await conn.start()
      connection = conn
      setStatus('live')
    } catch {
      connection = null
      setStatus('offline')
      scheduleRetry()
    } finally {
      starting = null
    }
  })()
  return starting
}

/** After the automatic reconnects gave up (or the first start failed): try again later, while anybody listens. */
function scheduleRetry() {
  if (retryTimer || listeners.size === 0) return
  retryTimer = setTimeout(() => {
    retryTimer = null
    if (listeners.size > 0) void start()
  }, 60_000)
}

/** Listen to the live channel; the connection opens with the first listener and closes with the last. */
export function subscribeLive(listener: LiveListener): () => void {
  listeners.add(listener)
  listener.onStatus?.(status)
  void start()
  return () => {
    listeners.delete(listener)
    if (listeners.size === 0) {
      if (retryTimer) clearTimeout(retryTimer)
      retryTimer = null
      const c = connection
      connection = null
      void c?.stop()
      setStatus('offline')
    }
  }
}
