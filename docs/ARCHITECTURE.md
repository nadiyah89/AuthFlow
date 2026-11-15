# AuthFlow — Architecture Documentation

## 1. System Overview

AuthFlow is a production-grade full-stack authentication web application built using **ASP.NET Core Web API (.NET 10)** on the backend, **React 19 + TypeScript + Vite** on the frontend, and **PostgreSQL 17** for data persistence.

The application follows a clean 3-tier monolithic layered architecture on the backend coupled with an in-memory session management system on the frontend.

---

## 2. Layer Responsibilities & Technical Concepts

```
┌────────────────────────────────────────────────────────┐
│               Frontend: React 19 / Vite                │
│  - Pages: Register, Login, Dashboard, Profile          │
│  - AuthContext & useAuth Hook                          │
│  - ApiClient (Relative /api, Bearer Injection,         │
│    Single-Flight Token Refresh Interceptor)            │
└───────────────────────────┬────────────────────────────┘
                            │
               HTTPS /api/* (Vercel Proxy / Vite Proxy)
                            ▼
┌────────────────────────────────────────────────────────┐
│            ASP.NET Core Web API (.NET 10)              │
│                                                        │
│  [Middleware Pipeline]                                 │
│  1. GlobalExceptionHandlerMiddleware (RFC 7807)       │
│  2. UseCors (Origins Whitelist)                        │
│  3. CsrfProtectionMiddleware (Header & Origin Check)  │
│  4. UseRateLimiter (Fixed-Window IP Throttling)        │
│  5. UseAuthentication & UseAuthorization (JWT)         │
│                                                        │
│  [Presentation Layer: Controllers]                     │
│  - AuthController (Register, Login, Refresh, Logout,   │
│    Me)                                                 │
│  - HealthController (Health Check)                     │
│                                                        │
│  [Business Layer: Services]                            │
│  - IAuthService / AuthService                          │
│  - ITokenService / TokenService                        │
│  - PasswordHasher<User> (PBKDF2 HMAC-SHA512)           │
│                                                        │
│  [Data Layer: Entity Framework Core 10]                │
│  - AppDbContext (PostgreSQL via Npgsql)                │
│  - Entities: User, RefreshToken                        │
└───────────────────────────┬────────────────────────────┘
                            │
                            ▼
               ┌───────────────────────────┐
               │  PostgreSQL 17 Database   │
               │  - Users Table            │
               │  - RefreshTokens Table    │
               │  - Row-Level Locking      │
               └───────────────────────────┘
```

---

## 3. End-to-End Request Lifecycles

### Complete Registration Lifecycle (React -> API -> PostgreSQL)
1. **User Action**: User enters Full Name, Email, Password, and Password Confirmation on `/register`.
2. **Client Validation**: React performs instant validation (email format, 8+ characters, uppercase, lowercase, number, special symbol, password match).
3. **HTTP Dispatch**: Native Fetch client dispatches `POST /api/auth/register` with `credentials: "include"`, `X-Requested-With: XMLHttpRequest`, and `X-AuthFlow-Client: Web`.
4. **Proxy**: Vite (in local development) or Vercel (in production) rewrites `/api/auth/register` to the backend.
5. **ASP.NET Core Middleware**:
   - `GlobalExceptionHandlerMiddleware` wraps execution in try/catch.
   - `CsrfProtectionMiddleware` validates presence of custom header and trusted origin.
   - `RateLimiter` ensures request limit is not exceeded.
6. **Controller**: `AuthController.Register` binds `RegisterRequestDto`. DataAnnotation attributes enforce server-side model constraints.
7. **Service Layer (`AuthService.RegisterAsync`)**:
   - Normalizes email (`Trim().ToUpperInvariant()`).
   - Checks if `Users` table already has `NormalizedEmail`.
   - Hashes password using `PasswordHasher<User>` (PBKDF2 with HMAC-SHA512 and 128-bit unique cryptographic salt).
   - Generates high-entropy 64-byte random refresh token and computes its SHA-256 hash.
   - Saves `User` and `RefreshToken` entities in PostgreSQL. Catches any concurrent database collisions (`PostgresException 23505`) and maps them to `409 Conflict`.
   - Generates short-lived signed JWT access token (15 mins) with claims (`sub`, `email`, `jti`, `name`).
8. **HTTP Response**:
   - Sets `Set-Cookie: refreshToken=...; Path=/api/auth; HttpOnly; SameSite=Lax; Secure`.
   - Returns `201 Created` with JSON payload `{ accessToken, expiresInMinutes: 15, user: { id, fullName, email } }`.
