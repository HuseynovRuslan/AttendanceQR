import { useApprovals } from './ApprovalsContext'
import '../../approvals/approvals.css'

/** The section's page when there is nothing for this person: the switch is off, or no area concerns them. */
export function NotActive() {
  const { state } = useApprovals()
  return (
    <div className="apx-embed">
      <div className="apx-panel">
        <div className="apx-state">
          <h3>{state === 'error' ? 'Təsdiq bölməsi yüklənmədi' : 'Təsdiq sistemi sizin sahənizdə aktiv deyil'}</h3>
          <p>
            {state === 'error'
              ? 'Serverlə əlaqə alınmadı. Səhifəni bir az sonra yeniləyin.'
              : 'Müraciətlər yalnız Company Admin-in təsdiq sistemini açdığı strukturlarda işləyir. Sizin filiallarınızda dəyişikliklər köhnə qaydada, birbaşa edilir.'}
          </p>
        </div>
      </div>
    </div>
  )
}
