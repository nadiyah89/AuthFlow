import React, { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'
import styles from '../styles/layout.module.css'

export const Navbar: React.FC = () => {
  const { user, logout } = useAuth()
  const location = useLocation()
  const [isLoggingOut, setIsLoggingOut] = useState(false)

  const handleLogout = async () => {
    setIsLoggingOut(true)
    try {
      await logout()
    } finally {
      setIsLoggingOut(false)
    }
  }

  return (
    <header className={styles.navbar}>
      <div className={styles.navContainer}>
        <Link to="/dashboard" className={styles.brand}>
          <div className={styles.brandLogo}>AF</div>
          <span className={styles.brandName}>AuthFlow</span>
        </Link>

        {user && (
          <nav className={styles.navLinks} aria-label="Main Navigation">
            <Link
              to="/dashboard"
              className={`${styles.navLink} ${
                location.pathname === '/dashboard' ? styles.activeLink : ''
              }`}
            >
              Dashboard
            </Link>
            <Link
              to="/profile"
              className={`${styles.navLink} ${
                location.pathname === '/profile' ? styles.activeLink : ''
              }`}
            >
              Profile
            </Link>

            <div className={styles.userSection}>
              <span className={styles.userEmail} title={user.email}>
                {user.fullName || user.email}
              </span>
              <button
                type="button"
                onClick={handleLogout}
                disabled={isLoggingOut}
                className={styles.logoutBtn}
                aria-label="Sign out of account"
              >
                {isLoggingOut ? 'Signing out...' : 'Sign out'}
              </button>
            </div>
          </nav>
        )}
      </div>
    </header>
  )
}
