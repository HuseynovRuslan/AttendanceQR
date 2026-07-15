import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

/**
 * An employee's name that links to their admin profile (/admin/employees/:id). Only Admins get the
 * link — the profile page (and employee management) is Admin-only, so managers/others see plain text.
 * Use everywhere an employee name + id is shown (attendance board, reports, requests, …) so the name
 * is clickable consistently across the panel.
 */
export function EmployeeLink({ id, name }: { id: string | null | undefined; name: string }) {
  const { role } = useAuth()
  // No id (e.g. a pre-auth rejected scan) or not an admin → plain text.
  if (role !== 'Admin' || !id) return <>{name}</>
  return (
    <Link to={`/admin/employees/${id}`} className="emp-link">
      {name}
    </Link>
  )
}
