import React from 'react'
import { Link } from 'react-router-dom'
import { Navbar } from '../components/Navbar'
import { useAuth } from '../hooks/useAuth'
import styles from '../styles/dashboard.module.css'

export const ProfilePage: React.FC = () => {
  const { user } = useAuth()

  const formattedUtc = user?.createdAtUtc
    ? new Date(user.createdAtUtc).toUTCString()
    : 'Unknown'

  const formattedLocal = user?.createdAtUtc
    ? new Date(user.createdAtUtc).toLocaleString()
    : 'Unknown'

  return (
    <>
      <Navbar />
      <main className={styles.dashboardContainer}>
        <section className={styles.card}>
          <div className={styles.cardHeader}>
            <div>
              <h1 style={{ fontSize: '1.4rem', fontWeight: 700, color: 'var(--color-text-main)' }}>
                Account Profile
              </h1>
              <p style={{ fontSize: '0.875rem', color: 'var(--color-text-muted)', marginTop: '0.2rem' }}>
                Verified account information retrieved directly from the protected <code>/api/auth/me</code> endpoint.
              </p>
            </div>
            <div className={styles.badgeSuccess} style={{ marginTop: 0 }}>
              <span className={styles.statusDot} />
              <span>Verified Identity</span>
            </div>
          </div>

          <div className={styles.infoList} style={{ marginTop: '1.5rem' }}>
            <div className={styles.infoItem}>
              <span className={styles.infoKey}>User Identity (ID)</span>
              <span className={styles.infoVal} style={{ fontFamily: 'monospace' }}>
                {user?.id}
              </span>
            </div>

            <div className={styles.infoItem}>
              <span className={styles.infoKey}>Full Name</span>
              <span className={styles.infoVal}>{user?.fullName}</span>
            </div>

            <div className={styles.infoItem}>
              <span className={styles.infoKey}>Email Address</span>
              <span className={styles.infoVal}>{user?.email}</span>
            </div>

            <div className={styles.infoItem}>
              <span className={styles.infoKey}>Member Since (UTC)</span>
              <span className={styles.infoVal}>{formattedUtc}</span>
            </div>

            <div className={styles.infoItem}>
              <span className={styles.infoKey}>Local Timezone Timestamp</span>
              <span className={styles.infoVal}>{formattedLocal}</span>
            </div>

            <div className={styles.infoItem}>
              <span className={styles.infoKey}>Session Architecture</span>
              <span className={styles.infoVal}>
                Access Token stored in memory only • Refresh Token in Secure HttpOnly Cookie
              </span>
            </div>
          </div>

          <div className={styles.actionRow} style={{ marginTop: '1.5rem' }}>
            <Link to="/dashboard" className={styles.actionBtn}>
              ← Back to Dashboard
            </Link>
          </div>
        </section>
      </main>
    </>
  )
}
