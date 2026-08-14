import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { SubPageHeader } from '../components/SubPageHeader'
import { sendAssistantChat, transcribeVoice, type AssistantMessage } from '../api/assistant'

/**
 * «AI Köməkçi» — the support chat. The audience is a cleaner or a driver on a phone, so the design
 * rules are: big tap targets, no jargon, and the first screen already contains the four questions
 * people actually come with — most sessions should be one tap + one answer.
 *
 * Voice input costs nothing here: the Android keyboard's mic button dictates Azerbaijani into any
 * text field, so a plain <input> IS the voice interface.
 */

/** Quick-start chips — the top real support reasons, pre-phrased. */
const QUICK: string[] = [
  'Skanım alınmır, kömək et',
  'Telefonumu dəyişmişəm',
  'Bu ay neçə saat işləmişəm?',
  'Niyə günüm 0 saat sayılıb?',
]

/** Screen keys the server may suggest → button label + route. Must mirror the backend allowlist. */
const ACTIONS: Record<string, { label: string; to: string }> = {
  'device-request': { label: '📱 Yeni telefon tələbi göndər', to: '/device-change-request' },
  profile: { label: '👤 Profil / PIN', to: '/profile' },
  history: { label: '🕐 Skan tarixçəm', to: '/stats' },
  scan: { label: '📷 Skan et', to: '/scan' },
}

interface Bubble extends AssistantMessage {
  actions?: string[]
}

/** Can this browser record audio at all? (Old WebViews without RECORD_AUDIO say no — the mic button
 *  then simply isn't offered, which beats a button that always errors.) */
const voiceSupported =
  typeof navigator !== 'undefined' && !!navigator.mediaDevices?.getUserMedia && typeof MediaRecorder !== 'undefined'

/** ~30s cap: support questions are one sentence, and the server caps the payload anyway. */
const MAX_VOICE_MS = 30_000

