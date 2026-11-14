# AuthFlow

A modern, full-stack authentication web application demonstrating industry-standard session management, secure credential handling, and deployment architecture.

## Tech Stack

- **Backend**: ASP.NET Core Web API (.NET 10, C#)
- **Data Access**: Entity Framework Core 10, Npgsql (PostgreSQL 17)
- **Password Security**: ASP.NET Core Identity `PasswordHasher<User>` (PBKDF2 with HMAC-SHA512)
- **Session Security**: Signed JWT access tokens (15m in memory) + Rotating HttpOnly Refresh-Token cookies (7d) with PostgreSQL row-level locking
- **CSRF Defense**: Custom header verification (`X-Requested-With`) + Origin/Referer whitelist
- **Frontend**: React 19, TypeScript, Vite, React Router, Native Fetch API, CSS Modules
- **Hosting Targets**: Render (Backend), Vercel (Frontend), Managed PostgreSQL (Database)

---

## Project Structure

```
AuthFlow/
├── backend/
│   ├── AuthFlow.slnx
│   ├── src/
│   │   └── AuthFlow.Api/
│   │       ├── Controllers/          # AuthController (Register, Login, Refresh, Logout, Me), HealthController
│   │       ├── Data/                 # EF Core DbContext & Migrations
│   │       ├── DTOs/                 # Register, Login, AuthResponse, UserProfile DTOs
│   │       ├── Middleware/           # GlobalExceptionHandlerMiddleware, CsrfProtectionMiddleware
│   │       ├── Models/               # User, RefreshToken, JwtSettings
│   │       ├── Services/             # AuthService, TokenService
│   │       ├── appsettings.json      # Base Configuration
│   │       └── Program.cs            # ASP.NET Core App Bootstrap
│   └── tests/
│       ├── AuthFlow.IntegrationTests/# 36 PostgreSQL integration, HTTP pipeline & CSRF tests
│       └── e2e-flow.mjs              # End-to-end integration test through Vite proxy
├── frontend/
│   ├── src/
│   │   ├── components/           # Navbar, ProtectedRoute, PublicOnlyRoute, LoadingSpinner
│   │   ├── context/              # AuthContext (Multi-tab BroadcastChannel & session management)
│   │   ├── hooks/                # useAuth hook
│   │   ├── pages/                # RegisterPage, LoginPage, DashboardPage, ProfilePage
│   │   ├── services/             # api.ts (Single-flight refresh queue & native fetch client)
│   │   ├── styles/               # auth.module.css, dashboard.module.css, layout.module.css, global.css
│   │   └── types/                # TypeScript interfaces
│   ├── vite.config.ts            # Vite config with local /api dev proxy
│   └── vercel.json               # Production reverse-proxy rewrites
├── docs/
│   └── ARCHITECTURE.md           # Architectural Design & Interview Learning Notes
└── .gitignore                    # Comprehensive Exclusion Rules
```

---

## Features Implemented

1. **User Registration**:
   - Full name, email, password, and password confirmation.
   - Client-side validation with real-time password requirement checklist.
   - Case-insensitive duplicate email detection.
   - Server-side validation with RFC 7807 `ProblemDetails`.
   - PBKDF2 with HMAC-SHA512 password hashing.
   - Automatic post-registration login with immediate dashboard redirection.
2. **User Login**:
   - Email and password authentication.
   - Password visibility toggle.
   - Generic invalid credentials error to mitigate user enumeration.
   - Account lockout protection after 5 consecutive failed attempts (15-minute lockout).
   - Inactive account handling.
3. **Session Management & Refresh Tokens**:
   - Short-lived JWT Access Tokens (15 mins) kept exclusively in frontend memory.
   - Zero tokens stored in `localStorage` or `sessionStorage`.
   - Cryptographically random 64-byte Refresh Tokens in `HttpOnly; Secure; SameSite=Lax; Path=/api/auth` cookies.
   - Database stores only SHA-256 hashes of refresh tokens.
   - Refresh token rotation upon every use with PostgreSQL row-level concurrency protection (`SELECT ... FOR UPDATE`).
   - Token family lineage tracking with automatic reuse detection (compromised tokens revoke the entire session family).
   - Single-flight refresh mechanism on the frontend to prevent duplicate refresh calls.
   - Multi-tab session synchronization via `BroadcastChannel`.
4. **Protected Routes & Profile**:
   - `/dashboard` displays authenticated user details, creation date, and session status.
   - `/profile` displays full verified account information from `/api/auth/me`.
   - Route guards automatically redirect unauthenticated users to `/login` and authenticated users to `/dashboard`.
5. **CSRF Protection**:
   - Custom anti-CSRF request header requirement (`X-Requested-With: XMLHttpRequest`).
   - Browser Origin and Referer validation against allowed domains.

---

## Local Development Startup

### 1. Start the Backend API
```bash
cd backend
dotnet restore
dotnet build
dotnet run --project src/AuthFlow.Api/AuthFlow.Api.csproj
```
The API starts on `http://localhost:5202`.

### 2. Start the Frontend
```bash
cd frontend
npm install
npm run dev
```
The frontend dev server starts on `http://localhost:5173` with automatic `/api` proxying to `http://localhost:5202`.

---

## Running Automated Tests

### Backend Unit & Integration Tests (36 tests against live PostgreSQL):
```bash
cd backend
dotnet test
```

### End-to-End Local Flow Test:
```bash
node backend/tests/e2e-flow.mjs
```

### Frontend TypeScript & Production Build:
```bash
cd frontend
npm run build
```

---

## Progress Status

- **Phase 0 (Architecture & Discovery)**: Complete & Approved.
- **Phase 1 (Workspace & Foundation)**: Complete & Approved.
- **Phase 2 (Database Modeling & Migrations)**: Complete & Approved.
- **Phase 3 (Backend Authentication — Register & Login)**: Complete & Approved.
- **Phase 4 (Session Management, Token Rotation & Protected Endpoints)**: Complete & Approved.
- **Phase 5 (Complete React Frontend & Backend Integration)**: Complete.
  - Complete React 19 + TypeScript + Vite frontend implemented with zero external UI libraries.
  - Register, Login, Dashboard, and Profile pages styled with responsive CSS Modules.
  - Centralized API client with in-memory tokens and single-flight refresh queue.
  - Multi-tab session synchronization via `BroadcastChannel`.
  - All 36 backend tests and 11 end-to-end integration flow tests verified passing.
