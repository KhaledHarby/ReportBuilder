import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { AuthSession, UserAccount } from '../api/types'
import {
  clearSession,
  getAccessToken,
  getRefreshToken,
  getStoredUser,
  setSession,
  setStoredUser,
  subscribeAuth,
} from './session'

type AuthContextValue = {
  user: UserAccount | null
  ready: boolean
  isAdmin: boolean
  can: (permission: string) => boolean
  login: (userName: string, password: string) => Promise<UserAccount>
  acceptSession: (session: AuthSession) => void
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [user, setUser] = useState<UserAccount | null>(getStoredUser)
  const [ready, setReady] = useState(false)

  const hydrate = useCallback(async () => {
    if (!getAccessToken() && !getRefreshToken()) {
      setUser(null)
      setReady(true)
      return
    }

    const tokenAtStart = getAccessToken()
    try {
      const me = await api.me()
      if (getAccessToken() !== tokenAtStart) {
        return
      }
      setStoredUser(me)
      setUser(me)
    } catch {
      if (getAccessToken() !== tokenAtStart) {
        return
      }
      clearSession()
      setUser(null)
    } finally {
      setReady(true)
    }
  }, [])

  useEffect(() => {
    void hydrate()
    return subscribeAuth(() => setUser(getStoredUser()))
  }, [hydrate])

  useEffect(() => {
    if (ready && !user) {
      queryClient.clear()
    }
  }, [ready, user, queryClient])

  const login = useCallback(async (userName: string, password: string) => {
    const session = await api.login(userName, password)
    setSession(session)
    queryClient.clear()
    setUser(session.user)
    return session.user
  }, [queryClient])

  const acceptSession = useCallback((session: AuthSession) => {
    setSession(session)
    queryClient.clear()
    setUser(session.user)
  }, [queryClient])

  const logout = useCallback(async () => {
    const refreshToken = getRefreshToken()
    try {
      await api.logout(refreshToken)
    } catch {
      // Local sign-out still proceeds if the API is unreachable.
    }
    clearSession()
    setUser(null)
  }, [])

  const can = useCallback(
    (permission: string) => {
      if (user?.permissions.includes(permission) === true) {
        return true
      }
      if (permission === 'logs.view' && user?.permissions.includes('logs.manage') === true) {
        return true
      }
      if (permission === 'lookups.view' && user?.permissions.includes('lookups.update') === true) {
        return true
      }
      return (
        permission === 'sql.view' && user?.permissions.includes('sql.modify') === true
      )
    },
    [user],
  )

  const isAdmin =
    user?.roles?.some((role) => role.toLowerCase() === 'admin') === true

  const value = useMemo(
    () => ({ user, ready, isAdmin, can, login, acceptSession, logout }),
    [user, ready, isAdmin, can, login, acceptSession, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within AuthProvider')
  }
  return context
}
