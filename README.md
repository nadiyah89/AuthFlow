
# AuthFlow

A full-stack authentication application built with React, ASP.NET Core and PostgreSQL, demonstrating secure authentication, token rotation, session management and cloud deployment.

- **Live Application:** https://auth-flow-coral-ten.vercel.app
- **Backend API:** https://authflow-api-tjev.onrender.com
- **Health Check:** https://authflow-api-tjev.onrender.com/api/health

## 1. Features

- **User Registration:** Real-time password validation, case-insensitive duplicate email detection and automatic login.
- **Secure Login:** Password hashing, generic invalid-credential responses and account lockout after 5 failed attempts for 15 minutes.
- **JWT Authentication:** Short-lived access tokens (15 minutes), stored exclusively in frontend memory.
- **Refresh Token Rotation:** Cryptographically secure 64-byte refresh tokens, stored as SHA-256 hashes in PostgreSQL.
- **Reuse Detection:** Revokes the entire token family when a previously revoked refresh token is reused.
- **Session Restoration:** Automatically restores sessions after page reload using HttpOnly cookies.
- **Single-Flight Refresh:** Coalesces concurrent refresh requests to prevent duplicate token rotation.
- **Multi-Tab Synchronization:** Uses BroadcastChannel to synchronize login and logout across tabs.
- **Protected Routes:** `ProtectedRoute` and `PublicOnlyRoute` manage frontend navigation.
- **User Profile:** Displays authenticated account information through `/api/auth/me`.
- **Secure Logout:** Revokes refresh tokens, clears cookies and synchronizes logout across tabs.
- **CSRF & CORS Protection:** Custom request headers and strict origin validation.
- **Rate Limiting:** Fixed-window limit of 30 requests per minute.
- **Error Handling:** RFC 7807 ProblemDetails and sanitization of cloud gateway errors (502/503/504).

## 2. Technology Stack

| Layer | Technologies |
|---|---|
| Backend | C#, ASP.NET Core Web API, .NET 10 |
| Frontend | React 19, TypeScript, Vite |
| Routing | React Router 7 |
| Styling | CSS Modules |
| HTTP | Native Fetch API |
| Database | PostgreSQL 17 |
| ORM | Entity Framework Core 10 |
| Database Provider | Npgsql |
| Authentication | JWT Bearer |
| Password Hashing | ASP.NET Core Identity PasswordHasher (PBKDF2, HMAC-SHA512) |
| Deployment | Vercel, Render, Docker |

## 3. Architecture

```text
React Frontend (Vercel)
       |
       | HTTPS /api/*
       v
ASP.NET Core Web API (Render)
       |
       | Entity Framework Core / Npgsql
       v
PostgreSQL Database
```

**Backend Layers**
- **Controllers:** `AuthController`, `HealthController`
- **Services:** `AuthService`, `TokenService`
- **Data:** `AppDbContext`, EF Core migrations
- **Models:** `User`, `RefreshToken`, `JwtSettings`
- **Middleware:** Global exception handling, CSRF protection, CORS and rate limiting
- **Program.cs:** Application configuration and middleware pipeline

**Frontend Structure**
- `pages/`: Register, Login, Dashboard and Profile
- `components/`: Navbar, route guards and loading spinner
- `context/`: Authentication state and multi-tab synchronization
- `hooks/`: `useAuth`
- `services/`: Centralized API client, token refresh and error handling
- `types/`: TypeScript interfaces
- `App.tsx`: Application routes
- `main.tsx`: Application entry point

## 4. Authentication & Security

### Access Tokens
- JWT signed using HMAC-SHA256 (HS256).
- 15-minute lifetime.
- Validated using issuer, audience, signing key and expiration.
- Includes user ID, email, name and unique token identifier (`jti`).
- Stored only in JavaScript memory, never in `localStorage` or `sessionStorage`.

### Refresh Tokens
- Generated using 64 cryptographically random bytes.
- SHA-256 hashes stored in PostgreSQL.
- Raw token held in an `HttpOnly`, `Secure`, `SameSite=Lax` browser cookie.
- Cookie lifetime: 7 days; path: `/api/auth`.
- Rotated after every successful refresh.
- PostgreSQL row-level locking (`SELECT ... FOR UPDATE`) prevents concurrent rotation races.
- Token-family lineage enables reuse detection and family-wide revocation.

### Password Security
- ASP.NET Core Identity `PasswordHasher<User>`.
- PBKDF2 with HMAC-SHA512 and unique cryptographic salts.
- Failed-login counter and 15-minute lockout after 5 consecutive failures.
- Dummy password verification for unknown accounts helps reduce timing-based user enumeration.

### Session Storage

