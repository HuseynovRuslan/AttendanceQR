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

/** POST /api/assistant/transcribe — a short voice clip → text (az/ru auto-detected server-side). */
export async function transcribeVoice(blob: Blob) {
  const audioBase64 = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    // result is "data:audio/webm;codecs=opus;base64,AAAA…" — send only the payload half.
    reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '')
    reader.onerror = () => reject(reader.error)
    reader.readAsDataURL(blob)
  })
  return apiRequest<{ text: string } | { error: string }>('/api/assistant/transcribe', {
    method: 'POST',
    body: { audioBase64, mimeType: blob.type || 'audio/webm' },
  })
}
