import { memo, type MouseEvent } from 'react'
import type { DayAttendanceRow } from '../../api/admin'
import { EmployeeLink } from '../../components/EmployeeLink'
import { StatusBadge, dayVisual } from '../../components/StatusBadge'
import { FaceFlagBadge } from '../../components/FaceFlagBadge'
import { IconCamera, IconClock, IconPencil } from '../../components/icons'
import { fmtTime } from '../../lib/format'
import { initials, shiftHours, shiftTitle } from './todayShift'

/**
 * One line of the board — pulled out of the page and memoised.
 *
 * Why it is its own component: Bakı Abadlıq puts 488 people on this table and nothing virtualises it,
 * so every re-render of the page was a re-render of 488 rows. Pressing a status card («İşdə») changes
 * nothing about the people who stay on screen, yet React re-rendered all of them and the board froze
 * for a moment on every press — reported from the field the day the redesign shipped.
 *
 * Memoisation only pays if the props are stable, which is what shapes this interface: every handler
 * is a `useCallback` with no dependencies, the derived strings (worked time, the day's hours) arrive
 * ready-made rather than as a clock the row reads for itself, and the reason MENU is not here at all
 * — it is `position: fixed`, so it is rendered once at page level instead of being a conditional
 * branch inside 488 rows.
 *
 * The one case this cannot help is the thirty-second poll: it replaces every row object, so every row
 * is genuinely new. That is the right trade — a background refresh may cost what a button press
 * must not.
 */
export interface TodayRowProps {
  r: DayAttendanceRow
  showLocCol: boolean
  showPosition: boolean
  showSchedule: boolean
  showWorked: boolean
  /** «Incomplete» reads «İşdə» today and «Çıxış yoxdur» on a past date. */
  isToday: boolean
  /** Already formatted («08:53»), empty when there is nothing to count. */
  workedText: string
  /** Still at work — the figure is counting up, so it takes the company's accent. */
  running: boolean
  mayViewPhotos: boolean
  assigning: boolean
  photoBusy: boolean
  onPosition: (position: string) => void
  onLocation: (locationId: string) => void
  onReason: (e: MouseEvent, employeeId: string) => void
  onPhoto: (r: DayAttendanceRow) => void
}

const PAST_INCOMPLETE = { cls: 'b-absent', label: 'Çıxış yoxdur', icon: 'x' as const }

