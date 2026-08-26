import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import { useNavigate } from 'react-router-dom'
import { clearToken, getToken, setToken, setUnauthorizedHandler } from '../api/client'
import { decodeJwt, roleHome, type Role } from '../lib/jwt'
import { listProfiles, rememberCurrent, removeProfile, type SavedProfile } from '../lib/profiles'
import { getMyProfile } from '../api/attendance'

interface AuthState {
  token: string | null
  isAuthenticated: boolean
  role: Role | null
  /** The signed-in employee's own id — lets a screen tell "this row is me" apart from everyone else
   *  (e.g. the admin employee form, which must not let you switch yourself off). */
  employeeId: string | null
  email: string | null
  /** True while the account is still on a temporary PIN — the app forces the "set your PIN" screen. */
  mustChangePin: boolean
  saveToken: (token: string) => void
  logout: () => void
  /** Other accounts saved on this device — the crew phone. Empty on an ordinary personal phone. */
  profiles: SavedProfile[]
  /** Put another saved account into the active slot. Reloads the app; see the implementation. */
  switchProfile: (employeeId: string) => void
}

const AuthContext = createContext<AuthState | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setTokenState] = useState<string | null>(() => getToken())
  // Re-read on every token change rather than held as state of its own: a profile added on another
  // screen, or dropped by the 401 handler below, has to be visible here without a second source of
  // truth to keep in step.
  const profiles = useMemo(() => listProfiles(), [token])
  const navigate = useNavigate()

  useEffect(() => {
    // A 401 from any authenticated call clears the session and returns to login.
    setUnauthorizedHandler(() => {
      // The active token just proved to be dead — its PIN was reset, or an admin retired the session.
      // Leaving it in the saved list would offer the holder a row that looks like a way in and is not,
      // and on a crew phone they would find that out standing at the poster. Drop it; the OTHER saved
      // profiles are untouched and still work.
      const dead = getToken()
      const sub = dead ? decodeJwt(dead)?.sub : null
      if (sub) removeProfile(sub)
      setTokenState(null)
      navigate('/login', { replace: true })
    })
  }, [navigate])

  const value = useMemo<AuthState>(() => {
    const claims = token ? decodeJwt(token) : null

    return {
      token,
      isAuthenticated: token !== null,
      role: claims?.role ?? null,
      employeeId: claims?.sub ?? null,
      email: claims?.email ?? null,
      mustChangePin: claims?.mcp === '1',
      saveToken: (t: string) => {
        setToken(t)
        setTokenState(t)
        // Remember this account on the device, so whoever sets a crew phone up can always get back to
        // their own login without retyping a PIN. The name is not in the JWT — only an id and an
        // email — and a switcher listing raw ids would be unusable, so it is fetched once here.
        //
        // Failure is not handled because there is nothing to handle: an account still on a temporary
        // PIN is refused this endpoint until it picks one, and saveToken runs again with the fresh
        // token the moment it does. rememberCurrent itself declines to store operator and
        // impersonation tokens.
        void getMyProfile().then((r) => {
          if (r.status === 200 && r.data && 'fullName' in r.data) rememberCurrent(r.data.fullName, t)
        })
      },
      logout: () => {
        // "Sign out of THIS account", the way it reads on the button. The other saved profiles stay —
        // wiping a crew phone's whole list because one worker tapped the red button would strand
        // twenty-nine people who had nothing to do with it. Removing a single profile is offered
        // explicitly in the switcher; clearing them all is offered there too.
        const sub = claims?.sub
        if (sub) removeProfile(sub)
        clearToken()
        setTokenState(null)
        navigate('/login', { replace: true })
      },
      profiles,
      switchProfile: (employeeId: string) => {
        const next = listProfiles().find((p) => p.employeeId === employeeId)
        if (!next) return
        setToken(next.token)
        // A FULL RELOAD, not a state update. Every screen in the app holds data belonging to whoever
        // was signed in — today's check-in card, the queued-scan badge, a half-loaded profile — and on
        // a crew phone the next person walks straight to the poster and taps. Carrying one worker's
        // rendered state into another worker's session is the kind of bug that gets discovered as a
        // wrong attendance record a week later, so the cheap, total answer is to start clean.
        //
        // Landing is by role: these are almost always workers, but the person who set the phone up
        // may well be an admin, and dropping them on the employee home would look like a demotion.
        window.location.assign(roleHome(decodeJwt(next.token)?.role))
      },
    }
  }, [token, navigate, profiles])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
