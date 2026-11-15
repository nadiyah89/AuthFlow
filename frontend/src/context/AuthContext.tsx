import React, { createContext, useCallback, useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../services/api'
import type {
  AuthContextType,
  LoginRequest,
  RegisterRequest,
  UserProfile,
} from '../types/auth.types'

type AuthBroadcastEvent =
  | { type: 'LOGIN'; user: UserProfile }
  | { type: 'LOGOUT' }

/**
 * Module-level broadcast channel manager.
 * Ensures the channel is not prematurely closed during React component
 * mount/unmount lifecycles, and prevents InvalidStateError on postMessage.
 */
class SessionBroadcastManager {
  private static instance: SessionBroadcastManager | null = null
  private channel: BroadcastChannel | null = null

  private constructor() {
    this.init()
  }

  public static getInstance(): SessionBroadcastManager {
    if (!SessionBroadcastManager.instance) {
      SessionBroadcastManager.instance = new SessionBroadcastManager()
    }
    return SessionBroadcastManager.instance
  }

  private init(): void {
    if (typeof BroadcastChannel === 'undefined') return
    try {
      this.channel = new BroadcastChannel('authflow_session_channel')
    } catch {
      this.channel = null
    }
  }

  public post(message: AuthBroadcastEvent): void {
    if (typeof BroadcastChannel === 'undefined') return

    try {
      if (!this.channel) {
        this.init()
      }
      this.channel?.postMessage(message)
    } catch {
      // If the channel was in an invalid/closed state, reinitialize and retry safely
      try {
        this.init()
        this.channel?.postMessage(message)
      } catch {
        // BroadcastChannel unavailable in this environment; safely ignore
      }
    }
  }

  public subscribe(listener: (message: AuthBroadcastEvent) => void): () => void {
    if (typeof BroadcastChannel === 'undefined') return () => {}

    if (!this.channel) {
      this.init()
    }
    if (!this.channel) return () => {}

    const onMessage = (event: MessageEvent<AuthBroadcastEvent>) => {
      if (event.data) {
        listener(event.data)
      }
    }

    this.channel.addEventListener('message', onMessage)
    return () => {
      this.channel?.removeEventListener('message', onMessage)
    }
  }
}

export const AuthContext = createContext<AuthContextType | undefined>(undefined)

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<UserProfile | null>(null)
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [error, setError] = useState<string | null>(null)

  const clearError = useCallback(() => setError(null), [])

  // Initial silent session check and multi-tab listener on mount
  useEffect(() => {
    let isMounted = true

    // Bind API auth failure hook (triggers if refresh token is rejected or revoked)
    api.setOnAuthFailure(() => {
      if (isMounted) {
        setUser(null)
      }
    })

    // Listen for multi-tab session changes
    const unsubscribe = SessionBroadcastManager.getInstance().subscribe((message) => {
      if (!isMounted) return
      if (message.type === 'LOGOUT') {
        api.setAccessToken(null)
        setUser(null)
      } else if (message.type === 'LOGIN' && message.user) {
        setUser(message.user)
      }
    })

    const restoreSession = async () => {
      try {
        // Attempt silent refresh using the single-flight coalesced refresh mechanism
        const response = await api.refreshSingleFlight()
        if (isMounted) {
          setUser(response.user)
        }
      } catch {
        // Unauthenticated or expired cookie
        if (isMounted) {
          setUser(null)
        }
      } finally {
        if (isMounted) {
          setIsLoading(false)
        }
      }
    }

    restoreSession()

    return () => {
      isMounted = false
      unsubscribe()
    }
  }, [])

  const register = useCallback(
    async (data: RegisterRequest) => {
      setError(null)
      try {
        const response = await api.register(data)
        setUser(response.user)
        SessionBroadcastManager.getInstance().post({ type: 'LOGIN', user: response.user })
      } catch (err) {
        const message =
          err instanceof ApiError ? err.message : 'Registration failed. Please try again.'
        setError(message)
        throw err
      }
    },
    []
  )

  const login = useCallback(
    async (data: LoginRequest) => {
      setError(null)
      try {
        const response = await api.login(data)
        setUser(response.user)
        SessionBroadcastManager.getInstance().post({ type: 'LOGIN', user: response.user })
      } catch (err) {
        const message =
          err instanceof ApiError ? err.message : 'Login failed. Please try again.'
        setError(message)
        throw err
      }
    },
    []
  )

  const logout = useCallback(async () => {
    try {
      // Safely broadcast logout to other tabs before server revocation and local state reset
      SessionBroadcastManager.getInstance().post({ type: 'LOGOUT' })
      await api.logout()
    } finally {
      setUser(null)
    }
  }, [])

  const refreshUser = useCallback(async (): Promise<UserProfile | null> => {
    try {
      const profile = await api.getMe()
      setUser(profile)
      return profile
    } catch {
      setUser(null)
      return null
    }
  }, [])

  const value = useMemo<AuthContextType>(
    () => ({
      user,
      isAuthenticated: !!user,
      isLoading,
      error,
      register,
      login,
      logout,
      refreshUser,
      clearError,
    }),
    [user, isLoading, error, register, login, logout, refreshUser, clearError]
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
