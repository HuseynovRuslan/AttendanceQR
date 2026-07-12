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
import { decodeJwt, type Role } from '../lib/jwt'

interface AuthState {
  token: string | null
  isAuthenticated: boolean
  role: Role | null
  email: string | null
  /** True while the account is still on a temporary PIN — the app forces the "set your PIN" screen. */
  mustChangePin: boolean
  saveToken: (token: string) => void
  logout: () => void
}

const AuthContext = createContext<AuthState | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setTokenState] = useState<string | null>(() => getToken())
  const navigate = useNavigate()

  useEffect(() => {
    // A 401 from any authenticated call clears the session and returns to login.
    setUnauthorizedHandler(() => {
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
      email: claims?.email ?? null,
      mustChangePin: claims?.mcp === '1',
      saveToken: (t: string) => {
        setToken(t)
        setTokenState(t)
      },
      logout: () => {
        clearToken()
        setTokenState(null)
        navigate('/login', { replace: true })
      },
    }
  }, [token, navigate])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
