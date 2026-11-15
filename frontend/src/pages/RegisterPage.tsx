import React, { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'
import { ApiError } from '../services/api'
import styles from '../styles/auth.module.css'

export const RegisterPage: React.FC = () => {
  const { register } = useAuth()
  const navigate = useNavigate()

  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')

  const [showPassword, setShowPassword] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [generalError, setGeneralError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  // Password rules validation helpers
  const hasMinLength = password.length >= 8
  const hasUpper = /[A-Z]/.test(password)
  const hasLower = /[a-z]/.test(password)
  const hasNumber = /\d/.test(password)
  const hasSpecial = /[^A-Za-z0-9]/.test(password)
  const isPasswordValid = hasMinLength && hasUpper && hasLower && hasNumber && hasSpecial

  const validate = (): boolean => {
    const errors: Record<string, string> = {}

    if (!fullName.trim()) {
      errors.fullName = 'Full name is required.'
    } else if (fullName.trim().length < 2) {
      errors.fullName = 'Full name must be at least 2 characters.'
    }

    if (!email.trim()) {
      errors.email = 'Email address is required.'
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
      errors.email = 'Please enter a valid email address.'
    }

    if (!password) {
      errors.password = 'Password is required.'
    } else if (!isPasswordValid) {
      errors.password = 'Password does not meet the security requirements below.'
    }

    if (!confirmPassword) {
      errors.confirmPassword = 'Password confirmation is required.'
    } else if (password !== confirmPassword) {
      errors.confirmPassword = 'Passwords do not match.'
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
      await register({
        fullName: fullName.trim(),
        email: email.trim(),
        password,
        confirmPassword,
      })
      navigate('/dashboard', { replace: true })
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.status === 409) {
          setGeneralError('An account with this email address already exists.')
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
          setGeneralError(err.message || 'Registration failed. Please try again.')
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
          <h1 className={styles.title}>Create your account</h1>
          <p className={styles.subtitle}>Enter your details to register for AuthFlow</p>
        </div>

        {generalError && (
          <div className={styles.alertError} role="alert">
            <span>⚠️</span>
            <span>{generalError}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className={styles.form} noValidate>
          <div className={styles.formGroup}>
            <label htmlFor="fullName" className={styles.label}>
              Full Name
            </label>
            <input
              id="fullName"
              type="text"
              className={`${styles.input} ${fieldErrors.fullName ? styles.inputError : ''}`}
              placeholder="e.g. Jane Doe"
              value={fullName}
              onChange={(e) => {
                setFullName(e.target.value)
                if (fieldErrors.fullName) setFieldErrors((prev) => ({ ...prev, fullName: '' }))
              }}
              disabled={isSubmitting}
              autoComplete="name"
              required
            />
            {fieldErrors.fullName && <p className={styles.fieldError}>{fieldErrors.fullName}</p>}
          </div>

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
                autoComplete="new-password"
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

          {password.length > 0 && (
            <div className={styles.passwordChecklist} aria-live="polite">
              <div className={`${styles.checkItem} ${hasMinLength ? styles.checkItemValid : ''}`}>
                <span>{hasMinLength ? '✓' : '○'}</span> At least 8 characters
              </div>
              <div className={`${styles.checkItem} ${hasUpper ? styles.checkItemValid : ''}`}>
                <span>{hasUpper ? '✓' : '○'}</span> At least one uppercase letter (A-Z)
              </div>
              <div className={`${styles.checkItem} ${hasLower ? styles.checkItemValid : ''}`}>
                <span>{hasLower ? '✓' : '○'}</span> At least one lowercase letter (a-z)
              </div>
              <div className={`${styles.checkItem} ${hasNumber ? styles.checkItemValid : ''}`}>
                <span>{hasNumber ? '✓' : '○'}</span> At least one number (0-9)
              </div>
              <div className={`${styles.checkItem} ${hasSpecial ? styles.checkItemValid : ''}`}>
                <span>{hasSpecial ? '✓' : '○'}</span> At least one special symbol (!@#$%^&*)
              </div>
            </div>
          )}

          <div className={styles.formGroup}>
            <label htmlFor="confirmPassword" className={styles.label}>
              Confirm Password
            </label>
            <input
              id="confirmPassword"
              type={showPassword ? 'text' : 'password'}
              className={`${styles.input} ${fieldErrors.confirmPassword ? styles.inputError : ''}`}
              placeholder="••••••••••••"
              value={confirmPassword}
              onChange={(e) => {
                setConfirmPassword(e.target.value)
                if (fieldErrors.confirmPassword)
                  setFieldErrors((prev) => ({ ...prev, confirmPassword: '' }))
              }}
              disabled={isSubmitting}
              autoComplete="new-password"
              required
            />
            {fieldErrors.confirmPassword && (
              <p className={styles.fieldError}>{fieldErrors.confirmPassword}</p>
            )}
          </div>

          <button type="submit" className={styles.submitBtn} disabled={isSubmitting}>
            {isSubmitting ? (
              <>
                <span className={styles.buttonSpinner} />
                Creating account...
              </>
            ) : (
              'Create account'
            )}
          </button>
        </form>

        <div className={styles.authFooter}>
          Already have an account? <Link to="/login">Sign in</Link>
        </div>
      </div>
    </div>
  )
}
