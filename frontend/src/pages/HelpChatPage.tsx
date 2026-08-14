import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { SubPageHeader } from '../components/SubPageHeader'
import { sendAssistantChat, type AssistantMessage } from '../api/assistant'

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

export function HelpChatPage() {
  const navigate = useNavigate()
  const [messages, setMessages] = useState<Bubble[]>([])
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const endRef = useRef<HTMLDivElement>(null)

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
      <SubPageHeader title="AI Köməkçi" />

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

        {busy && (
          <div className="flex justify-start">
            <div className="rounded-3xl rounded-bl-lg border border-slate-100 bg-white px-4 py-3 shadow-sm">
              <span className="inline-flex gap-1 text-slate-400">
                <span className="animate-bounce">●</span>
                <span className="animate-bounce [animation-delay:120ms]">●</span>
                <span className="animate-bounce [animation-delay:240ms]">●</span>
              </span>
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
            placeholder="Sualınızı yazın…"
            className="min-w-0 flex-1 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm outline-none focus:border-blue-400"
          />
          <button
            type="submit"
            disabled={busy || !input.trim()}
            className="shrink-0 rounded-2xl bg-blue-600 px-5 py-3 text-sm font-bold text-white disabled:opacity-40"
          >
            Göndər
          </button>
        </form>
        <p className="mx-auto mt-1.5 max-w-md text-center text-[11px] text-slate-400">
          Köməkçi səhv edə bilər — maaş və rəsmi məsələlərdə son söz rəhbərinizindir.
        </p>
      </div>
    </div>
  )
}
