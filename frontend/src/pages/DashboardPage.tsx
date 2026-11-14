import React from 'react'
import { Link } from 'react-router-dom'
import { Navbar } from '../components/Navbar'
import { useAuth } from '../hooks/useAuth'
import styles from '../styles/dashboard.module.css'

export const DashboardPage: React.FC = () => {
  const { user, logout } = useAuth()

  const memberSince = user?.createdAtUtc
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
          <div className={styles.welcomeHeader}>
            <div>
              <h1 className={styles.welcomeTitle}>
                Welcome, {user?.fullName || 'there'}!
              </h1>
              <p className={styles.welcomeSubtitle}>
                Here is a summary of your account and current session.
              </p>
            </div>
            <div className={styles.badgeSuccess}>
              <span className={styles.statusDot} />
              <span>Active</span>
            </div>
          </div>
        </section>

        <section className={styles.grid}>
          <div className={styles.card}>
            <div className={styles.cardHeader}>
              <span className={styles.cardLabel}>Account Holder</span>
              <span className={styles.cardIcon}>👤</span>
            </div>
            <div className={styles.cardValue}>{user?.fullName}</div>
            <div className={styles.cardDesc}>{user?.email}</div>
          </div>

          <div className={styles.card}>
            <div className={styles.cardHeader}>
              <span className={styles.cardLabel}>Member Since</span>
              <span className={styles.cardIcon}>📅</span>
            </div>
            <div className={styles.cardValue}>{memberSince}</div>
            <div className={styles.cardDesc}>Verified user account</div>
          </div>

          <div className={styles.card}>
            <div className={styles.cardHeader}>
              <span className={styles.cardLabel}>Security</span>
              <span className={styles.cardIcon}>🛡️</span>
            </div>
            <div className={styles.cardValue}>Protected</div>
            <div className={styles.cardDesc}>Password authentication enabled</div>
          </div>
        </section>

        <section className={styles.card}>
          <div className={styles.cardHeader}>
            <span className={styles.cardLabel}>Quick Actions</span>
          </div>
          <p className={styles.cardDesc} style={{ marginBottom: '1rem' }}>
            Manage your account details or end your current session.
          </p>
          <div className={styles.actionRow}>
            <Link to="/profile" className={styles.actionBtn}>
              View Profile
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
