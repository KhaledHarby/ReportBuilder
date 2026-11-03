import type { UserAccount } from '../api/types'

const ACCESS = 'rb.accessToken'
const REFRESH = 'rb.refreshToken'
const EXPIRES = 'rb.accessExpiresAt'
const USER = 'rb.user'

export type AuthSession = {
  accessToken: string
  refreshToken: string
  accessTokenExpiresAt: string
  user: UserAccount
}

const listeners = new Set<() => void>()

export function subscribeAuth(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function notify() {
  listeners.forEach((listener) => listener())
}

export function getAccessToken() {
  return localStorage.getItem(ACCESS)
}

export function getRefreshToken() {
  return localStorage.getItem(REFRESH)
}

export function getAccessExpiresAt() {
  return localStorage.getItem(EXPIRES)
}

export function getStoredUser(): UserAccount | null {
  const raw = localStorage.getItem(USER)
  if (!raw) {
    return null
  }
  try {
    return JSON.parse(raw) as UserAccount
  } catch {
    return null
  }
}

export function setSession(session: AuthSession) {
  localStorage.setItem(ACCESS, session.accessToken)
  localStorage.setItem(REFRESH, session.refreshToken)
  localStorage.setItem(EXPIRES, session.accessTokenExpiresAt)
  localStorage.setItem(USER, JSON.stringify(session.user))
  notify()
}

export function setStoredUser(user: UserAccount) {
  localStorage.setItem(USER, JSON.stringify(user))
  notify()
}

export function clearSession() {
  localStorage.removeItem(ACCESS)
  localStorage.removeItem(REFRESH)
  localStorage.removeItem(EXPIRES)
  localStorage.removeItem(USER)
  notify()
}
