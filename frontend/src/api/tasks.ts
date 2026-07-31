import { apiRequest } from './client'

/** One item on the shared operator task board. */
export interface TaskRow {
  id: string
  title: string
  isDone: boolean
  by: string
  at: string
}

/** Whether the signed-in operator may see the "Tapşırıqlar" board (config allowlist, not a role). */
export function getTaskAccess() {
  return apiRequest<{ canAccess: boolean }>('/api/tasks/access')
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

export function deleteTask(id: string) {
  return apiRequest<{ removed: boolean }>(`/api/tasks/${id}`, { method: 'DELETE' })
}
