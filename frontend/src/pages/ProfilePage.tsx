import React from 'react'
import { Link } from 'react-router-dom'
import { Navbar } from '../components/Navbar'
import { useAuth } from '../hooks/useAuth'
import styles from '../styles/dashboard.module.css'

export const ProfilePage: React.FC = () => {
  const { user } = useAuth()

  const memberSince = user?.createdAtUtc
    ? new Date(user.createdAtUtc).toLocaleDateString('en-US', {
        year: 'numeric',
        month: 'long',
        day: 'numeric',
      })
    : 'Recently'

  const initials = user?.fullName
    ? user.fullName
        .split(' ')
        .map((n) => n[0])
        .join('')
        .toUpperCase()
        .slice(0, 2)
    : 'U'

  return (
    <>
      <Navbar />
      <main className={styles.dashboardContainer}>
        <div className={styles.profileWrapper}>
          <section className={styles.card}>
            <div className={styles.profileHeader}>
              <div className={styles.avatarCircle}>{initials}</div>
              <div className={styles.profileInfo}>
                <h1 className={styles.profileName}>{user?.fullName}</h1>
                <p className={styles.profileEmail}>{user?.email}</p>
              </div>
              <div className={styles.badgeSuccessLight}>
                <span className={styles.statusDot} />
                <span>Active Account</span>
              </div>
            </div>

            <div className={styles.infoList}>
              <div className={styles.infoItem}>
                <span className={styles.infoKey}>Full Name</span>
                <span className={styles.infoVal}>{user?.fullName}</span>
              </div>

              <div className={styles.infoItem}>
                <span className={styles.infoKey}>Email Address</span>
                <span className={styles.infoVal}>{user?.email}</span>
              </div>

              <div className={styles.infoItem}>
                <span className={styles.infoKey}>Member Since</span>
                <span className={styles.infoVal}>{memberSince}</span>
              </div>

              <div className={styles.infoItem}>
                <span className={styles.infoKey}>Account Status</span>
                <span className={styles.infoVal} style={{ color: 'var(--color-success)', fontWeight: 600 }}>
                  Active Account
                </span>
              </div>

              <div className={styles.infoItem}>
                <span className={styles.infoKey}>Authentication</span>
                <span className={styles.infoVal}>Password protected</span>
              </div>
            </div>

            <div className={styles.actionRow} style={{ marginTop: '1.75rem' }}>
              <Link to="/dashboard" className={styles.actionBtn}>
                ← Back to Dashboard
              </Link>
            </div>
          </section>
        </div>
      </main>
    </>
  )
}
