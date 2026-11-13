import type { PlanPreview } from '../../../api/shiftPlan'
import { dayLabel } from './planModel'

/**
 * What a plan will do before it does it: who, how many days are new, how many replace a plan that was there, and
 * which shift becomes which. The point is that a wrong choice is seen here — «12 days go from Səhər to Gecə» — and
 * not in next week's tabel. A count of zero is not shown: it is one more number to read and it says nothing.
 */
export function PreviewSummary({ preview, names }: { preview: PlanPreview; names?: string[] }) {
  if (preview.standing) {
    return (
      <div className="sp-preview">
        <Who names={names} />
        <p className="sp-previewlead">
          <b>{preview.changedPeople ?? 0}</b> işçinin daimi növbəsi <b>{preview.from ? dayLabel(preview.from) : ''}</b> tarixindən
          dəyişəcək: <b>{preview.label}</b>. Bitmə tarixi yoxdur.
        </p>
        <Moves preview={preview} unit="işçi" />
        <p className="sp-note">Bu tarixdən əvvəlki günlər köhnə növbə ilə qalır.</p>
        {preview.pastDays > 0 && <p className="sp-note sp-note-warn">Başlanğıc keçmişdədir: {preview.pastDays} keçmiş gün yenidən hesablanacaq.</p>}
      </div>
    )
  }
  const created = preview.newDays ?? 0
  const replaced = preview.replacedDays ?? 0
  const cleared = preview.clearedDays ?? 0
  const kept = preview.unchangedDays ?? 0
  return (
    <div className="sp-preview">
      <Who names={names} />
      {created + replaced + cleared === 0 ? (
        <p className="sp-previewlead">Heç nə dəyişməyəcək — plan artıq belədir.</p>
      ) : (
        <dl className="sp-counts">
          {created > 0 && <div><dt>Yeni yazılacaq</dt><dd>{created} gün</dd></div>}
          {replaced > 0 && <div className="sp-count-warn"><dt>Mövcud planı əvəz edəcək</dt><dd>{replaced} gün</dd></div>}
          {cleared > 0 && <div><dt>Öz növbəsinə qayıdacaq</dt><dd>{cleared} gün</dd></div>}
          {kept > 0 && <div><dt>Onsuz da belədir</dt><dd>{kept} gün</dd></div>}
        </dl>
      )}
      <Moves preview={preview} unit="gün" />
      {preview.pastDays > 0 && <p className="sp-note sp-note-warn">{preview.pastDays} keçmiş gün dəyişəcək — onların tabeli yenidən hesablanacaq.</p>}
    </div>
  )
}

/** The people, by name: «2 işçi» is easy to confirm without noticing it is the wrong two. */
function Who({ names }: { names?: string[] }) {
  if (!names || names.length === 0) return null
  const shown = names.slice(0, 6)
  return (
    <p className="sp-previewwho">
      Kim: <b>{shown.join(', ')}</b>{names.length > shown.length ? ` və daha ${names.length - shown.length} nəfər` : ''}
    </p>
  )
}

function Moves({ preview, unit }: { preview: PlanPreview; unit: string }) {
  if (preview.changes.length === 0) return null
  return (
    <ul className="sp-moves" aria-label="Nə nəyə dəyişəcək">
      {preview.changes.map((m) => (
        <li key={`${m.from}→${m.to}`}>
          <span className="sp-movefrom"><ShiftLabel text={m.from} /></span>
          <span aria-hidden className="sp-arrow">→</span>
          <span className="sp-moveto"><ShiftLabel text={m.to} /></span>
          <span className="sp-movecount">{m.count} {unit}</span>
        </li>
      ))}
    </ul>
  )
}

/** «Öz növbəsi: Səhər 06:00–15:00» → the shift, and «öz növbəsi» under it in small type: the shift is what is read. */
export function ShiftLabel({ text }: { text: string }) {
  const prefix = 'Öz növbəsi: '
  const own = text.startsWith(prefix)
  return <>{unbroken(own ? text.slice(prefix.length) : text)}{own && <span className="sp-labelnote">öz növbəsi</span>}</>
}

/** «Axşam 16:00–00:30»: the name may wrap, the hours may not — «16:00–» on one line and «00:30» on the next is misread. */
function unbroken(text: string) {
  const m = /^(.*?)\s*(\d\d:\d\d–\d\d:\d\d)$/.exec(text)
  if (!m) return text
  return <>{m[1]}{m[1] ? ' ' : ''}<span className="sp-nowrap">{m[2]}</span></>
}
