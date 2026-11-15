# AuthFlow

A full-stack authentication application built with ASP.NET Core Web API and React, demonstrating secure authentication, JWT access tokens, refresh-token rotation, and session management.

## Tech Stack

- **Backend:** C#, ASP.NET Core Web API (.NET 10)
- **Database:** PostgreSQL 17, Entity Framework Core 10, Npgsql
- **Security:** PasswordHasher, JWT, HttpOnly cookies, refresh-token rotation, CSRF protection
- **Frontend:** React 19, TypeScript, Vite, React Router
- **Deployment:** Render (Backend & Database), Vercel (Frontend)

## Key Features

- User registration and login with server-side validation.
- Secure password hashing using ASP.NET Core Identity.
- JWT access tokens with a 15-minute lifetime.
- Refresh tokens stored in HttpOnly cookies with a 7-day lifetime.
- SHA-256 hashed refresh tokens stored in PostgreSQL.
- Refresh-token rotation and reuse detection.
- Account lockout after 5 consecutive failed login attempts.
- In-memory access-token storage (no localStorage or sessionStorage).
- Automatic token refresh with a single-flight request mechanism.
- Multi-tab session synchronization using BroadcastChannel.
- Protected routes and authenticated user profile.
- CSRF checks and centralized exception handling.

## Project Structure

```text
AuthFlow/
├── backend/
│   ├── src/AuthFlow.Api/
│   │   ├── Controllers/
│   │   ├── Data/
│   │   ├── DTOs/
│   │   ├── Middleware/
│   │   ├── Models/
│   │   ├── Services/
│   │   └── Program.cs
│   └── tests/
│       ├── AuthFlow.IntegrationTests/
│       └── e2e-flow.mjs
├── frontend/
│   ├── src/
│   │   ├── components/
│   │   ├── context/
│   │   ├── hooks/
│   │   ├── pages/
│   │   ├── services/
│   │   └── styles/
│   ├── vite.config.ts
│   └── vercel.json
└── docs/
    └── ARCHITECTURE.md
```

## Authentication Flow

1. User registers or logs in.
2. Backend validates credentials and verifies the password.
3. Backend generates a JWT access token and refresh token.
4. Access token is returned to the frontend and kept in memory.
5. Refresh token is sent through an HttpOnly cookie; only its hash is stored in the database.
6. When the access token expires, the frontend requests a refresh.
7. Backend validates and rotates the refresh token.
8. Logout revokes the refresh-token session.

<<<<<<< HEAD
## Local Setup
=======
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
5. **CSRF Protection & Cold-Start Resilience**:
   - Custom anti-CSRF request header requirement (`X-Requested-With: XMLHttpRequest`).
   - Strict Origin and Referer validation enforcing exact scheme, host, and port matching (preventing DNS suffix attacks).
   - Defensive client-side error parsing that sanitizes gateway timeouts and cold-start HTML responses.
>>>>>>> c99542e (fix: improve auth error handling and CSRF origin validation)

**Backend**
```bash
cd backend
dotnet restore
dotnet run --project src/AuthFlow.Api/AuthFlow.Api.csproj
```

**Frontend**
```bash
cd frontend
npm install
npm run dev
```

- Frontend: `http://localhost:5173`
- Backend: `http://localhost:5202`

## Testing

```bash
cd backend
dotnet test
```

```bash
node backend/tests/e2e-flow.mjs
```

```bash
cd frontend
npm run build
```

Reported results: 36 backend integration tests and 11 end-to-end tests passing.

## Deployment

- **Frontend:** Vercel
- **Backend:** Render
- **Database:** Managed PostgreSQL on Render

## Project Status

- Architecture and foundation: Complete
- Database modeling and migrations: Complete
- Backend authentication: Complete
- Session management and token rotation: Complete
- React frontend and backend integration: Complete

**AuthFlow demonstrates practical full-stack development, secure authentication, API integration, database management, and deployment using modern technologies.**