function Row({
  r, showLocCol, showPosition, showSchedule, showWorked, isToday,
  workedText, running, mayViewPhotos, assigning, photoBusy,
  onPosition, onLocation, onReason, onPhoto,
}: TodayRowProps) {
  const hours = showSchedule ? shiftHours(r) : null
  const canGiveReason = r.status === 'Absent' || r.status === 'Onboarding' || r.status === 'DayOff'
    || ((r.status === 'OnLeave' || r.status === 'Permission') && !!r.leaveId)

  return (
    <tr>
      <td data-label="İşçi">
        <span className="att-emp">
          {/* Initials, not a thumbnail: the selfie is the audit control and opens on demand, and a
              column of faces would be two hundred signed URLs on a screen that is read in a room with
              other people in it. */}
          <span className="att-av" aria-hidden="true">{initials(r.employeeName)}</span>
          <span className="att-emp-t">
            <span className="att-emp-n"><EmployeeLink id={r.employeeId} name={r.employeeName} /></span>
            {/* The job title sits under the name, where the design puts it — and stays the filter it
                was: one click narrows the board to that trade. */}
            {r.position && !showPosition && (
              <button
                className="att-emp-p"
                onClick={() => onPosition(r.position!)}
                title={`Yalnız «${r.position}» vəzifəsi`}
              >
                {r.position}
              </button>
            )}
          </span>
        </span>
      </td>
      {showLocCol && (
        <td data-label="Ərazi">
          <button className="tbl-filter" onClick={() => onLocation(r.locationId)}>
            {r.locationName}
          </button>
        </td>
      )}
      {showPosition && (
        <td data-label="Vəzifə">
          {r.position
            ? <button className="tbl-filter" onClick={() => onPosition(r.position!)}>{r.position}</button>
            : null}
        </td>
      )}
      {showSchedule && (
        <td data-label="İş qrafiki">
          {hours
            ? (
              <span className="att-sched" title={shiftTitle(r)}>
                <IconClock />
                {hours}
              </span>
            )
            : <span className="att-none">—</span>}
        </td>
      )}
      <td data-label="Status">
        {/* Pencil next to the badge on a Qayıb row (to pin a reason) or an assigned single-day leave
            (to change it, or revert to Qayıb). The menu it opens lives at page level. */}
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 2 }}>
          <StatusBadge
            status={r.status}
            override={r.status === 'Incomplete' && !isToday ? PAST_INCOMPLETE : dayVisual(r.status, r.leaveType)}
          />
          {/* Which rows can be given a reason.
              «Aktivləşdirməyib»: the person whose day nobody can decide but a human — no scan history,
              so the system will never call them absent by itself.
              «İstirahət»: a rest day is the branch's calendar, not a statement about the person, and
              somebody on that day may in fact be on holiday or off sick. The Fəvvarələr manager had
              nineteen people reading «İstirahət» on a Sunday, some of them on leave and some ill, and
              no way to say so from this screen — the pencil simply never appeared on those rows. */}
          {canGiveReason && (
            assigning ? (
              <span className="muted" style={{ marginLeft: 6, fontSize: 12 }}>…</span>
            ) : (
              <button
                className="reason-pencil"
                title="Səbəb təyin et / dəyiş"
                onClick={(e) => onReason(e, r.employeeId)}
              >
                <IconPencil />
              </button>
            )
          )}
        </span>
        {/* Who pinned this reason. It was a second line under the badge, which made every leave row
            twice the height of the ones around it — on a board of two hundred names, uneven rows are
            what stops the eye tracking down a column. */}
        {r.leaveAssignedBy && <span className="tbl-by" title={`Təyin edən: ${r.leaveAssignedBy}`}>ⓘ</span>}
        {/* A Qayıb somebody wrote by hand says whose decision it was — it costs a day's pay. */}
        {r.absenceMarkedBy && <span className="tbl-by" title={`Qayıbı yazan: ${r.absenceMarkedBy}`}>✋</span>}
        {/* This giriş-çıxış was entered/changed by hand, not scanned — attribute it. */}
        {r.manualBy && (
          <div style={{ fontSize: 11, marginTop: 4, color: 'var(--amber)' }}>
            Əl ilə daxil edilib · {r.manualBy}
          </div>
        )}
        {/* Not a manual entry and not a poster scan: the worker closed their own field visit and went
            home, which closed this day at the moment they left the site. */}
        {r.closedByFieldVisit && (
          <div style={{ fontSize: 11, marginTop: 4, color: 'var(--c600)' }}>
            📍 Ərazi çıxışı ilə bağlandı
          </div>
        )}
      </td>
      <td className="mono" data-label="Giriş">
        {(r.checkInAtUtc ?? r.fieldCheckInAtUtc) ? fmtTime(r.checkInAtUtc ?? r.fieldCheckInAtUtc) : ''}
        {r.status === 'Field' && (
          <span
            className="tag"
            title="Sahə ziyarəti — GPS ilə"
            style={{ marginLeft: 6, background: 'var(--leaf-bg)', color: 'var(--leaf-d)' }}
          >
            📍 sahə
          </span>
        )}
        {r.wasOffline && (
          <span
            className="tag"
            title="Oflayn qeydə alınıb — vaxt telefonun saatı ilədir"
            style={{ marginLeft: 6, background: 'var(--amber-bg)', color: 'var(--amber)' }}
          >
            📴 oflayn
          </span>
        )}
        {r.lateArrivalReason && (
          <div style={{ fontSize: 11, color: 'var(--amber)', fontWeight: 600, marginTop: 2 }}>
            Gec: {r.lateArrivalReason}
          </div>
        )}
      </td>
      <td className="mono" data-label="Çıxış">
        {/* On a day worked in two stretches this is the NIGHT's departure, not the morning block's —
            otherwise the row would read «07:00 → 11:00» and look as though the nine hours after ten at
            night were never recorded. */}
        {(r.lastCheckOutAtUtc ?? r.checkOutAtUtc ?? r.fieldCheckOutAtUtc)
          ? fmtTime(r.lastCheckOutAtUtc ?? r.checkOutAtUtc ?? r.fieldCheckOutAtUtc)
          : ''}
        {/* The count alone did not read: «07:18 → 11:19 · 2 blok» looked like one unbroken stretch
            with a puzzling label, and the whole point of a split day is that the person went home in
            between. So the stretches are named under the times. */}
        {(r.blocks ?? 1) > 1 && r.blockSpans && (
          <div style={{ fontSize: 11, color: 'var(--c400)', marginTop: 3, lineHeight: 1.5 }}>
            {r.blockSpans.map((b, i) => (
              <div key={i}>
                {i + 1}. {b.inAtUtc ? fmtTime(b.inAtUtc) : '—'} → {b.outAtUtc ? fmtTime(b.outAtUtc) : 'işdə'}
              </div>
            ))}
          </div>
        )}
        {r.earlyDepartureReason && <div className="tbl-note">Tez: {r.earlyDepartureReason}</div>}
      </td>
      {showWorked && (
        <td className="mono" data-label="İş vaxtı">
          {workedText
            ? (
              <span
                className={`att-worked${running ? ' live' : ''}`}
                title={running ? 'İşdədir — indiyə qədər' : undefined}
              >
                {workedText}
              </span>
            )
            : <span className="att-none">—</span>}
        </td>
      )}
      <td data-label="Əməliyyat">
        <span className="att-act">
          <FaceFlagBadge status={r.faceMatchStatus} score={r.faceMatchScore} compact />
          {/* Şəkli olan HƏR sətirdə (sahibin qərarı, 2026-08-31). Əvvəl yalnız üz-uyğunsuzluğu flaqlı
              və ortaq telefonlu sətirlərdə göstərilirdi; səbəb R2-dən yüklənmə gecikməsi idi, o isə
              burada tətbiq olunmur — şəkil YALNIZ düyməyə basanda çəkilir. Menecerdə görünmür:
              `mayViewPhotos`. */}
          {mayViewPhotos && r.hasPhoto && r.recordId ? (
            <button
              className="tbl-icon"
              disabled={photoBusy}
              onClick={() => onPhoto(r)}
              title="Giriş şəklini gör"
              aria-label="Giriş şəklini gör"
            >
              {photoBusy ? '…' : <IconCamera />}
            </button>
          ) : null}
        </span>
      </td>
    </tr>
  )
}

export const TodayRow = memo(Row)