| Location | Stored Data |
|---|---|
| PostgreSQL | User records and hashed refresh tokens |
| Frontend Memory | Access JWT and authentication state |
| Browser Cookie | Raw refresh token (HttpOnly) |
| localStorage / sessionStorage | No authentication tokens |

### CSRF & CORS
- Custom request headers required for refresh and logout.
- Strict scheme, hostname and port validation.
- Production CORS uses explicitly configured allowed origins with credentials enabled.

## 5. Session Management

| Event | Behaviour |
|---|---|
| Page Refresh | Silent refresh restores the session |
| Access Token Expires | Refresh and retry original request |
| Concurrent Refresh Requests | Share one refresh promise |
| New Browser Tab | Session restored through refresh cookie |
| Login in Another Tab | BroadcastChannel synchronizes user state |
| Logout in Another Tab | Authentication state cleared and redirected |
| Refresh Token Expires | Session cleared and login required |

`SessionBroadcastManager` maintains a module-scoped BroadcastChannel with safe event subscription and cleanup.

## 6. Database Schema

The application uses two primary tables.

**Users**
- `Id` (UUID, Primary Key)
- `FullName`
- `Email`
- `NormalizedEmail` (Unique)
- `PasswordHash`
- `IsActive`
- `AccessFailedCount`
- `LockoutEndUtc`
- `CreatedAtUtc`
- `UpdatedAtUtc`

**RefreshTokens**
- `Id` (UUID, Primary Key)
- `UserId` (Foreign Key)
- `TokenHash` (Unique)
- `FamilyId` (Indexed)
- `CreatedAtUtc`
- `ExpiresAtUtc`
- `RevokedAtUtc`
- `ReplacedByTokenId`

User deletion cascades to associated refresh-token records. PostgreSQL B-tree indexes support email and token-hash lookups.

## 7. API Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| POST | `/api/auth/register` | Register and authenticate user |
| POST | `/api/auth/login` | Authenticate user |
| POST | `/api/auth/refresh` | Rotate refresh token and issue access token |
| POST | `/api/auth/logout` | Revoke session and clear cookie |
| GET | `/api/auth/me` | Get authenticated user profile |
| GET | `/api/health` | Check API health |

Common responses include `400`, `401`, `403`, `409` and `423`, depending on validation, authentication, authorization, duplicate accounts and account lockout.


## 8. Local Development

### Prerequisites
- .NET 10 SDK
- Node.js 20+
- PostgreSQL

### Database

Create the local database:

```sql
CREATE DATABASE authflow_dev;
```

Configure the connection string in `appsettings.json` or through an environment variable.

### Backend

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project src/AuthFlow.Api/AuthFlow.Api.csproj
```

API: `http://localhost:5202`

### Frontend

```bash
cd frontend
npm install
npm run dev
```

Frontend: `http://localhost:5173`

Vite proxies `/api` requests to the local backend.

## 9. Testing

**Backend Integration Tests**

```bash
dotnet test backend/tests/AuthFlow.IntegrationTests/AuthFlow.IntegrationTests.csproj
```

**Additional Verification Suites**

```bash
node backend/tests/e2e-flow.mjs
node backend/tests/session-sync-regression.mjs
node backend/tests/client-error-resilience.mjs
```

**Frontend Production Build**

```bash
cd frontend
npm run build
```

**Browser Token-Refresh Harness**

Open `http://localhost:5173/test-harness.html` while the local servers are running.

**Latest Reported Verification Results**
- Backend integration tests: 39/39 passed.
- End-to-end pipeline: 11/11 passed.
- Session synchronization: 4/4 passed.
- Client error resilience: 5/5 passed.
- Browser refresh harness: 5/5 passed.
- Frontend TypeScript and production build: successful.

## 10. Production Deployment

| Component | Platform |
|---|---|
| React Frontend | Vercel |
| ASP.NET Core API | Render |
| PostgreSQL | Render Managed PostgreSQL |
| Containerization | Docker |

- **Frontend:** https://auth-flow-coral-ten.vercel.app
- **Backend:** https://authflow-api-tjev.onrender.com

Vercel rewrites `/api/*` requests to the Render backend. React Router routes are served through the SPA fallback.

Production configuration uses environment variables for the database connection, JWT signing secret, issuer, audience and allowed CORS origins.

## 11. Known Limitations & Future Improvements

1. **Rate Limiting:** The current fixed-window limit is global (30 requests/minute); IP-based partitioning would improve scalability.
2. **Token Cleanup:** Add a background service to prune expired and revoked refresh-token records while retaining required audit history.
3. **Render Free Tier:** Backend cold starts can cause temporary delays; a persistent hosting instance would improve availability.

---

**AuthFlow demonstrates secure authentication, JWT-based authorization, refresh-token rotation, session lifecycle management, database persistence, frontend-backend integration and cloud deployment.**
