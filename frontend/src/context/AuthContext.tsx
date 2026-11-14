import React, { createContext, useCallback, useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../services/api'
import type {
  AuthContextType,
  LoginRequest,
  RegisterRequest,
  UserProfile,
} from '../types/auth.types'

export const AuthContext = createContext<AuthContextType | undefined>(undefined)

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<UserProfile | null>(null)
  const [isLoading, setIsLoading] = useState<boolean>(true)
  const [error, setError] = useState<string | null>(null)

  const clearError = useCallback(() => setError(null), [])

  // Cross-tab broadcast coordination channel
  const broadcastChannel = useMemo(() => {
    return typeof BroadcastChannel !== 'undefined'
      ? new BroadcastChannel('authflow_session_channel')
      : null
  }, [])

  // Initial silent session check on application mount
  useEffect(() => {
    let isMounted = true

    // Bind API auth failure hook (triggers if refresh token is rejected or revoked)
    api.setOnAuthFailure(() => {
      if (isMounted) {
        setUser(null)
      }
    })

    // Listen for multi-tab session changes
    if (broadcastChannel) {
      broadcastChannel.onmessage = (event) => {
        if (event.data?.type === 'LOGOUT') {
          api.setAccessToken(null)
          if (isMounted) setUser(null)
        } else if (event.data?.type === 'LOGIN' && event.data.user) {
          if (isMounted) setUser(event.data.user)
        }
      }
    }

    const restoreSession = async () => {
      try {
        // Attempt silent refresh using HttpOnly cookie
        const response = await api.refresh()
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
      if (broadcastChannel) {
        broadcastChannel.close()
      }
    }
  }, [broadcastChannel])

  const register = useCallback(
    async (data: RegisterRequest) => {
      setError(null)
      try {
        const response = await api.register(data)
        setUser(response.user)
        broadcastChannel?.postMessage({ type: 'LOGIN', user: response.user })
      } catch (err) {
        const message =
          err instanceof ApiError ? err.message : 'Registration failed. Please try again.'
        setError(message)
        throw err
      }
    },
    [broadcastChannel]
  )

  const login = useCallback(
    async (data: LoginRequest) => {
      setError(null)
      try {
        const response = await api.login(data)
        setUser(response.user)
        broadcastChannel?.postMessage({ type: 'LOGIN', user: response.user })
      } catch (err) {
        const message =
          err instanceof ApiError ? err.message : 'Login failed. Please try again.'
        setError(message)
        throw err
      }
    },
    [broadcastChannel]
  )

  const logout = useCallback(async () => {
    try {
      await api.logout()
    } finally {
      setUser(null)
      broadcastChannel?.postMessage({ type: 'LOGOUT' })
    }
  }, [broadcastChannel])

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
