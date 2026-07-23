import { apiRequest } from './client'
import type { TaskRecipient } from './tasks'

export interface TaskGiver {
  assignerId: string
  assignerName: string
  recipients: TaskRecipient[]
}

/** GET /api/admin/task-permissions — every granted assigner and who they may send to. Admin-only. */
export function getTaskPermissions() {
  return apiRequest<TaskGiver[]>('/api/admin/task-permissions')
}

/** PUT /api/admin/task-permissions/{assignerId} — replace an assigner's full recipient set
 *  (empty list removes their giver status). Admin-only. */
export function setTaskRecipients(assignerId: string, recipientEmployeeIds: string[]) {
  return apiRequest<{ assignerId: string; recipientCount: number } | { error: string }>(
    `/api/admin/task-permissions/${assignerId}`,
    { method: 'PUT', body: { recipientEmployeeIds } },
  )
}