9. **React State Update**: `AuthContext` receives the payload, stores the access token in memory (`ApiClient.accessToken`), updates user state, and navigates to `/dashboard`.

---

### Complete Login & Session Restoration Lifecycle
1. **Login**: User enters email and password -> `POST /api/auth/login`.
   - If unknown email: performs constant-time dummy password verification to neutralize timing-based user enumeration.
   - If password incorrect: increments `AccessFailedCount`. If $\ge 5$, sets `LockoutEndUtc = DateTime.UtcNow.AddMinutes(15)` and returns `423 Locked`.
   - If correct: resets `AccessFailedCount = 0`, rotates refresh token in PostgreSQL, returns JWT access token, and sets new HttpOnly cookie.
2. **Page Refresh / Session Restoration**:
   - When the user refreshes the browser, in-memory state is cleared.
   - `AuthContext` mounts and automatically calls `POST /api/auth/refresh`.
   - The browser automatically sends the HttpOnly `refreshToken` cookie.
   - PostgreSQL locks the token row with `SELECT ... FOR UPDATE`, marks it revoked, inserts a successor token with the same `FamilyId`, and commits.
   - New JWT access token and rotated cookie are returned.
   - The user remains logged in seamlessly without seeing a login screen.

---

## 4. Frontend Concurrency & Single-Flight Refresh

### Single-Flight Refresh Architecture
When an access token expires while multiple components or parallel requests are in flight (e.g. initial dashboard load fetching profile and stats):
1. Both requests encounter a `401 Unauthorized` response.
2. The first request detects `!this.refreshPromise` and initiates a single `POST /api/auth/refresh` operation:
   ```ts
   this.refreshPromise = this.refresh();
   ```
3. The second request detects that `this.refreshPromise` is already pending and awaits the exact same promise!
4. Exactly **one** refresh network request is sent to the backend.
5. Once resolved, both original requests are replayed with the new access token.
6. This eliminates race conditions and prevents triggering backend reuse detection.

### Multi-Tab Synchronization
AuthFlow uses the browser's `BroadcastChannel` API (`authflow_session_channel`). When a user logs in or out in one tab, all other open tabs immediately synchronize their authentication state without requiring manual reloads.

---

## 5. Security & Storage Boundaries

| Credential | Storage Location | Accessible to JS | Mitigated Vulnerabilities |
| :--- | :--- | :--- | :--- |
| **Access Token** | React API Client in-memory variable | Yes (internal memory only) | Mitigates persistent token harvesting via XSS (never in localStorage or sessionStorage). |
| **Refresh Token** | Browser Cookie (`HttpOnly; Secure; SameSite=Lax; Path=/api/auth`) | **No** | Mitigates XSS script inspection; strictly scoped to `/api/auth`. |
| **Refresh Token in DB** | SHA-256 Hash in `RefreshTokens.TokenHash` | N/A | Raw tokens never touch database tables. |
| **Passwords** | `Users.PasswordHash` (PBKDF2 HMAC-SHA512) | N/A | Salted and hashed with 100,000+ iterations. |
| **CSRF Defense** | Header `X-Requested-With` + Strict Origin matching | N/A | Rejects unauthorized cross-origin state changes; forbids suffix-based origin attacks. |

---

## 6. Production Deployment & Cold-Start Resilience

### Production CORS & CSRF Configuration
In production environments (e.g., Render backend + Vercel frontend), allowed origins must be supplied via environment variables without hardcoding secrets:
- **Render Dashboard Environment Variable:**
  `CorsSettings__AllowedOrigins__0 = https://your-app.vercel.app`
- In ASP.NET Core, this configures `CorsSettings:AllowedOrigins` array index 0.
- `CsrfProtectionMiddleware` and `Program.cs` automatically load and enforce these origins, requiring exact scheme, hostname, and port matches (preventing DNS suffix attacks such as `attacker-yourapp.vercel.app`).
- In local development (`ASPNETCORE_ENVIRONMENT=Development`), local dev ports (`http://localhost:5173`, `http://localhost:5202`, etc.) are automatically trusted.

### Render Free-Tier Cold-Start Handling
When a free-tier Render container spins down after inactivity, initial requests may receive an HTTP 502 Bad Gateway or 504 Gateway Timeout while the instance starts up:
- The frontend `ApiClient` intercepts non-JSON and HTML responses.
- Raw HTML error pages are sanitized and mapped to a readable user-facing message:
  `"The backend server is starting up or temporarily unreachable. Please retry in a few moments."`
- Raw HTML is never exposed to users or injected into error banners.
