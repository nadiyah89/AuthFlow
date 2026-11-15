# AuthFlow

AuthFlow is a modern, production-grade full-stack authentication web application demonstrating industry-standard session management, secure credential handling, and cloud deployment architecture.

- **Production Frontend:** [https://auth-flow-coral-ten.vercel.app](https://auth-flow-coral-ten.vercel.app)
- **Production Backend API:** [https://authflow-api-tjev.onrender.com](https://authflow-api-tjev.onrender.com)
- **Backend Health Check:** [https://authflow-api-tjev.onrender.com/api/health](https://authflow-api-tjev.onrender.com/api/health)

---

## 1. Project Overview

AuthFlow implements a clean, layered authentication architecture addressing real-world security challenges:
- **Stateless Access Tokens:** Short-lived JWTs (15 minutes) kept exclusively in frontend memory, eliminating persistent token theft via XSS (never stored in `localStorage` or `sessionStorage`).
- **Secure Refresh Tokens:** High-entropy 64-byte tokens stored in `HttpOnly; Secure; SameSite=Lax; Path=/api/auth` cookies, stored in PostgreSQL as SHA-256 hashes.
- **Concurrency & Rotation:** Every refresh request issues a new token pair and rotates the database record using PostgreSQL row-level locks (`SELECT ... FOR UPDATE`), preventing race conditions.
- **Lineage Tracking & Reuse Detection:** If an already revoked token is replayed, the entire token family is immediately revoked to stop credential abuse.
- **Double-Layer CSRF Defense:** Enforces custom headers (`X-Requested-With`) combined with strict origin, host, and port matching.
- **Resilient Client:** Single-flight refresh lock coalesces concurrent 401s, multi-tab `BroadcastChannel` synchronizes sessions, and automated error sanitization cleanly handles cloud gateway cold starts.

---

## 2. Core Features

- **User Registration:** Client-side real-time password complexity checklist, case-insensitive duplicate email detection, and automatic post-registration login with dashboard redirection.
- **User Login:** Email and password authentication with constant-time dummy password hashing for unknown users to neutralize timing-based user enumeration.
- **Account Lockout:** Locks accounts for 15 minutes after 5 consecutive failed attempts (`423 Locked`).
- **Session Management:** In-memory access token storage, silent background session restoration on page reload, single-flight refresh queue, and multi-tab synchronization.
- **Protected Routes:** Route guards (`ProtectedRoute` / `PublicOnlyRoute`) managing client-side navigation.
- **User Profile:** Dedicated profile screen displaying verified account metadata from `/api/auth/me`.
- **Idempotent Logout:** Database session revocation, cookie clearance, and cross-tab logout broadcast.
- **Gateway Cold-Start Sanitization:** Catches cloud reverse-proxy 502/503/504 gateway timeouts and converts them to clear user guidance instead of raw HTML error banners.

---

## 3. Technology Stack

### Backend
- **Language & Runtime:** C#, .NET 10 SDK (`net10.0`)
- **Framework:** ASP.NET Core Web API
- **Data Access:** Entity Framework Core 10 (`Microsoft.EntityFrameworkCore 10.0.0`)
- **Database Driver:** Npgsql PostgreSQL Provider (`Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0`)
- **Authentication:** `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0`
- **Password Hashing:** ASP.NET Core Identity `PasswordHasher<User>` (PBKDF2 with HMAC-SHA512)
- **Containerization:** Multi-stage Dockerfile (`mcr.microsoft.com/dotnet/aspnet:10.0`)

### Database
- **Engine:** PostgreSQL 17 (Managed PostgreSQL on Render)
- **Schema Management:** EF Core Code-First Migrations

### Frontend
- **Framework & Libraries:** React 19 (`^19.3.1`), TypeScript (`~7.0.2`), Vite (`^8.3.6`)
- **Routing:** React Router (`^7.18.5`)
- **Styling:** Modular CSS Modules with responsive mobile layouts
- **HTTP Client:** Native browser `fetch` with centralized `ApiClient` wrapper

### Deployment & Hosting
- **Frontend SPA:** Vercel (with server-side reverse-proxy rewrites)
- **Backend API:** Render Web Service (containerized Docker execution)
- **Database:** Render Managed PostgreSQL

---

## 4. System Architecture

```text
┌────────────────────────────────────────────────────────────────────────┐
│                        Frontend: React 19 / Vite                       │
│  - Pages: Register, Login, Dashboard, Profile                          │
│  - AuthContext & useAuth Hook (State & Multi-Tab BroadcastChannel)     │
│  - ApiClient (In-Memory Token, Single-Flight Refresh, Error Sanitizer) │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                       HTTPS /api/* │ (Vercel Proxy / Vite Proxy)
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                     ASP.NET Core Web API (.NET 10)                     │
│                                                                        │
│  [Middleware Pipeline]                                                 │
│  1. ForwardedHeadersOptions (Render Reverse Proxy SSL Termination)     │
│  2. GlobalExceptionHandlerMiddleware (RFC 7807 ProblemDetails)         │
│  3. UseCors (Dynamic Environment Origin Whitelist)                     │
│  4. CsrfProtectionMiddleware (Custom Headers + Exact Origin Match)    │
│  5. UseRateLimiter (Fixed-Window Throttling)                           │
│  6. UseAuthentication & UseAuthorization (JWT Bearer)                  │
│                                                                        │
│  [Presentation Layer]                                                  │
│  - AuthController (Register, Login, Refresh, Logout, Me)               │
│  - HealthController (Service Status & Environment Diagnostic)          │
│                                                                        │
│  [Business Layer]                                                      │
│  - AuthService & TokenService                                          │
│  - PasswordHasher<User> (PBKDF2 HMAC-SHA512)                           │
│                                                                        │
│  [Data Access Layer]                                                   │
│  - AppDbContext (PostgreSQL via Npgsql)                                │
│  - Entities: User, RefreshToken (Row-Level Locking)                    │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
                       ┌─────────────────────────┐
                       │  PostgreSQL 17 Database │
                       │  - Users Table          │
                       │  - RefreshTokens Table  │
                       └─────────────────────────┘
```

---

## 5. Folder Structure

```text
AuthFlow/
├── backend/
│   ├── AuthFlow.slnx                     # .NET Solution File
│   ├── Dockerfile                        # Multi-Stage Production Container Build
│   ├── .dockerignore                     # Docker Build Exclusions
│   ├── src/
│   │   └── AuthFlow.Api/
│   │       ├── Controllers/              # AuthController, HealthController
│   │       ├── Data/                     # AppDbContext & EF Core Migrations
│   │       ├── DTOs/                     # Register, Login, AuthResponse, UserProfile DTOs
│   │       ├── Middleware/               # GlobalExceptionHandler, CsrfProtection
│   │       ├── Models/                   # User, RefreshToken, JwtSettings
│   │       ├── Services/                 # AuthService, TokenService
│   │       ├── appsettings.json          # Base Configuration
│   │       ├── appsettings.Development.json
│   │       └── Program.cs                # Application Bootstrap & Middleware Pipeline
│   └── tests/
│       ├── AuthFlow.IntegrationTests/    # 39 xUnit Integration Tests against Live PostgreSQL
│       └── e2e-flow.mjs                  # 11-Step End-to-End Local Pipeline Test
├── frontend/
│   ├── index.html                        # Single Page Application Entry Point
│   ├── package.json                      # Dependencies & Scripts
│   ├── vite.config.ts                    # Vite Configuration with Local /api Proxy
│   ├── vercel.json                       # Vercel Production Reverse-Proxy Rewrites
│   ├── test-harness.html                 # Browser-Based Live Token Refresh Verification Harness
│   └── src/
│       ├── components/                   # Navbar, ProtectedRoute, PublicOnlyRoute, LoadingSpinner
│       ├── context/                      # AuthContext (Multi-Tab BroadcastChannel & State)
│       ├── hooks/                        # useAuth Hook
│       ├── pages/                        # RegisterPage, LoginPage, DashboardPage, ProfilePage
│       ├── services/                     # api.ts (ApiClient, Single-Flight Queue, Sanitizer)
│       ├── styles/                       # CSS Modules (auth, dashboard, layout, global.css)
│       ├── types/                        # TypeScript Interfaces (auth.types.ts)
│       ├── App.tsx                       # React Router Routes
│       └── main.tsx                      # DOM Mounting & Global Styles
└── docs/
    └── ARCHITECTURE.md                   # In-Depth Architectural Specification
```

---

## 6. Authentication Architecture

AuthFlow separates concerns cleanly between identification, authorization, and session retention:
- **Client Memory:** Holds the active JWT access token in JavaScript memory.
- **Browser Cookie Jar:** Holds the rotating refresh token inside an `HttpOnly` cookie.
- **Database:** Stores user records and salted SHA-256 hashes of refresh tokens.
- **Stateless Verification:** Microservices or protected routes validate access tokens cryptographically without database queries.
- **Stateful Rotation:** Refreshing an access token requires an atomic database query with row-level locks, verifying active session validity.

---

## 7. Registration and Login Flow

### Registration
1. User enters Full Name, Email, Password, and Confirm Password on `/register`.
2. React validates format in real-time (email format, 8+ chars, upper, lower, number, special character, password match).
3. `POST /api/auth/register` is dispatched with anti-CSRF headers.
4. Backend normalizes email (`Trim().ToUpperInvariant()`), verifies uniqueness, and hashes password using PBKDF2 HMAC-SHA512 with a unique 128-bit salt.
5. High-entropy 64-byte refresh token is generated; SHA-256 hash is saved to PostgreSQL.
6. Short-lived JWT access token (15 mins) is signed and returned in JSON response.
7. Refresh token is appended to response as an `HttpOnly` cookie.
8. Frontend stores access token in memory, sets user state, and navigates immediately to `/dashboard`.

### Login
1. User enters Email and Password on `/login`.
2. `POST /api/auth/login` is dispatched.
3. If email does not exist: backend executes constant-time dummy password verification to neutralize timing enumeration, returning generic `"Invalid email or password."`.
4. If account is locked (`LockoutEndUtc > UtcNow`): returns HTTP 423 Locked with remaining minutes.
5. If password check fails: increments `AccessFailedCount`. If $\ge 5$, sets 15-minute lockout and returns HTTP 423.
6. If password is valid: resets failed count, issues new rotating refresh token and JWT access token, sets cookie, and returns user profile.

---

## 8. JWT Access-Token Flow

- **Algorithm:** HMAC-SHA256 (`HS256`)
- **Lifetime:** 15 minutes (`AccessTokenExpirationMinutes: 15`)
- **Claims Included:**
  - `sub`: User ID (GUID)
  - `email`: Normalized user email
  - `jti`: Unique token identifier (GUID)
  - `name`: User full name
  - `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`: User ID
- **Validation Parameters:**
  - `ValidateIssuer = true` (`AuthFlow.Api`)
  - `ValidateAudience = true` (`AuthFlow.Client`)
  - `ValidateIssuerSigningKey = true` (Secret $\ge 32$ characters)
  - `ValidateLifetime = true`
  - `ClockSkew = TimeSpan.Zero` (Strict expiration enforcement)

---

## 9. Refresh-Token Rotation & Reuse Detection

- **Token Generation:** 64 cryptographically random bytes via `RandomNumberGenerator` (Base64URL-encoded).
- **Storage:** Only the SHA-256 hash is persisted in `RefreshTokens.TokenHash`.
- **Database Concurrency:** Rotations run inside an EF Core execution strategy and explicit database transaction using PostgreSQL row-level locks:
  ```sql
  SELECT * FROM "RefreshTokens" WHERE "TokenHash" = @tokenHash FOR UPDATE;
  ```
- **Successor Linking:** Old token is marked `RevokedAtUtc = UtcNow` and linked via `ReplacedByTokenId = successor.Id`. Successor token inherits the existing `FamilyId`.
- **Reuse Detection:** If a previously revoked refresh token is presented:
  1. The backend detects `tokenRecord.RevokedAtUtc.HasValue`.
  2. A security incident warning is logged.
  3. All tokens belonging to that `FamilyId` are immediately marked revoked in PostgreSQL.
  4. Returns `401 Unauthorized` and wipes the client cookie.

---

## 10. Session Management

| Scenario | System Behavior |
| :--- | :--- |
| **Page Refresh (F5)** | React memory clears. `AuthProvider` calls `api.refreshSingleFlight()`. Browser supplies HttpOnly cookie. Backend validates, rotates cookie, returns new access token. User remains logged in seamlessly. |
| **React StrictMode Double-Mount** | `AuthProvider` mounts, unmounts, and mounts again in development. The single-flight mechanism coalesces both calls into one in-flight Promise; only 1 network request is made, preventing premature token invalidation. |
| **Tab Reopen** | Opening the site in a new tab reads the valid cookie, executes silent single-flight refresh, and restores the session. |
| **Token Expiry (15m)** | Next API call receives HTTP 401. `ApiClient` intercepts 401, calls `refreshTokenSingleFlight()`, acquires new token, updates memory, and replays original call. |
| **Concurrent 401s** | Multiple parallel API calls receiving 401 share the identical refresh promise; only one refresh network request is sent to the server. |
| **Refresh Cookie Expiry (7d)** | Refresh returns 401. `ApiClient` clears memory, triggers auth failure callback, and redirects to `/login`. |
| **Logout** | Token revoked in DB, cookie cleared (`Expires = Yesterday`), memory cleared, logout broadcasted to other tabs via `SessionBroadcastManager`. |

---

## 11. Multi-Tab Synchronization & SessionBroadcastManager

AuthFlow coordinates multi-tab authentication state using a dedicated `SessionBroadcastManager` singleton wrapped around the native `BroadcastChannel` API (`authflow_session_channel`):
- **Singleton Architecture:** The underlying `BroadcastChannel` is maintained at module scope rather than bound to the React component lifecycle. This prevents channels from being prematurely closed during React re-renders or unmounts, avoiding `InvalidStateError` exceptions.
- **Clean Event Subscriptions:** Components subscribe to session events with `addEventListener('message')` and clean up with `removeEventListener('message')`, preserving the channel connection for the entire tab lifetime.
- **On Login in Tab A:** Tab A broadcasts `{ type: 'LOGIN', user }`. Tab B receives the event and updates its user state without requiring a page reload.
- **On Logout in Tab A:** Tab A broadcasts `{ type: 'LOGOUT' }`. Tab B clears its in-memory access token, resets user state to `null`, and redirects immediately to `/login`.
- **Fault Tolerance:** `postMessage` is guarded; if the browser channel is closing or unavailable, `SessionBroadcastManager` catches the exception and safely re-initializes.

---

## 12. Logout Flow

1. User clicks "Sign out" in the navigation bar.
2. `POST /api/auth/logout` is dispatched with anti-CSRF headers.
3. Backend extracts cookie, hashes token, sets `RevokedAtUtc = UtcNow` in PostgreSQL, and returns HTTP 200.
4. Response sends `Set-Cookie: refreshToken=; Expires=Thu, 01 Jan 1970 00:00:00 GMT; Path=/api/auth; Secure; HttpOnly; SameSite=Lax`.
5. Frontend clears in-memory access token (`api.setAccessToken(null)`), sets user state to `null`, and posts `LOGOUT` to `BroadcastChannel`.
6. Subsequent attempts to use the revoked cookie return HTTP 401.

---

## 13. CSRF and CORS Protection

### CSRF Defense
State-changing cookie-authenticated endpoints (`/api/auth/refresh` and `/api/auth/logout`) are protected by `CsrfProtectionMiddleware`:
1. **Custom Header Check:** Requires `X-Requested-With`, `X-AuthFlow-Client`, or `X-CSRF-Token`. Cross-origin HTML forms cannot attach custom headers without preflight approval.
2. **Strict Origin & Host Check:** Parses incoming `Origin` / `Referer` as a `Uri` and executes `IsSameOrigin()`:
   - Validates exact case-insensitive hostname equality (strictly forbidding suffix-based bypass attacks like `http://attacker-localhost` or `https://authflow.onrender.com.attacker.com`).
   - Validates exact port match (taking standard 80/443 defaults into account).
   - Validates scheme equality.

### CORS Configuration
- In Development: Automatically permits local development hosts (`http://localhost:5173`, `http://localhost:5202`, `https://localhost:7034`, `http://127.0.0.1:5173`, `http://127.0.0.1:5202`).
- In Production: Strictly loads authorized origins from `CorsSettings:AllowedOrigins` (injected via Render environment variable `CorsSettings__AllowedOrigins__0`).
- Credentials enabled: `AllowCredentials()` configured alongside explicit origins.

---

## 14. Security Features Summary

- **PBKDF2 Password Hashing:** 100,000 iterations with HMAC-SHA512 and unique cryptographic salts.
- **In-Memory JWT Tokens:** Eliminates token extraction risks via Cross-Site Scripting (XSS).
- **HttpOnly Cookies:** JavaScript cannot read refresh tokens.
- **PostgreSQL Row Locks:** Eliminates race conditions during token rotation.
- **Token Lineage Reuse Detection:** Prevents session hijacking from stolen refresh tokens.
- **Timing-Attack Neutralization:** Constant-time dummy verification on nonexistent accounts.
- **Account Lockout:** 15-minute lockout after 5 consecutive failures.
- **RFC 7807 ProblemDetails:** Standardized error reporting with zero stack trace leakage in production.
- **Gateway Cold-Start Sanitization:** Catches 502/504 gateway HTML pages and converts them to friendly notifications.

---

## 15. API Endpoints

### 1. Register User
- **Method & Path:** `POST /api/auth/register`
- **Request Body:**
  ```json
  {
    "fullName": "Jane Doe",
    "email": "jane@example.com",
    "password": "Password123!",
    "confirmPassword": "Password123!"
  }
  ```
- **Responses:**
  - `201 Created`: `{ "accessToken": "...", "expiresInMinutes": 15, "user": { "id": "...", "fullName": "Jane Doe", "email": "jane@example.com", "createdAtUtc": "..." } }` (+ `Set-Cookie: refreshToken=...`)
  - `400 Bad Request`: Validation failure (RFC 7807 ProblemDetails with `errors` dictionary)
  - `409 Conflict`: Duplicate email address

### 2. Login User
- **Method & Path:** `POST /api/auth/login`
- **Request Body:**
  ```json
  {
    "email": "jane@example.com",
    "password": "Password123!"
  }
  ```
- **Responses:**
  - `200 OK`: `{ "accessToken": "...", "expiresInMinutes": 15, "user": { ... } }` (+ `Set-Cookie: refreshToken=...`)
  - `400 Bad Request`: Missing credentials
  - `401 Unauthorized`: Invalid email or password
  - `403 Forbidden`: Account is inactive
  - `423 Locked`: Account locked due to repeated failed logins

### 3. Refresh Access Token
- **Method & Path:** `POST /api/auth/refresh`
- **Headers:** `X-Requested-With: XMLHttpRequest`, `Origin: https://auth-flow-coral-ten.vercel.app`
- **Cookie:** `refreshToken=...`
- **Responses:**
  - `200 OK`: `{ "accessToken": "...", "expiresInMinutes": 15, "user": { ... } }` (+ new rotated `Set-Cookie`)
  - `401 Unauthorized`: Missing, expired, or revoked token
  - `403 Forbidden`: CSRF validation failure

### 4. Logout User
- **Method & Path:** `POST /api/auth/logout`
- **Headers:** `X-Requested-With: XMLHttpRequest`
- **Cookie:** `refreshToken=...`
- **Responses:**
  - `200 OK`: `{ "message": "Logged out successfully." }` (+ expired `Set-Cookie`)
  - `403 Forbidden`: CSRF validation failure

### 5. Get Current User Profile
- **Method & Path:** `GET /api/auth/me`
- **Headers:** `Authorization: Bearer <jwt-token>`
- **Responses:**
  - `200 OK`: `{ "id": "...", "fullName": "Jane Doe", "email": "jane@example.com", "createdAtUtc": "..." }`
  - `401 Unauthorized`: Missing, expired, or malformed JWT

### 6. Health Check
- **Method & Path:** `GET /api/health`
- **Responses:**
  - `200 OK`: `{ "status": "Healthy", "service": "AuthFlow.Api", "environment": "Production", "timestampUtc": "..." }`

---

## 16. Database Schema and Relationships

```text
┌────────────────────────────────────────┐       ┌────────────────────────────────────────┐
│                 Users                  │       │             RefreshTokens              │
├────────────────────────────────────────┤       ├────────────────────────────────────────┤
│ Id (PK, UUID)                          │◄──┐   │ Id (PK, UUID)                          │
│ FullName (varchar 100, NOT NULL)       │   │   │ UserId (FK, UUID, NOT NULL) ───────────┘
│ Email (varchar 256, NOT NULL)          │   └───│ TokenHash (varchar 128, NOT NULL, UNIQUE)
│ NormalizedEmail (varchar 256, UNIQUE)  │       │ FamilyId (UUID, NOT NULL, INDEX)       │
│ PasswordHash (text, NOT NULL)          │       │ CreatedAtUtc (timestamptz, NOT NULL)   │
│ IsActive (boolean, DEFAULT true)       │       │ ExpiresAtUtc (timestamptz, NOT NULL)   │
│ AccessFailedCount (int, DEFAULT 0)     │       │ RevokedAtUtc (timestamptz, NULLABLE)   │
│ LockoutEndUtc (timestamptz, NULLABLE)  │       │ ReplacedByTokenId (UUID, NULLABLE)     │
│ CreatedAtUtc (timestamptz, NOT NULL)   │       └────────────────────────────────────────┘
│ UpdatedAtUtc (timestamptz, NOT NULL)   │
└────────────────────────────────────────┘
```

- **Cascade Delete:** Deleting a `User` automatically purges all associated `RefreshTokens`.
- **Unique Indexes:** `IX_Users_NormalizedEmail` enforces case-insensitive uniqueness; `IX_RefreshTokens_TokenHash` guarantees token uniqueness.
- **B-Tree Indexing & Query Complexity:** Lookups by `NormalizedEmail` and `TokenHash` leverage standard PostgreSQL B-Tree indexes, providing $O(\log n)$ search and retrieval complexity.

---

## 17. Environment Variables

### Backend Configuration (Render Web Service)
| Variable Name | Description | Example / Production Value |
| :--- | :--- | :--- |
| `PORT` | Dynamic port injected by Render edge router | `8080` (auto-bound by API) |
| `ConnectionStrings__DefaultConnection` | Managed PostgreSQL connection string | `Host=...;Database=...;Username=...;Password=...` |
| `JwtSettings__Secret` | Cryptographic key for HMAC-SHA256 ($\ge 32$ chars) | *(Set via Render Secret Environment Variable)* |
| `JwtSettings__Issuer` | JWT Token Issuer | `AuthFlow.Api` |
| `JwtSettings__Audience` | JWT Token Audience | `AuthFlow.Client` |
| `CorsSettings__AllowedOrigins__0` | Authorized production frontend origin | `https://auth-flow-coral-ten.vercel.app` |

---

## 18. Local Development Setup

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) (v20+)
- Local PostgreSQL instance running on port `5432`

### 1. Database Setup
Create the local development database:
```sql
CREATE DATABASE authflow_dev;
```
Ensure your connection string in `backend/src/AuthFlow.Api/appsettings.json` matches your local credentials:
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=authflow_dev;Username=postgres;Password=yourpassword"
}
```

### 2. Backend Startup
```bash
cd backend
dotnet restore
dotnet build
dotnet run --project src/AuthFlow.Api/AuthFlow.Api.csproj
```
The API starts on `http://localhost:5202` (Health check: `http://localhost:5202/api/health`).

### 3. Frontend Startup
```bash
cd frontend
npm install
npm run dev
```
The Vite development server starts on `http://localhost:5173` with automatic `/api` proxying to `http://localhost:5202`.

---

## 19. Testing Instructions

### Backend Unit & Integration Tests (39 Tests against live PostgreSQL):
```bash
cd backend
dotnet test
```

### End-to-End Local Flow Test (11 automated pipeline steps):
```bash
node backend/tests/e2e-flow.mjs
```

### Session Synchronization & BroadcastChannel Regression Suite:
```bash
node backend/tests/session-sync-regression.mjs
```

### Client Error Parsing & Resilience Suite:
```bash
node backend/tests/client-error-resilience.mjs
```

### Frontend TypeScript Verification & Production Build:
```bash
cd frontend
npm run build
```

### Live Browser Token-Refresh Verification Harness:
Open `http://localhost:5173/test-harness.html` in your browser while local servers are running to interactively verify the 5 stages of token refresh, 401 replay, and session revocation.

### Latest Verified Test Results
- **Backend Integration Tests:** 39 passed, 0 failed, 0 skipped (2.0s)
- **End-to-End Local Pipeline:** 11 / 11 passed
- **Session Sync & BroadcastChannel Suite:** 4 / 4 passed
- **Client Error Resilience Suite:** 5 / 5 passed
- **Frontend TypeScript & Build:** 0 errors, 0 warnings (220ms)
- **Browser Token Refresh Harness:** 5 / 5 stages passed

---

## 20. Production Deployment Architecture

- **Frontend Deployment (Vercel):**
  - Hosted at: `https://auth-flow-coral-ten.vercel.app`
  - Configured via `frontend/vercel.json`:
    - Rewrites `/api/:path*` to `https://authflow-api-tjev.onrender.com/api/:path*`.
    - Rewrites `/(.*)` to `/index.html` for client-side React Router navigation.
- **Backend Deployment (Render):**
  - Hosted at: `https://authflow-api-tjev.onrender.com`
  - Containerized via `backend/Dockerfile` with `ForwardedHeadersOptions` for SSL termination.
  - Authorized origin configured via `CorsSettings__AllowedOrigins__0 = https://auth-flow-coral-ten.vercel.app`.

---

## 21. Known Limitations & Future Enhancements

1. **Rate Limiter Partitioning:** The current rate limiter uses a global fixed-window pool of 30 requests/minute. In high-traffic production, this can be enhanced to partition by client IP using `PartitionedRateLimiter.Create<HttpContext, string>`.
2. **Refresh Token Pruning:** `RefreshTokens` records remain in the database for lineage audit history. An automated background worker (e.g. `IHostedService`) can be added to delete revoked or expired tokens older than 30 days.
3. **Render Free-Tier Spin-Down:** On free-tier hosting, Render sleeps containers after 15 minutes of inactivity. AuthFlow includes client-side sanitization to handle cold-start delays smoothly; migrating to a persistent paid instance eliminates spin-down latency entirely.
