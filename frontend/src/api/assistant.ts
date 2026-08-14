import { apiRequest } from './client'

/** One turn of the support chat, as the server wants it (system prompt is the server's own). */
export interface AssistantMessage {
  role: 'user' | 'assistant'
  content: string
}

export interface AssistantReply {
  reply: string
  /** Screen keys the model suggested (device-request | profile | history | scan) — rendered as buttons. */
  actions: string[]
  remainingToday: number
}

/** POST /api/assistant/chat — the whole history each time; the server is stateless on purpose. */
export function sendAssistantChat(messages: AssistantMessage[]) {
  return apiRequest<AssistantReply | { error: string }>('/api/assistant/chat', {
    method: 'POST',
    body: { messages },
  })
}
