import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import {
  getPreferences,
  savePreferences,
  type Channels,
  type NotificationPreferences,
  type PreferenceBranch,
} from '../api/notificationCenter'
import { NotificationKindIcon } from '../components/NotificationKindIcon'
import { SwitchRow } from '../components/Switch'
import { IconBell, IconCheck, IconChevronLeft, IconChevronRight, IconRefresh, IconSearch, IconX } from '../components/icons'
import { isNativeApp } from '../lib/host'
import { disablePush, enablePush, isSubscribed, pushPermission, pushSupported, sendTestPush, TEST_PUSH_TRANSIENT } from '../lib/push'
import { isStandalone } from '../lib/device'
import { useLeaveGuard } from '../lib/useLeaveGuard'

/** Settles with `fallback` if `p` has not settled within `ms` — a browser API that never answers must not freeze a screen. */
export function within<T>(p: Promise<T>, ms: number, fallback: T): Promise<T> {
  return new Promise((resolve) => {
    const t = setTimeout(() => resolve(fallback), ms)
    p.then(
      (v) => {
        clearTimeout(t)
        resolve(v)
      },
      () => {
        clearTimeout(t)
        resolve(fallback)
      },
    )
  })
}

/** How long «Bu cihaz» waits for the browser to say whether it is subscribed before saying it could not tell. */
export const DEVICE_CHECK_MS = 6000

/**
 * «Bildiriş ayarları» — one person's notification settings, kept on the server for the company they are signed in to.
 *
 * Two parts. «Bu cihaz» is the phone itself: whether it can receive a push at all (a browser permission or the app's).
 * «Davamiyyət bildirişləri» is what they want to hear about the people they may see — every type off until switched
 * on, the branches (all they may see, or a pick from those only), and how it reaches them. The second part is shown
 * only to somebody the server says may see anybody's attendance; a plain employee sees the first part alone.
 *
 * The server is the authority on all of it: the branch list is their scope as the server computes it, and a save is
 * validated there again (a branch outside their scope, a type that does not exist, no channel at all).
 */

const ERRORS: Record<string, string> = {
  NotEligible: 'Sizin işçilərin davamiyyətinə baxmaq səlahiyyətiniz yoxdur — bu bildirişlər sizə göndərilə bilməz.',
  BranchOutOfScope: 'Seçdiyiniz filiallardan biri artıq sizin səlahiyyətinizdə deyil. Siyahını yeniləyin.',
  NoBranchSelected: 'Ən azı bir filial seçin və ya «Bütün səlahiyyətli filiallar»ı seçin.',
  NoChannel: 'Bildirişin necə çatacağını seçin — ən azı bir kanal açıq olmalıdır.',
  UnknownType: 'Bildiriş növü tanınmadı. Səhifəni yeniləyin.',
}

interface Draft {
  types: Record<string, boolean>
  branchMode: 'all' | 'selected'
  locationIds: string[]
  channels: Channels
}

function draftOf(p: NotificationPreferences): Draft {
  return {
    types: Object.fromEntries(p.types.map((t) => [t.key, t.enabled])),
    branchMode: p.branchMode,
    locationIds: [...p.locationIds],
    channels: { ...p.channels },
  }
}

function sameDraft(a: Draft, b: Draft): boolean {
  return (
    JSON.stringify(a.types) === JSON.stringify(b.types) &&
    a.branchMode === b.branchMode &&
    [...a.locationIds].sort().join() === [...b.locationIds].sort().join() &&
    a.channels.inApp === b.channels.inApp &&
    a.channels.push === b.channels.push &&
    a.channels.web === b.channels.web
  )
}

