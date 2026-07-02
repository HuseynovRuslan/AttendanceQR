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

interface AuthState {
  token: string | null
  isAuthenticated: boolean
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

  const value = useMemo<AuthState>(
    () => ({
      token,
      isAuthenticated: token !== null,
      saveToken: (t: string) => {
        setToken(t)
        setTokenState(t)
      },
      logout: () => {
        clearToken()
        setTokenState(null)
        navigate('/login', { replace: true })
      },
    }),
    [token, navigate],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
