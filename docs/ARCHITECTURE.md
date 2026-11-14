# AuthFlow — Architecture Documentation

## 1. System Overview

AuthFlow is a full-stack authentication web application built using **ASP.NET Core Web API (.NET 10)** on the backend and **React + TypeScript + Vite** on the frontend, with **PostgreSQL** for persistence.

The application follows a clean 3-tier monolithic layered architecture designed for maintainability, security, and clarity for technical evaluation.

---

## 2. Layer Responsibilities

```
[ Frontend: React / Vite ]
       │
       ▼ (Relative /api requests via Vercel proxy / Vite dev proxy)
[ Presentation Layer: Controllers ]
       │ (Request/Response DTOs, Model Validation, Cookie Issuance)
       ▼
[ Business Layer: Services ]
       │ (IAuthService, ITokenService, PBKDF2 Password Hashing)
       ▼
[ Data Layer: EF Core ]
       │ (AppDbContext, Entities, Npgsql Provider)
       ▼
[ Database: PostgreSQL ]
```

### 1. Presentation Layer (`Controllers/`)
- Maps HTTP endpoints (`/api/auth/*`, `/api/health`).
- Validates incoming DTOs via DataAnnotations.
- Issues and clears `HttpOnly` refresh-token cookies.
- Returns RFC 7807 compliant problem details and standard HTTP status codes (`200 OK`, `201 Created`, `400 Bad Request`, `401 Unauthorized`, `409 Conflict`).

### 2. Business Layer (`Services/`)
- **`IAuthService` / `AuthService`**: Coordinates user registration, credential validation, refresh-token validation, and revocation.
- **`ITokenService` / `TokenService`**:
  - Issues short-lived signed JWT access tokens (15-minute expiration) with claims (`sub`, `email`, `jti`).
  - Generates cryptographically secure, high-entropy 64-byte random refresh tokens.
  - Hashes passwords using ASP.NET Core Identity's PBKDF2 implementation with HMAC-SHA512 and unique per-user 128-bit cryptographic salts.

### 3. Data Layer (`Data/`, `Models/`)
- **`AppDbContext`**: Entity Framework Core DbContext targeting PostgreSQL via Npgsql.
- **`User` Entity**: Stores user identification, email (unique index), password hash, and metadata.
- **`RefreshToken` Entity**: Stores hashed refresh token, associated user ID, expiration timestamp, created timestamp, and revocation status.
- **Controlled Migrations**: EF Core migrations are executed explicitly via CLI (`dotnet ef database update`) or dedicated migration scripts, **not** automatically on every application startup.

---

## 3. Authentication & Session Strategy

### Dual-Token Pattern
- **Access Token (JWT)**:
  - Lifespan: 15 minutes.
  - Transmission: `Authorization: Bearer <token>` HTTP header.
  - Storage: In-memory in the React `AuthContext` (never in `localStorage` or `sessionStorage` to mitigate token harvesting from XSS).
- **Refresh Token**:
  - Lifespan: 7 days.
  - Transmission: `HttpOnly; Secure; SameSite=Lax; Path=/api/auth` cookie.
  - Function: Prevents JavaScript from reading the raw cookie value directly, significantly raising the bar against client-side script inspection.

### Refresh Token Concurrency & Deduplication
To prevent race conditions where multiple parallel frontend requests (e.g., initial page load firing multiple data calls after an access token expires) trigger duplicate simultaneous refresh requests:
- The frontend API client maintains a shared `refreshPromise` singleton.
- When an expired token is detected (`401 Unauthorized`), the first request initiates the `/api/auth/refresh` call.
- Any concurrent requests queue behind this identical promise, awaiting the single new access token rather than dispatching duplicate refresh mutations.

---

## 4. Deployment Architecture

- **Frontend**: Hosted on Vercel.
- **Backend**: Hosted on Render as a Web Service.
- **Database**: Managed PostgreSQL (e.g., Neon or Render PostgreSQL).

### Same-Origin Vercel Rewrite
To ensure seamless cookie handling without third-party cookie blocking:
```json
{
  "rewrites": [
    {
      "source": "/api/:path*",
      "destination": "https://<BACKEND-HOST>.onrender.com/api/:path*"
    }
  ]
}
```
- The browser dispatches requests to its own origin (`/api/...`).
- The rewrite proxies requests securely over HTTPS to Render.
- Cookies issued on `/api/auth` remain first-party to the browser.
- In development, the Vite development proxy (`vite.config.ts`) mirrors this exact behavior pointing to `http://localhost:5202`.

---

## 5. Security & Configuration Boundaries

1. **No Hardcoded Secrets**: Secrets (JWT signing key, database connection strings) are loaded via environment variables in production and `appsettings.Development.json` (or User Secrets) locally.
2. **Git Hygiene**: Build artifacts (`bin/`, `obj/`, `dist/`), dependency folders (`node_modules/`), and sensitive configuration files (`*.env`, `appsettings.Production.json`) are excluded via `.gitignore`.
