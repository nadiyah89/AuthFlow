import React from 'react'
import styles from '../styles/layout.module.css'

interface LoadingSpinnerProps {
  message?: string
}

export const LoadingSpinner: React.FC<LoadingSpinnerProps> = ({ message = 'Loading...' }) => {
  return (
    <div className={styles.spinnerContainer} role="status" aria-live="polite">
      <div className={styles.spinner} />
      <p className={styles.spinnerText}>{message}</p>
    </div>
  )
}
