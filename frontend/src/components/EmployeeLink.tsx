import { Link } from 'react-router-dom'
import { useScopedRole } from '../auth/AccessContext'

/**
 * An employee's name, linking to their profile card (/admin/employees/:id).
 *
 * Admins and managers both, now the card loads from a manager-scoped endpoint. It was admin-only
 * because the page fetched the whole company roster and picked a row out of it — so for a manager
 * every name on every board was dead text, and the way to see one of their own people's month was to
 * ask an admin.
 *
 * The link is not the boundary: the route checks the access table and the endpoints re-check the
 * branch. This only decides whether a name looks clickable.
 */
export function EmployeeLink({ id, name }: { id: string | null | undefined; name: string }) {
  // Outside the panel there is no access context and this is the token's role, as before; in a company
  // switched to Enforced, whoever may open a profile («employees.view»).
  const role = useScopedRole('employees.view')
  // No id (e.g. a pre-auth rejected scan) or an employee's own view → plain text.
  if ((role !== 'Admin' && role !== 'Manager') || !id) return <>{name}</>
  return (
    <Link to={`/admin/employees/${id}`} className="emp-link">
      {name}
    </Link>
  )
}