export function NotificationSettingsPage() {
  const [prefs, setPrefs] = useState<NotificationPreferences | null>(null)
  const [draft, setDraft] = useState<Draft | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [savedAt, setSavedAt] = useState<number | null>(null)

  async function load() {
    setLoadError(false)
    const { status, data } = await getPreferences()
    if (status === 200 && data) {
      setPrefs(data)
      setDraft(draftOf(data))
    } else {
      setLoadError(true)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  // «Yadda saxlanıldı» fades after a few seconds; it is announced once (role=status) and never steals focus.
  useEffect(() => {
    if (savedAt === null) return
    const t = setTimeout(() => setSavedAt(null), 4000)
    return () => clearTimeout(t)
  }, [savedAt])

  const dirty = prefs && draft ? !sameDraft(draft, draftOf(prefs)) : false
  const anyType = draft ? Object.values(draft.types).some(Boolean) : false
  const guard = useLeaveGuard(dirty)

  // The save bar floats over the page's end: the page grows by its height, so it never covers the last control.
  const barRef = useRef<HTMLDivElement>(null)
  const [barHeight, setBarHeight] = useState(0)
  const barShown = !!prefs?.eligible && (dirty || saving || !!saveError || savedAt !== null)
  useLayoutEffect(() => {
    const el = barRef.current
    if (!barShown || !el) {
      setBarHeight(0)
      return
    }
    const measure = () => setBarHeight(el.getBoundingClientRect().height)
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const ro = new ResizeObserver(measure)
    ro.observe(el)
    return () => ro.disconnect()
  }, [barShown])

  async function save(): Promise<boolean> {
    if (!draft || !prefs) return false
    setSaving(true)
    setSaveError(null)
    const { status, data } = await savePreferences({
      category: prefs.category,
      enabledTypes: Object.entries(draft.types).filter(([, on]) => on).map(([key]) => key),
      branchMode: draft.branchMode,
      locationIds: draft.branchMode === 'selected' ? draft.locationIds : [],
      inApp: draft.channels.inApp,
      push: draft.channels.push,
      web: draft.channels.web,
    })
    setSaving(false)
    if (status === 200 && data && 'types' in data) {
      setPrefs(data)
      setDraft(draftOf(data))
      setSavedAt(Date.now())
      return true
    }
    const code = data && typeof data === 'object' && 'error' in data ? String(data.error) : ''
    setSaveError(ERRORS[code] ?? 'Yadda saxlanmadı. İnternet bağlantısını yoxlayıb yenidən cəhd edin.')
    return false
  }

  async function saveAndLeave() {
    if (await save()) guard.leave()
    else guard.stay()
  }

  return (
    <div className="flex flex-col gap-5">
      <div>
        <Link
          to="/menu"
          className="-ml-2 inline-flex min-h-11 items-center gap-1 rounded-xl px-2 text-sm font-semibold text-slate-600 active:bg-slate-100"
        >
          <IconChevronLeft className="h-4 w-4" aria-hidden="true" />
          Profil
        </Link>
        <h1 className="mt-1 text-xl font-extrabold text-slate-900">Bildiriş ayarları</h1>
        <p className="text-sm text-slate-600">Hansı bildirişləri alacağınızı və necə çatacağını seçin.</p>
      </div>

      <DeviceSection />

      {loadError && (
        <div role="alert" className="rounded-3xl border border-red-100 bg-red-50 p-4">
          <div className="font-semibold text-red-800">Ayarlar yüklənmədi</div>
          <p className="mt-0.5 text-sm text-red-700">İnternet bağlantısını yoxlayın və yenidən cəhd edin.</p>
          <button
            type="button"
            onClick={() => void load()}
            className="mt-3 min-h-11 rounded-xl bg-white px-4 text-sm font-semibold text-red-700 ring-1 ring-red-200 active:bg-red-50"
          >
            Yenidən yüklə
          </button>
        </div>
      )}

      {!prefs && !loadError && <SettingsSkeleton />}

      {prefs && draft && !prefs.eligible && (
        <div className="rounded-3xl border border-slate-100 bg-white p-4 text-sm leading-relaxed text-slate-600 shadow-sm">
          İşçilərin giriş, çıxış və gecikmə bildirişləri filiala rəhbərlik edənlər üçündür. Sizin hesabınızda belə
          səlahiyyət olmadığından burada yalnız bu cihazın bildirişləri idarə olunur.
        </div>
      )}

      {prefs && draft && prefs.eligible && (
        <>
          <Section title="Davamiyyət bildirişləri" hint="Hamısı əvvəlcə söndürülüdür.">
            {prefs.types.map((t) => (
              <SwitchRow
                key={t.key}
                id={`type-${t.key}`}
                checked={!!draft.types[t.key]}
                onChange={(on) => setDraft({ ...draft, types: { ...draft.types, [t.key]: on } })}
                label={t.label}
                description={t.description}
                icon={<NotificationKindIcon typeKey={t.key} />}
              />
            ))}
          </Section>

          <BranchSection prefs={prefs} draft={draft} onChange={setDraft} disabled={!anyType} />

          <Section title="Bildiriş kanalları" hint={anyType ? undefined : 'Əvvəlcə ən azı bir bildiriş növünü açın.'}>
            <SwitchRow
              id="ch-inapp"
              checked={draft.channels.inApp}
              disabled={!anyType}
              onChange={(on) => setDraft({ ...draft, channels: { ...draft.channels, inApp: on } })}
              label="Tətbiqdaxili bildirişlər"
              description="Bildiriş mərkəzində tarixçə saxlanılır, oxunmamışların sayı görünür."
            />
            <SwitchRow
              id="ch-push"
              checked={draft.channels.push}
              disabled={!anyType}
              onChange={(on) => setDraft({ ...draft, channels: { ...draft.channels, push: on } })}
              label="Mobil push bildirişləri"
              description="Telefona bildiriş gəlir. Çox sayda olanda bir xülasə kimi göndərilir."
            />
            <SwitchRow
              id="ch-web"
              checked={draft.channels.web}
              disabled={!anyType}
              onChange={(on) => setDraft({ ...draft, channels: { ...draft.channels, web: on } })}
              label="Veb admin panel bildirişləri"
              description="Panel açıq olanda yeni bildiriş dərhal ekranda görünür."
            />
          </Section>

          <Link
            to="/admin/notifications"
            className="flex min-h-14 items-center gap-3 rounded-3xl border border-slate-100 bg-white px-4 py-3 shadow-sm active:bg-slate-50"
          >
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-slate-100 text-slate-600">
              <IconBell className="h-5 w-5" aria-hidden="true" />
            </span>
            <span className="min-w-0 flex-1 font-semibold text-slate-900">Bildiriş mərkəzi</span>
            <IconChevronRight className="h-5 w-5 text-slate-500" aria-hidden="true" />
          </Link>
        </>
      )}

      {/* Room for the save bar below the last control — exactly its height, whatever it holds (an error makes it taller). */}
      {barShown && <div aria-hidden="true" data-testid="save-bar-room" style={{ height: `calc(${barHeight}px + env(safe-area-inset-bottom))` }} />}

      {/* The save bar: visible only with something to save, above the tab bar, never covering the last control. */}
      {barShown && (
        <div ref={barRef} className="fixed inset-x-0 bottom-[calc(4.5rem+env(safe-area-inset-bottom))] z-30 px-4">
          <div className="mx-auto flex max-w-md flex-col gap-2 rounded-3xl border border-slate-200 bg-white/95 p-3 shadow-lg backdrop-blur">
            {saveError && (
              <p role="alert" className="flex items-start gap-2 px-1 text-sm text-red-700">
                <IconX className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                {saveError}
              </p>
            )}
            {savedAt !== null && !dirty ? (
              <p role="status" className="flex min-h-11 items-center justify-center gap-2 text-sm font-semibold text-green-700">
                <IconCheck className="h-4 w-4" aria-hidden="true" />
                Yadda saxlanıldı
              </p>
            ) : (
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => {
                    if (!prefs) return
                    setDraft(draftOf(prefs))
                    setSaveError(null)
                  }}
                  disabled={saving}
                  className="min-h-11 flex-1 rounded-2xl px-4 text-sm font-semibold text-slate-700 ring-1 ring-slate-200 active:bg-slate-50 disabled:opacity-50"
                >
                  Ləğv et
                </button>
                <button
                  type="button"
                  onClick={() => void save()}
                  disabled={saving || !dirty}
                  className="min-h-11 flex-[2] rounded-2xl bg-blue-600 px-4 text-sm font-bold text-white active:bg-blue-700 disabled:opacity-50"
                >
                  {saving ? 'Yadda saxlanır…' : 'Yadda saxla'}
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      {guard.pending && (
        <LeaveDialog saving={saving} onSave={() => void saveAndLeave()} onLeave={guard.leave} onStay={guard.stay} />
      )}
    </div>
  )
}

/**
 * «Dəyişikliklər yadda saxlanmayıb» — asked when the person tries to leave with changes (a link, the tab bar, back).
 * The safe answer has the focus; Escape stays.
 */
function LeaveDialog({
  saving,
  onSave,
  onLeave,
  onStay,
}: {
  saving: boolean
  onSave: () => void
  onLeave: () => void
  onStay: () => void
}) {
  const stayRef = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    stayRef.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onStay()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onStay])
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-slate-900/40 p-4 sm:items-center" onClick={onStay}>
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="leave-title"
        aria-describedby="leave-text"
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-sm rounded-3xl bg-white p-5 shadow-xl"
      >
        <h2 id="leave-title" className="text-lg font-bold text-slate-900">Dəyişikliklər yadda saxlanmayıb</h2>
        <p id="leave-text" className="mt-1 text-sm leading-relaxed text-slate-600">
          Səhifədən çıxsanız, bildiriş ayarlarında etdiyiniz dəyişikliklər itəcək.
        </p>
        <div className="mt-4 flex flex-col gap-2">
          <button
            type="button"
            onClick={onSave}
            disabled={saving}
            className="min-h-11 rounded-2xl bg-blue-600 px-4 text-sm font-bold text-white active:bg-blue-700 disabled:opacity-50"
          >
            {saving ? 'Yadda saxlanır…' : 'Yadda saxla və çıx'}
          </button>
          <button
            type="button"
            onClick={onLeave}
            disabled={saving}
            className="min-h-11 rounded-2xl px-4 text-sm font-semibold text-red-700 ring-1 ring-red-200 active:bg-red-50 disabled:opacity-50"
          >
            Saxlamadan çıx
          </button>
          <button
            ref={stayRef}
            type="button"
            onClick={onStay}
            className="min-h-11 rounded-2xl px-4 text-sm font-semibold text-slate-700 active:bg-slate-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-600"
          >
            Səhifədə qal
          </button>
        </div>
      </div>
    </div>
  )
}

function Section({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return (
    <section aria-label={title}>
      <h2 className="px-1 pb-2 text-[15px] font-bold text-slate-900">{title}</h2>
      <div className="divide-y divide-slate-100 overflow-hidden rounded-3xl border border-slate-100 bg-white shadow-sm">
        {children}
      </div>
      {hint && <p className="px-1 pt-2 text-[13px] leading-snug text-slate-600">{hint}</p>}
    </section>
  )
}

function SettingsSkeleton() {
  return (
    <div aria-hidden="true" className="flex flex-col gap-5">
      {[3, 2, 3].map((rows, i) => (
        <div key={i}>
          <div className="mb-2 ml-1 h-4 w-40 animate-pulse rounded bg-slate-200 motion-reduce:animate-none" />
          <div className="divide-y divide-slate-100 overflow-hidden rounded-3xl border border-slate-100 bg-white">
            {Array.from({ length: rows }, (_, r) => (
              <div key={r} className="flex items-center gap-3 p-4">
                <div className="h-9 w-9 animate-pulse rounded-full bg-slate-100 motion-reduce:animate-none" />
                <div className="flex-1 space-y-2">
                  <div className="h-3.5 w-28 animate-pulse rounded bg-slate-100 motion-reduce:animate-none" />
                  <div className="h-3 w-48 animate-pulse rounded bg-slate-100 motion-reduce:animate-none" />
                </div>
                <div className="h-7 w-12 animate-pulse rounded-full bg-slate-100 motion-reduce:animate-none" />
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}

// --- branches -----------------------------------------------------------------------------------------------

function BranchSection({
  prefs,
  draft,
  onChange,
  disabled,
}: {
  prefs: NotificationPreferences
  draft: Draft
  onChange: (d: Draft) => void
  disabled: boolean
}) {
  const total = prefs.branches.length
  const options: { value: 'all' | 'selected'; label: string; description: string }[] = [
    {
      value: 'all',
      label: 'Bütün səlahiyyətli filiallar',
      description: prefs.companyWide ? 'Şirkətin bütün filialları, sonradan açılanlar da.' : `Rəhbərlik etdiyiniz ${total} filial.`,
    },
    { value: 'selected', label: 'Yalnız seçilmiş filiallar', description: 'Siyahıdan özünüz seçirsiniz.' },
  ]

  return (
    <section aria-labelledby="branches-title">
      <h2 id="branches-title" className="px-1 pb-2 text-[15px] font-bold text-slate-900">Bildiriş alınacaq filiallar</h2>
      <div
        role="radiogroup"
        aria-labelledby="branches-title"
        className="divide-y divide-slate-100 overflow-hidden rounded-3xl border border-slate-100 bg-white shadow-sm"
      >
        {options.map((o) => {
          const on = draft.branchMode === o.value
          return (
            <button
              key={o.value}
              type="button"
              role="radio"
              aria-checked={on}
              disabled={disabled}
              onClick={() => onChange({ ...draft, branchMode: o.value })}
              className="flex min-h-14 w-full items-center gap-3 px-4 py-3 text-left active:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-blue-600"
            >
              <span
                aria-hidden="true"
                className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-full border-2 ${on ? 'border-blue-600' : 'border-slate-300'}`}
              >
                {on && <span className="h-2.5 w-2.5 rounded-full bg-blue-600" />}
              </span>
              <span className="min-w-0 flex-1">
                <span className="block font-semibold text-slate-900">{o.label}</span>
                <span className="mt-0.5 block text-sm text-slate-600">{o.description}</span>
              </span>
            </button>
          )
        })}
      </div>
      {draft.branchMode === 'selected' && !disabled && (
        <BranchPicker
          branches={prefs.branches}
          selected={draft.locationIds}
          onChange={(ids) => onChange({ ...draft, locationIds: ids })}
        />
      )}
    </section>
  )
}

/** Search, pick many, all/none, a count, grouped by structure — usable with 5 branches and with 200. */
function BranchPicker({
  branches,
  selected,
  onChange,
}: {
  branches: PreferenceBranch[]
  selected: string[]
  onChange: (ids: string[]) => void
}) {
  const [query, setQuery] = useState('')
  const picked = useMemo(() => new Set(selected), [selected])
  const folded = (s: string) => s.toLocaleLowerCase('az')
  const visible = useMemo(
    () => (query.trim() ? branches.filter((b) => folded(b.name).includes(folded(query.trim()))) : branches),
    [branches, query],
  )
  const groups = useMemo(() => {
    const map = new Map<string, { name: string; items: PreferenceBranch[] }>()
    for (const b of visible) {
      const key = b.structureId ?? ''
      const g = map.get(key) ?? { name: b.structureName ?? 'Struktursuz filiallar', items: [] }
      g.items.push(b)
      map.set(key, g)
    }
    // Named structures first, alphabetically; the ungrouped last.
    return [...map.entries()].sort(([ak, a], [bk, b]) => (ak === '' ? 1 : bk === '' ? -1 : a.name.localeCompare(b.name, 'az')))
  }, [visible])
  const grouped = groups.length > 1 || (groups.length === 1 && groups[0][0] !== '')

  function toggle(id: string) {
    onChange(picked.has(id) ? selected.filter((x) => x !== id) : [...selected, id])
  }
  function selectVisible() {
    onChange([...new Set([...selected, ...visible.map((b) => b.id)])])
  }

  return (
    <div className="mt-3 overflow-hidden rounded-3xl border border-slate-100 bg-white shadow-sm">
      <div className="border-b border-slate-100 p-3">
        <label className="relative block">
          <span className="sr-only">Filial axtar</span>
          <IconSearch className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-500" aria-hidden="true" />
          <input
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Filial axtar"
            className="min-h-11 w-full rounded-2xl bg-slate-50 pl-9 pr-3 text-base text-slate-900 placeholder:text-slate-500 focus:bg-white focus:outline-none focus:ring-2 focus:ring-blue-600"
          />
        </label>
        <div className="mt-2 flex items-center gap-2">
          <span className="flex-1 px-1 text-sm text-slate-600" aria-live="polite">
            <b className="tabular-nums text-slate-900">{selected.length}</b> / {branches.length} seçilib
          </span>
          <button
            type="button"
            onClick={selectVisible}
            className="min-h-11 rounded-xl px-3 text-sm font-semibold text-blue-700 active:bg-blue-50"
          >
            {query.trim() ? 'Tapılanları seç' : 'Hamısını seç'}
          </button>
          <button
            type="button"
            onClick={() => onChange([])}
            disabled={selected.length === 0}
            className="min-h-11 rounded-xl px-3 text-sm font-semibold text-slate-600 active:bg-slate-100 disabled:opacity-40"
          >
            Təmizlə
          </button>
        </div>
      </div>

      <div className="max-h-80 overflow-y-auto overscroll-contain">
        {visible.length === 0 && (
          <p className="p-4 text-center text-sm text-slate-600">«{query}» adlı filial tapılmadı.</p>
        )}
        {groups.map(([key, g]) => (
          <div key={key || 'none'} role="group" aria-label={grouped ? g.name : undefined}>
            {grouped && (
              <div className="sticky top-0 bg-slate-50/95 px-4 py-1.5 text-xs font-semibold text-slate-600 backdrop-blur">
                {g.name}
              </div>
            )}
            {g.items.map((b) => {
              const on = picked.has(b.id)
              return (
                <button
                  key={b.id}
                  type="button"
                  role="checkbox"
                  aria-checked={on}
                  onClick={() => toggle(b.id)}
                  className="flex min-h-12 w-full items-center gap-3 px-4 py-2 text-left active:bg-slate-50 focus-visible:outline focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-blue-600"
                >
                  <span
                    aria-hidden="true"
                    className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-md border-2 ${
                      on ? 'border-blue-600 bg-blue-600 text-white' : 'border-slate-300 bg-white'
                    }`}
                  >
                    {on && <IconCheck className="h-3.5 w-3.5" />}
                  </span>
                  <span className="min-w-0 flex-1 truncate text-slate-900">{b.name}</span>
                </button>
              )
            })}
          </div>
        ))}
      </div>
    </div>
  )
}

// --- this device ------------------------------------------------------------------------------------------------

/**
 * Whether THIS phone can receive a push — the one part no server setting can fix. Says what is wrong in the words of
 * the platform and how to fix it there: a blocked permission is changed only in the phone's or browser's settings,
 * which a web page cannot open, so the steps are given instead.
 */
function DeviceSection() {
  // 'checking' never stays: the browser gets DEVICE_CHECK_MS to answer, then it is 'unknown' with a way to ask again.
  const [device, setDevice] = useState<'checking' | 'on' | 'off' | 'unknown'>('checking')
  const on = device === 'on'
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)
  const supported = pushSupported()
  const [denied, setDenied] = useState(pushPermission() === 'denied')
  const native = isNativeApp()

  const check = useCallback(async () => {
    setDevice('checking')
    const answer = await within(
      isSubscribed().then((v): 'on' | 'off' => (v ? 'on' : 'off')),
      DEVICE_CHECK_MS,
      'unknown' as const,
    )
    setDevice(answer)
  }, [])

  useEffect(() => {
    if (supported) void check()
  }, [check, supported])

  async function turnOn() {
    setBusy(true)
    setMsg(null)
    const r = await enablePush()
    setBusy(false)
    if (r === 'ok') {
      setDevice('on')
    } else if (r === 'denied') {
      setDenied(true)
    } else if (r === 'disabled') {
      setMsg('Bu serverdə push bildirişləri hələ qoşulmayıb.')
    } else if (r === 'unsupported') {
      setMsg('Bu cihaz push bildirişlərini dəstəkləmir.')
    } else {
      setMsg('Alınmadı. Bir azdan yenidən cəhd edin.')
    }
  }

  async function turnOff() {
    setBusy(true)
    await disablePush()
    setBusy(false)
    setDevice('off')
  }

  async function test() {
    setBusy(true)
    setMsg(null)
    const result = await within(sendTestPush(), 15000, null)
    setBusy(false)
    if (result === null) setMsg('Sınaq bildirişi göndərilmədi. Bir azdan yenidən cəhd edin.')
    else if (result.transient) setMsg(TEST_PUSH_TRANSIENT)
    else if (result.reached === 0) setMsg('Bu cihazın abunəsi tapılmadı — bildirişləri söndürüb yenidən açın.')
    else setMsg('Sınaq bildirişi göndərildi — bir neçə saniyəyə gəlməlidir.')
  }

  let state: { tone: 'ok' | 'warn' | 'off'; title: string; text: string }
  if (!supported) {
    state = {
      tone: 'warn',
      title: 'Bu brauzerdə bildiriş yoxdur',
      text: isStandalone()
        ? 'Bu cihaz push bildirişlərini dəstəkləmir.'
        : 'iPhone-da bildiriş almaq üçün saytı «Ana ekrana əlavə et» ilə quraşdırın və oradan açın.',
    }
  } else if (denied) {
    state = {
      tone: 'warn',
      title: 'Bildiriş icazəsi bağlıdır',
      text: native
        ? 'Telefonun Ayarlar → Tətbiqlər → QRLog → Bildirişlər bölməsində icazəni açın, sonra bura qayıdın.'
        : 'Brauzerin ünvan sətrindəki kilid işarəsinə toxunun → Bildirişlər → İcazə verin. Sonra səhifəni yeniləyin.',
    }
  } else if (device === 'checking') {
    state = { tone: 'off', title: 'Yoxlanılır…', text: 'Bu cihazın bildiriş icazəsinə və abunəsinə baxılır.' }
  } else if (device === 'unknown') {
    state = {
      tone: 'warn',
      title: 'Vəziyyəti öyrənmək alınmadı',
      text: 'Brauzer vaxtında cavab vermədi. Yenidən yoxlayın; alınmasa, səhifəni yeniləyin.',
    }
  } else if (on) {
    state = { tone: 'ok', title: 'Bu cihazda bildirişlər açıqdır', text: 'Elanlar, növbə xatırlatmaları və seçdiyiniz bildirişlər bu telefona gəlir.' }
  } else {
    state = { tone: 'off', title: 'Bu cihazda bildirişlər söndürülüb', text: 'Açsanız, elanlar, növbə xatırlatmaları və seçdiyiniz bildirişlər bu telefona gələcək.' }
  }

  const dot =
    device === 'checking' && supported && !denied
      ? 'bg-slate-400 animate-pulse motion-reduce:animate-none'
      : state.tone === 'ok' ? 'bg-green-600' : state.tone === 'warn' ? 'bg-amber-500' : 'bg-slate-400'

  return (
    <section aria-labelledby="device-title">
      <h2 id="device-title" className="px-1 pb-2 text-[15px] font-bold text-slate-900">Bu cihaz</h2>
      <div className="rounded-3xl border border-slate-100 bg-white p-4 shadow-sm">
        <div className="flex items-start gap-3">
          <span aria-hidden="true" className={`mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full ${dot}`} />
          <div className="min-w-0 flex-1">
            <div className="font-semibold text-slate-900" aria-live="polite">{state.title}</div>
            <p className="mt-0.5 text-sm leading-snug text-slate-600">{state.text}</p>
            {msg && <p role="status" className="mt-2 text-sm text-slate-700">{msg}</p>}
          </div>
        </div>
        {supported && !denied && device === 'unknown' && (
          <div className="mt-3 flex gap-2">
            <button
              type="button"
              onClick={() => void check()}
              className="inline-flex min-h-11 items-center gap-2 rounded-2xl px-4 text-sm font-semibold text-blue-700 ring-1 ring-blue-200 active:bg-blue-50"
            >
              <IconRefresh className="h-4 w-4" aria-hidden="true" />
              Yenidən yoxla
            </button>
          </div>
        )}
        {supported && (device === 'on' || device === 'off' || denied) && (
          <div className="mt-3 flex gap-2">
            {on ? (
              <>
                <button
                  type="button"
                  onClick={() => void test()}
                  disabled={busy}
                  className="min-h-11 rounded-2xl px-4 text-sm font-semibold text-blue-700 ring-1 ring-blue-200 active:bg-blue-50 disabled:opacity-50"
                >
                  Sınaq bildirişi
                </button>
                <button
                  type="button"
                  onClick={() => void turnOff()}
                  disabled={busy}
                  className="min-h-11 rounded-2xl px-4 text-sm font-semibold text-slate-700 ring-1 ring-slate-200 active:bg-slate-50 disabled:opacity-50"
                >
                  Bu cihazda söndür
                </button>
              </>
            ) : (
              <button
                type="button"
                onClick={() => void turnOn()}
                disabled={busy}
                className="min-h-11 rounded-2xl bg-blue-600 px-4 text-sm font-bold text-white active:bg-blue-700 disabled:opacity-50"
              >
                {busy ? 'Açılır…' : denied ? 'Yenidən yoxla' : 'Bildirişləri aç'}
              </button>
            )}
          </div>
        )}
      </div>
    </section>
  )
}
