import { useEffect, useState } from 'react'
import { EmployeeLink } from '../../components/EmployeeLink'
import { getBirthdays, type BirthdayRow } from '../../api/admin'

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'İyun',
  'İyul', 'Avqust', 'Sentyabr', 'Oktyabr', 'Noyabr', 'Dekabr',
]

export function BirthdaysPage() {
  const [rows, setRows] = useState<BirthdayRow[]>([])
  const [loaded, setLoaded] = useState(false)
  const monthName = MONTHS[new Date().getMonth()]
  const todayDay = new Date().getDate()

  useEffect(() => {
    void getBirthdays().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) setRows(data)
      setLoaded(true)
    })
  }, [])

  return (
    <div>
      <div className="card card-pad">
        <div className="card-title">🎂 {monthName} ayında doğum günü olanlar</div>

        {loaded && rows.length === 0 && (
          <div className="muted" style={{ padding: '16px 0' }}>
            Bu ay doğum günü olan yoxdur. (Yalnız tam doğum tarixi olan işçilər görünür — İşçilər
            səhifəsindən doğum tarixini əlavə edin.)
          </div>
        )}

        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          {rows.map((r) => {
            const upcoming = !r.isToday && r.day >= todayDay
            return (
              <div
                key={r.employeeId}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 12,
                  border: '1px solid var(--c100)',
                  borderRadius: 'var(--r)',
                  padding: '10px 14px',
                  background: r.isToday ? 'var(--leaf-bg)' : undefined,
                }}
              >
                <div
                  style={{
                    width: 44,
                    textAlign: 'center',
                    fontFamily: "'Sora',sans-serif",
                    fontWeight: 800,
                    fontSize: 20,
                    color: r.isToday ? 'var(--leaf-d)' : 'var(--c700)',
                    lineHeight: 1,
                  }}
                >
                  {r.day}
                  <div style={{ fontSize: 10, fontWeight: 600, color: 'var(--c400)' }}>{monthName.slice(0, 3)}</div>
                </div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontWeight: 700, color: 'var(--c900)' }}>
                    <EmployeeLink id={r.employeeId} name={r.fullName} />
                  </div>
                  <div style={{ fontSize: 12, color: 'var(--c400)' }}>
                    {r.locationName}
                    {r.turningAge > 0 && ` · ${r.turningAge} yaş`}
                  </div>
                </div>
                {r.isToday ? (
                  <span className="tag" style={{ background: 'var(--leaf-bg)', color: 'var(--leaf-d)', fontWeight: 700 }}>
                    🎉 Bu gün!
                  </span>
                ) : upcoming ? (
                  <span className="tag" style={{ background: 'var(--amber-bg)', color: 'var(--amber)' }}>
                    yaxınlaşır
                  </span>
                ) : (
                  <span className="tag muted">keçib</span>
                )}
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}
