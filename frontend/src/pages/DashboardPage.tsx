import React from 'react'
import { Link } from 'react-router-dom'
import { Navbar } from '../components/Navbar'
import { useAuth } from '../hooks/useAuth'
import styles from '../styles/dashboard.module.css'

export const DashboardPage: React.FC = () => {
  const { user, logout } = useAuth()

  const formattedDate = user?.createdAtUtc
    ? new Date(user.createdAtUtc).toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'long',
        day: 'numeric',
      })
    : 'Recently'

  return (
    <>
      <Navbar />
      <main className={styles.dashboardContainer}>
        <section className={styles.welcomeCard}>
          <h1 className={styles.welcomeTitle}>
            Welcome back, {user?.fullName || 'User'}!
          </h1>
          <p className={styles.welcomeSubtitle}>
            You are securely authenticated into AuthFlow. Your session is protected by short-lived in-memory JWTs and rolling HttpOnly cookies.
          </p>

          <div className={styles.badgeSuccess}>
            <span className={styles.statusDot} />
            <span>Active Authenticated Session</span>
          </div>
        </section>

        <section className={styles.grid}>
          <div className={styles.card}>
            <div className={styles.cardHeader}>
              <span className={styles.cardLabel}>User Profile</span>
              <span className={styles.cardIcon}>👤</span>
            </div>
            <div className={styles.cardValue}>{user?.fullName}</div>
            <div className={styles.cardDesc}>{user?.email}</div>
          </div>

          <div className={styles.card}>
            <div className={styles.cardHeader}>
              <span className={styles.cardLabel}>Account Created</span>
              <span className={styles.cardIcon}>📅</span>
            </div>
            <div className={styles.cardValue}>{formattedDate}</div>
            <div className={styles.cardDesc}>PostgreSQL Verified Record</div>
          </div>

          <div className={styles.card}>
            <div className={styles.cardHeader}>
              <span className={styles.cardLabel}>Security Tier</span>
              <span className={styles.cardIcon}>🛡️</span>
            </div>
            <div className={styles.cardValue}>Enterprise Session</div>
            <div className={styles.cardDesc}>PBKDF2 Hashing + CSRF Protected</div>
          </div>
        </section>

        <section className={styles.card}>
          <div className={styles.cardHeader}>
            <span className={styles.cardLabel}>Quick Actions</span>
          </div>
          <div className={styles.actionRow}>
            <Link to="/profile" className={styles.actionBtn}>
              View Full Profile
            </Link>
            <button
              type="button"
              onClick={() => logout()}
              className={`${styles.actionBtn} ${styles.secondaryBtn}`}
            >
              Sign out
            </button>
          </div>
        </section>
      </main>
    </>
  )
}
