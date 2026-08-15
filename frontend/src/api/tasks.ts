import { apiRequest } from './client'

/** One item on the shared operator task board. */
export interface TaskRow {
  id: string
  title: string
  isDone: boolean
  isImportant: boolean
  dueDate: string | null
  by: string
  at: string
  /** Who the item is FOR (null = nobody in particular — the honest default on a shared list). */
  assignedToEmployeeId: string | null
  assignedToName: string | null
}

/** Who a task may be given to: the company's own admins and managers. */
export interface Assignable {
  id: string
  name: string
}

/** Whether the signed-in operator may see the "Tapşırıqlar" board (config allowlist, not a role). */
export function getTaskAccess() {
  return apiRequest<{ canAccess: boolean }>('/api/tasks/access')
}

export function getAssignable() {
  return apiRequest<Assignable[]>('/api/tasks/assignable')
}

/** PUT /api/tasks/{id}/assign — employeeId null hands it back to nobody. */
export function assignTask(id: string, employeeId: string | null) {
  return apiRequest<{ assignedToEmployeeId: string | null; assignedToName: string | null }>(
    `/api/tasks/${id}/assign`, { method: 'PUT', body: { employeeId } })
}

export function getTasks() {
  return apiRequest<TaskRow[]>('/api/tasks')
}

export function createTask(title: string) {
  return apiRequest<{ id: string; by: string }>('/api/tasks', { method: 'POST', body: { title } })
}

export function toggleTask(id: string) {
  return apiRequest<{ isDone: boolean }>(`/api/tasks/${id}/toggle`, { method: 'POST' })
}

export function toggleTaskImportant(id: string) {
  return apiRequest<{ isImportant: boolean }>(`/api/tasks/${id}/important`, { method: 'POST' })
}

export function deleteTask(id: string) {
  return apiRequest<{ removed: boolean }>(`/api/tasks/${id}`, { method: 'DELETE' })
}

export function renameTask(id: string, title: string) {
  return apiRequest<{ title: string }>(`/api/tasks/${id}/title`, { method: 'PUT', body: { title } })
}

/** dueDate = "yyyy-MM-dd" to set, null to clear. */
export function setTaskDue(id: string, dueDate: string | null) {
  return apiRequest<{ dueDate: string | null }>(`/api/tasks/${id}/due`, { method: 'PUT', body: { dueDate } })
}

export function reorderTasks(ids: string[]) {
  return apiRequest<{ ok: boolean }>('/api/tasks/reorder', { method: 'PUT', body: { ids } })
}
