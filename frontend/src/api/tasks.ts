import { apiRequest } from './client'

export type TaskStatus = 'Pending' | 'Completed'
export type TaskDirection = 'outgoing' | 'incoming'

export interface TaskItem {
  id: string
  title: string
  description: string | null
  dueDate: string | null
  status: TaskStatus
  /** outgoing = I assigned it; incoming = it was assigned to me. */
  direction: TaskDirection
  assignedById: string
  assignedByName: string
  assignedToId: string
  assignedToName: string
  createdAtUtc: string
  completedAtUtc: string | null
  acknowledged: boolean
}

export interface TaskRecipient {
  id: string
  name: string
}

export interface TaskAccess {
  /** Should the Tapşırıqlar section be shown to this user at all. */
  canSee: boolean
  /** May this user create/send tasks. */
  canAssign: boolean
  /** The employees this user is allowed to send tasks to (all active employees, for an admin). */
  recipients: TaskRecipient[]
}

export interface CreateTaskInput {
  assignedToEmployeeIds: string[]
  title: string
  description?: string | null
  dueDate: string
}

/** GET /api/admin/tasks — every task I assigned or was assigned. Admin + Manager. */
export function getTasks() {
  return apiRequest<TaskItem[]>('/api/admin/tasks')
}

/** GET /api/admin/tasks/access — what the current user may do with tasks (nav gating + recipient list). */
export function getTaskAccess() {
  return apiRequest<TaskAccess>('/api/admin/tasks/access')
}

/** POST /api/admin/tasks — send a task to one or more recipients; each is notified. */
export function createTask(input: CreateTaskInput) {
  return apiRequest<{ ids: string[] } | { error: string }>('/api/admin/tasks', { method: 'POST', body: input })
}

/** POST /api/admin/tasks/{id}/complete — the assignee marks it ready; the assigner is notified. */
export function completeTask(id: string) {
  return apiRequest<{ id: string; status: TaskStatus } | { error: string }>(
    `/api/admin/tasks/${id}/complete`,
    { method: 'POST' },
  )
}

/** POST /api/admin/tasks/{id}/acknowledge — the assigner confirms they saw the completion. */
export function acknowledgeTask(id: string) {
  return apiRequest<{ id: string } | { error: string }>(
    `/api/admin/tasks/${id}/acknowledge`,
    { method: 'POST' },
  )
}

/** DELETE /api/admin/tasks/{id} — the assigner cancels a task they created. */
export function deleteTask(id: string) {
  return apiRequest<{ deleted: string } | { error: string }>(`/api/admin/tasks/${id}`, { method: 'DELETE' })
}