export function HelpChatPage() {
  const navigate = useNavigate()
  const [messages, setMessages] = useState<Bubble[]>([])
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [recording, setRecording] = useState(false)
  const [transcribing, setTranscribing] = useState(false)
  const recorderRef = useRef<MediaRecorder | null>(null)
  const stopTimerRef = useRef<number | undefined>(undefined)
  const endRef = useRef<HTMLDivElement>(null)

  // Leaving the screen mid-recording must release the microphone — a page that keeps the mic light
  // on after it's closed is how an app loses trust for good.
  useEffect(() => () => stopRecorder(false), [])

  function stopRecorder(wantResult: boolean) {
    window.clearTimeout(stopTimerRef.current)
    const rec = recorderRef.current
    if (!rec) return
    recorderRef.current = null
    if (!wantResult) rec.ondataavailable = null
    if (rec.state !== 'inactive') rec.stop()
    rec.stream.getTracks().forEach((t) => t.stop())
    setRecording(false)
  }

  async function toggleVoice() {
    if (busy || transcribing) return
    if (recording) {
      stopRecorder(true)
      return
    }
    setNotice(null)
    let stream: MediaStream
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    } catch {
      setNotice('Mikrofon icazəsi verilmədi — telefonun parametrlərindən QRLog üçün mikrofonu açın.')
      return
    }
    // Chrome/Android records webm+opus; iOS Safari has neither and falls back to its native mp4.
    const mime = MediaRecorder.isTypeSupported('audio/webm;codecs=opus') ? 'audio/webm;codecs=opus'
      : MediaRecorder.isTypeSupported('audio/mp4') ? 'audio/mp4' : ''
    const rec = new MediaRecorder(stream, mime ? { mimeType: mime } : undefined)
    const chunks: Blob[] = []
    rec.ondataavailable = (e) => {
      if (e.data.size > 0) chunks.push(e.data)
    }
    rec.onstop = () => {
      stream.getTracks().forEach((t) => t.stop())
      if (chunks.length === 0) return
      void sendVoice(new Blob(chunks, { type: rec.mimeType || 'audio/webm' }))
    }
    recorderRef.current = rec
    rec.start()
    setRecording(true)
    stopTimerRef.current = window.setTimeout(() => stopRecorder(true), MAX_VOICE_MS)
  }

  async function sendVoice(blob: Blob) {
    setTranscribing(true)
    try {
      const { status, data } = await transcribeVoice(blob)
      if (status === 200 && data && 'text' in data && data.text.trim()) {
        // Straight into send: the audience can't comfortably edit text, and a wrong transcription
        // is fixed by speaking again, not by cursor work.
        await send(data.text.trim())
      } else if (status === 429) {
        setNotice('Bu günlük səsli sual limitiniz doldu — yazaraq soruşun.')
      } else {
        setNotice('Səs aydın alınmadı — bir daha cəhd edin və ya yazın.')
      }
    } catch {
      setNotice('İnternet bağlantısı yoxdur — qoşulanda yenidən cəhd edin.')
    } finally {
      setTranscribing(false)
    }
  }

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, busy])

  async function send(text: string) {
    const trimmed = text.trim()
    if (!trimmed || busy) return
    setNotice(null)
    setInput('')
    const history: Bubble[] = [...messages, { role: 'user', content: trimmed }]
    setMessages(history)
    setBusy(true)
    try {
      // The server only needs role+content — strip the client-side extras.
      const { status, data } = await sendAssistantChat(history.map(({ role, content }) => ({ role, content })))
      if (status === 200 && data && 'reply' in data) {
        setMessages([...history, { role: 'assistant', content: data.reply, actions: data.actions }])
        return
      }
      // Every failure keeps the user's message on screen — retyping it would be the real loss.
      if (status === 429) setNotice('Bu günlük sual limitiniz doldu — sabah yenidən yazın.')
      else if (status === 403) setNotice('Köməkçi sizin şirkət üçün aktiv deyil.')
      else if (status === 503) setNotice('Köməkçi hazırda qurulmayıb.')
      else setNotice('Cavab almaq mümkün olmadı — bir azdan yenidən cəhd edin.')
    } catch {
      setNotice('İnternet bağlantısı yoxdur — qoşulanda yenidən göndərin.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <SubPageHeader title="Süni intellekt köməkçisi" />

      <main className="mx-auto flex w-full max-w-md flex-1 flex-col gap-3 p-4 pb-32">
        {/* First open: say what this is and offer the four real questions. */}
        {messages.length === 0 && (
          <>
            <div className="rounded-3xl border border-slate-100 bg-white p-5 shadow-sm">
              <div className="text-3xl">💬</div>
              <h1 className="mt-2 text-lg font-bold text-slate-900">Nə kömək edə bilərəm?</h1>
              <p className="mt-1 text-sm text-slate-600">
                Skan, giriş-çıxış və iş saatlarınızla bağlı sualları cavablandırıram. Sualınızı yazın
                — və ya klaviaturadakı 🎤 düyməsi ilə deyin.
              </p>
            </div>
            <div className="flex flex-col gap-2">
              {QUICK.map((q) => (
                <button
                  key={q}
                  onClick={() => void send(q)}
                  className="rounded-2xl border border-blue-100 bg-white px-4 py-3 text-left text-sm font-semibold text-blue-700 shadow-sm transition active:scale-[0.99]"
                >
                  {q}
                </button>
              ))}
            </div>
          </>
        )}

        {messages.map((m, i) => (
          <div key={i} className={m.role === 'user' ? 'flex justify-end' : 'flex justify-start'}>
            <div
              className={
                m.role === 'user'
                  ? 'max-w-[85%] rounded-3xl rounded-br-lg bg-blue-600 px-4 py-3 text-sm text-white'
                  : 'max-w-[85%] rounded-3xl rounded-bl-lg border border-slate-100 bg-white px-4 py-3 text-sm text-slate-800 shadow-sm'
              }
            >
              <p className="whitespace-pre-wrap">{m.content}</p>
              {m.role === 'assistant' && (m.actions?.length ?? 0) > 0 && (
                <div className="mt-3 flex flex-col gap-2">
                  {m.actions!.filter((a) => ACTIONS[a]).map((a) => (
                    <button
                      key={a}
                      onClick={() => navigate(ACTIONS[a].to)}
                      className="rounded-xl bg-blue-50 px-3 py-2.5 text-left text-sm font-bold text-blue-700"
                    >
                      {ACTIONS[a].label}
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
        ))}

        {(busy || transcribing) && (
          <div className="flex justify-start">
            <div className="rounded-3xl rounded-bl-lg border border-slate-100 bg-white px-4 py-3 shadow-sm">
              {transcribing ? (
                <span className="text-sm text-slate-500">🎙 Səsiniz yazıya çevrilir…</span>
              ) : (
                <span className="inline-flex gap-1 text-slate-400">
                  <span className="animate-bounce">●</span>
                  <span className="animate-bounce [animation-delay:120ms]">●</span>
                  <span className="animate-bounce [animation-delay:240ms]">●</span>
                </span>
              )}
            </div>
          </div>
        )}

        {notice && (
          <div className="rounded-2xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">{notice}</div>
        )}
        <div ref={endRef} />
      </main>

      {/* Fixed composer, above the keyboard. */}
      <div className="fixed inset-x-0 bottom-0 border-t border-slate-200 bg-white p-3">
        <form
          className="mx-auto flex max-w-md items-center gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            void send(input)
          }}
        >
          <input
            value={input}
            onChange={(e) => setInput(e.target.value)}
            placeholder={recording ? 'Danışın…' : 'Sualınızı yazın…'}
            className="min-w-0 flex-1 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm outline-none focus:border-blue-400"
          />
          {voiceSupported && (
            <button
              type="button"
              onClick={() => void toggleVoice()}
              disabled={busy || transcribing}
              aria-label={recording ? 'Yazmanı dayandır' : 'Səslə soruş'}
              className={
                recording
                  ? 'shrink-0 animate-pulse rounded-2xl bg-red-500 px-4 py-3 text-white'
                  : 'shrink-0 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 disabled:opacity-40'
              }
            >
              {recording ? '⏹' : '🎤'}
            </button>
          )}
          <button
            type="submit"
            disabled={busy || !input.trim()}
            className="shrink-0 rounded-2xl bg-blue-600 px-5 py-3 text-sm font-bold text-white disabled:opacity-40"
          >
            Göndər
          </button>
        </form>
        <p className="mx-auto mt-1.5 max-w-md text-center text-[11px] text-slate-400">
          {recording
            ? 'Sualınızı azərbaycanca və ya rusca deyin, sonra ⏹ basın.'
            : 'Köməkçi səhv edə bilər — maaş və rəsmi məsələlərdə son söz rəhbərinizindir.'}
        </p>
      </div>
    </div>
  )
}
