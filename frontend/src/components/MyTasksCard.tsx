import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useFeatureEnabled } from '../branding/BrandingContext'
import { getMyTasks, type EmployeeTask } from '../api/employeeTasks'

/**
 * "You have work waiting" on the home screen.
 *
 * A task assigned to someone who never opens the menu is a task that does not exist, and the push
 * that announced it was swiped away hours ago. So the home screen — the one screen everybody opens,
 * every morning, to scan — carries the count.
 *
 * Renders nothing at all when there is nothing open: an empty card every day is how a person learns
 * to stop looking at that part of the screen.
 */
export function MyTasksCard() {
  const navigate = useNavigate()
  const enabled = useFeatureEnabled('employeetasks')
  const [tasks, setTasks] = useState<EmployeeTask[]>([])

  useEffect(() => {
    if (!enabled) return
    void getMyTasks()
      .then((r) => {
        if (r.status === 200 && Array.isArray(r.data)) setTasks(r.data.filter((t) => t.status === 'Assigned'))
      })
      .catch(() => {})
  }, [enabled])

  if (!enabled || tasks.length === 0) return null

  const overdue = tasks.filter((t) => t.overdue).length
  const today = new Date().toISOString().slice(0, 10)
  const dueToday = tasks.filter((t) => t.dueDate === today && !t.overdue).length

  return (
    <button
      onClick={() => navigate('/tasks')}
      className={`flex w-full items-center justify-between rounded-3xl border p-4 text-left shadow-sm transition active:scale-[0.99] ${
        overdue > 0 ? 'border-red-200 bg-red-50' : 'border-slate-100 bg-white'
      }`}
    >
      <div className="flex min-w-0 items-center gap-3">
        <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-blue-100 text-xl">📋</span>
        <span className="min-w-0">
          <span className="block font-bold text-slate-900">
            {tasks.length === 1 ? '1 tapşırığınız var' : `${tasks.length} tapşırığınız var`}
          </span>
          <span className="block truncate text-sm text-slate-500">
            {overdue > 0
              ? `${overdue} gecikib — açıb baxın`
              : dueToday > 0
                ? `${dueToday}-i bu gün bitməlidir`
                : tasks[0].title}
          </span>
        </span>
      </div>
      <span className="shrink-0 pl-2 text-slate-300">›</span>
    </button>
  )
}
