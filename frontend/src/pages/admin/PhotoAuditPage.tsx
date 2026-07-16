import { useEffect, useMemo, useState } from 'react'
import { getSummary, recheckFaces } from '../../api/admin'
import {
  getEmployeeAttendance,
  getPhotoUrl,
  type AttendanceRecord,
  type PhotoUrlResponse,
} from '../../api/attendance'
import { StatusBadge } from '../../components/StatusBadge'
import { PhotoCompareModal } from '../../components/PhotoCompareModal'
import { FaceFlagBadge } from '../../components/FaceFlagBadge'
import { IconCamera, IconX } from '../../components/icons'
import { fmtDateOfInstant, fmtTime } from '../../lib/format'

interface EmpOption {
  id: string
  name: string
  location: string | null
}

interface ModalState {
  title: string
  photo: PhotoUrlResponse
}

export function PhotoAuditPage() {
  const [employees, setEmployees] = useState<EmpOption[]>([])
  const [empError, setEmpError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState('')

  const [records, setRecords] = useState<AttendanceRecord[]>([])
  const [recError, setRecError] = useState<string | null>(null)
  const [recLoading, setRecLoading] = useState(false)
  const [loadedEmployee, setLoadedEmployee] = useState<string | null>(null)

  const [busyId, setBusyId] = useState<string | null>(null)
  const [noPhotoIds, setNoPhotoIds] = useState<Set<string>>(new Set())
  const [rowError, setRowError] = useState<string | null>(null)
  const [modal, setModal] = useState<ModalState | null>(null)
  const [recheckBusy, setRecheckBusy] = useState(false)
  const [recheckMsg, setRecheckMsg] = useState<string | null>(null)

  // Employee roster for the picker. Sourced from the summary endpoint over the last 90 days (the
  // photo-retention window) because it is available to BOTH Admin and Manager and is already scoped
  // server-side — the Admin-only /api/admin/employees list can't be used on a manager-facing page.
  useEffect(() => {
    void loadEmployees()
  }, [])

  async function loadEmployees() {
    const to = new Date()
    const from = new Date()
    from.setDate(from.getDate() - 90)
    const { status, data } = await getSummary(toDateStr(from), toDateStr(to))
    if (status === 200 && data && 'rows' in data) {
      const opts = data.rows
        .map((r) => ({ id: r.employeeId, name: r.employeeName, location: r.locationName }))
        .sort((a, b) => a.name.localeCompare(b.name))
      setEmployees(opts)
      setEmpError(null)
    } else if (status === 403) {
      setEmpError('İcazəniz yoxdur')
    } else {
      setEmpError('İşçi siyahısı yüklənmədi')
    }
  }

  const selectedEmployee = useMemo(
    () => employees.find((e) => e.id === selectedId) ?? null,
    [employees, selectedId],
  )

  async function onSelect(id: string) {
    setSelectedId(id)
    setRecords([])
    setNoPhotoIds(new Set())
    setRowError(null)
    setRecError(null)
    if (!id) {
      setLoadedEmployee(null)
      return
    }
    setRecLoading(true)
    const { status, data } = await getEmployeeAttendance(id)
    if (status === 200 && Array.isArray(data)) {
      const sorted = [...data].sort((a, b) => b.attendanceDate.localeCompare(a.attendanceDate))
      setRecords(sorted)
    } else if (status === 403) {
      setRecError('Bu işçini görməyə icazəniz yoxdur')
    } else {
      setRecError('Qeydlər yüklənmədi')
    }
    setLoadedEmployee(id)
    setRecLoading(false)
  }

  async function viewPhoto(record: AttendanceRecord) {
    setBusyId(record.recordId)
    setRowError(null)
    // Always fetch fresh — presigned URLs expire (~5 min), so never reuse a cached URL on reopen.
    const { status, data } = await getPhotoUrl(record.recordId)
    setBusyId(null)
    if (status !== 200 || !data || 'error' in data) {
      setRowError('Şəkil yüklənmədi')
      return
    }
    if (!data.hasPhoto) {
      setNoPhotoIds((prev) => new Set(prev).add(record.recordId))
      return
    }
    setModal({
      title: `${selectedEmployee?.name ?? ''} — ${fmtDateOfInstant(record.attendanceDate)}`,
      photo: data,
    })
  }

  async function onRecheckAll() {
    setRecheckBusy(true)
    setRecheckMsg(null)
    const { status, data } = await recheckFaces()
    setRecheckBusy(false)
    if (status === 200 && data && 'queued' in data)
      setRecheckMsg(`${data.queued} qeyd üz-uyğunluğuna görə yenidən yoxlanışa göndərildi (bir neçə dəqiqəyə hazır olar).`)
    else setRecheckMsg('Əməliyyat alınmadı')
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 12, marginBottom: 12, flexWrap: 'wrap' }}>
        <div>
          <h1 style={{ fontSize: 18, fontWeight: 800, color: 'var(--c900)' }}>Foto Audit</h1>
          <div className="muted" style={{ fontSize: 13 }}>Giriş şəklini referans ilə müqayisə et — üz uyğunluğu avtomatik yoxlanır.</div>
        </div>
        <button className="btn btn-sm" disabled={recheckBusy} onClick={onRecheckAll} title="Bütün foto qeydlərini üz-uyğunluğuna görə yenidən yoxla">
          {recheckBusy ? 'Göndərilir…' : 'Hamısını yenidən yoxla'}
        </button>
      </div>
      {recheckMsg && (
        <div className="fb fb-ok" style={{ marginBottom: 12 }}>
          <span>{recheckMsg}</span>
        </div>
      )}

      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <label className="form-label" htmlFor="emp-select">
          İşçi seçin
        </label>
        <select
          id="emp-select"
          className="inp"
          value={selectedId}
          onChange={(e) => void onSelect(e.target.value)}
        >
          <option value="">— İşçi seçin —</option>
          {employees.map((e) => (
            <option key={e.id} value={e.id}>
              {e.name}
              {e.location ? ` · ${e.location}` : ''}
            </option>
          ))}
        </select>
        {empError && (
          <div className="fb fb-err" style={{ marginTop: 10 }}>
            <IconX />
            <span>{empError}</span>
          </div>
        )}
      </div>

      {rowError && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{rowError}</span>
        </div>
      )}
      {recError && (
        <div className="fb fb-err" style={{ marginBottom: 12 }}>
          <IconX />
          <span>{recError}</span>
        </div>
      )}

      {selectedId && recLoading && (
        <div className="card card-pad muted" style={{ textAlign: 'center', padding: 40 }}>
          Yüklənir…
        </div>
      )}

      {selectedId &&
        !recLoading &&
        loadedEmployee === selectedId &&
        records.length === 0 &&
        !recError && (
          <div className="card card-pad muted" style={{ textAlign: 'center', padding: 40 }}>
            Qeyd yoxdur
          </div>
        )}

      {records.length > 0 && (
        <div className="tbl-wrap">
          <table>
            <thead>
              <tr>
                <th>Tarix</th>
                <th>Status</th>
                <th>Giriş</th>
                <th>Çıxış</th>
                <th>Foto</th>
                <th>Üz</th>
              </tr>
            </thead>
            <tbody>
              {records.map((r) => (
                <tr key={r.recordId}>
                  <td style={{ fontWeight: 700, color: 'var(--c900)' }}>{fmtDateOfInstant(r.attendanceDate)}</td>
                  <td>
                    <StatusBadge status={r.status} />
                  </td>
                  <td className="mono">{fmtTime(r.checkInAtUtc)}</td>
                  <td className="mono">{fmtTime(r.checkOutAtUtc)}</td>
                  <td>
                    {noPhotoIds.has(r.recordId) ? (
                      <span className="muted" style={{ fontSize: 12 }}>
                        Foto yoxdur
                      </span>
                    ) : (
                      <button
                        className="btn btn-sm"
                        disabled={busyId === r.recordId}
                        onClick={() => void viewPhoto(r)}
                      >
                        <IconCamera /> {busyId === r.recordId ? 'Yüklənir…' : 'Şəkli gör'}
                      </button>
                    )}
                  </td>
                  <td>
                    <FaceFlagBadge status={r.faceMatchStatus} score={r.faceMatchScore} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {modal && (
        <PhotoCompareModal
          title={modal.title}
          referenceUrl={modal.photo.referencePhotoUrl}
          checkInUrl={modal.photo.checkInPhotoUrl}
          checkInTakenAtUtc={modal.photo.checkInPhotoTakenAtUtc}
          faceMatchStatus={modal.photo.faceMatchStatus}
          faceMatchScore={modal.photo.faceMatchScore}
          onClose={() => setModal(null)}
        />
      )}
    </div>
  )
}

function toDateStr(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

