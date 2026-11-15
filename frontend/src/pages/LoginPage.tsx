import React, { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'
import { ApiError } from '../services/api'
import styles from '../styles/auth.module.css'

export const LoginPage: React.FC = () => {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [generalError, setGeneralError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  // If navigated from protected route, preserve target location
  const fromLocation = (location.state as { from?: { pathname?: string } })?.from?.pathname || '/dashboard'

  const validate = (): boolean => {
    const errors: Record<string, string> = {}

    if (!email.trim()) {
      errors.email = 'Email address is required.'
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
      errors.email = 'Please enter a valid email address.'
    }

    if (!password) {
      errors.password = 'Password is required.'
    }

    setFieldErrors(errors)
    return Object.keys(errors).length === 0
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setGeneralError(null)

    if (!validate()) {
      return
    }

    setIsSubmitting(true)
    try {
      await login({
        email: email.trim(),
        password,
      })
      navigate(fromLocation, { replace: true })
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 401) {
          setGeneralError('Invalid email or password.')
        } else if (err.status === 423) {
          setGeneralError(err.message || 'Account is temporarily locked due to repeated failed attempts.')
        } else if (err.status === 403) {
          setGeneralError('This account is inactive. Please contact support.')
        } else if (err.validationErrors && Object.keys(err.validationErrors).length > 0) {
          const apiFieldErrors: Record<string, string> = {}
          for (const [key, msgs] of Object.entries(err.validationErrors)) {
            const normalizedKey = key.charAt(0).toLowerCase() + key.slice(1)
            if (msgs && msgs.length > 0) {
              apiFieldErrors[normalizedKey] = msgs[0]
            }
          }
          setFieldErrors(apiFieldErrors)
        } else {
          setGeneralError(err.message || 'Authentication failed.')
        }
      } else {
        setGeneralError('Network error. Unable to reach server.')
      }
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className={styles.authContainer}>
      <div className={styles.authCard}>
        <div className={styles.authHeader}>
          <div className={styles.brandLogo}>AF</div>
          <h1 className={styles.title}>Welcome back</h1>
          <p className={styles.subtitle}>Enter your credentials to access your account</p>
        </div>

        {generalError && (
          <div className={styles.alertError} role="alert">
            <span>⚠️</span>
            <span>{generalError}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className={styles.form} noValidate>
          <div className={styles.formGroup}>
            <label htmlFor="email" className={styles.label}>
              Email Address
            </label>
            <input
              id="email"
              type="email"
              className={`${styles.input} ${fieldErrors.email ? styles.inputError : ''}`}
              placeholder="jane@example.com"
              value={email}
              onChange={(e) => {
                setEmail(e.target.value)
                if (fieldErrors.email) setFieldErrors((prev) => ({ ...prev, email: '' }))
              }}
              disabled={isSubmitting}
              autoComplete="email"
              required
            />
            {fieldErrors.email && <p className={styles.fieldError}>{fieldErrors.email}</p>}
          </div>

          <div className={styles.formGroup}>
            <label htmlFor="password" className={styles.label}>
              Password
            </label>
            <div className={styles.inputWrapper}>
              <input
                id="password"
                type={showPassword ? 'text' : 'password'}
                className={`${styles.input} ${fieldErrors.password ? styles.inputError : ''}`}
                placeholder="••••••••••••"
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value)
                  if (fieldErrors.password) setFieldErrors((prev) => ({ ...prev, password: '' }))
                }}
                disabled={isSubmitting}
                autoComplete="current-password"
                required
              />
              <button
                type="button"
                onClick={() => setShowPassword((prev) => !prev)}
                className={styles.passwordToggle}
                tabIndex={-1}
                aria-label={showPassword ? 'Hide password' : 'Show password'}
              >
                {showPassword ? 'Hide' : 'Show'}
              </button>
            </div>
            {fieldErrors.password && <p className={styles.fieldError}>{fieldErrors.password}</p>}
          </div>

          <button type="submit" className={styles.submitBtn} disabled={isSubmitting}>
            {isSubmitting ? (
              <>
                <span className={styles.buttonSpinner} />
                Signing in...
              </>
            ) : (
              'Sign in'
            )}
          </button>
        </form>

        <div className={styles.authFooter}>
          Don't have an account? <Link to="/register">Create an account</Link>
        </div>
      </div>
    </div>
  )
}
