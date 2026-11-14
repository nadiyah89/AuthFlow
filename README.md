# AuthFlow

A modern, full-stack authentication web application demonstrating industry-standard session management, secure credential handling, and deployment architecture.

## Tech Stack

- **Backend**: ASP.NET Core Web API (.NET 10, C#)
- **Data Access**: Entity Framework Core 10, Npgsql (PostgreSQL 17)
- **Password Security**: ASP.NET Core Identity `PasswordHasher<User>` (PBKDF2 with HMAC-SHA512)
- **Session Security**: Signed JWT access tokens (15m) + Rotating HttpOnly Refresh-Token cookies (7d) with PostgreSQL row-level concurrency protection
- **CSRF Defense**: Origin/Referer verification + custom request header check on cookie-authenticated mutating endpoints
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
│       └── AuthFlow.IntegrationTests/# 36 PostgreSQL integration, HTTP pipeline & CSRF tests
├── frontend/
│   ├── src/
│   ├── vite.config.ts            # Vite config with local /api dev proxy
│   └── vercel.json               # Production reverse-proxy rewrites
├── docs/
│   └── ARCHITECTURE.md           # Architectural Design & Interview Learning Notes
└── .gitignore                    # Comprehensive Exclusion Rules
```

---

## Running Automated Tests

AuthFlow includes 36 automated unit and integration tests verifying database constraints, token rotation, reuse detection, concurrent serialization, CSRF defenses, and HTTP pipelines:

```bash
cd backend
dotnet test
```

---

## Manual API Testing Instructions

### 1. Start Backend API
```bash
cd backend
dotnet run --project src/AuthFlow.Api/AuthFlow.Api.csproj
```
The API listens on `http://localhost:5202`.

### 2. User Registration
```bash
curl -i -X POST http://localhost:5202/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "fullName": "Demo User",
    "email": "demo@example.com",
    "password": "P@ssword123!Secure",
    "confirmPassword": "P@ssword123!Secure"
  }'
```
Returns `201 Created` with JSON payload and `Set-Cookie: refreshToken=...; Path=/api/auth; HttpOnly`.

### 3. User Login
```bash
curl -i -X POST http://localhost:5202/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "demo@example.com",
    "password": "P@ssword123!Secure"
  }'
```
Returns `200 OK` with JSON payload and new refresh token cookie.

### 4. Protected User Profile (`GET /api/auth/me`)
```bash
curl -i -X GET http://localhost:5202/api/auth/me \
  -H "Authorization: Bearer <ACCESS_TOKEN>"
```
Returns `200 OK` with user profile JSON.

### 5. Refresh Session (`POST /api/auth/refresh`)
```bash
curl -i -X POST http://localhost:5202/api/auth/refresh \
  -H "Origin: http://localhost:5173" \
  -H "X-Requested-With: XMLHttpRequest" \
  -H "Cookie: refreshToken=<REFRESH_COOKIE>"
```
Rotates the token, returns `200 OK` with new access token, and sets new rotated refresh cookie.

### 6. Logout (`POST /api/auth/logout`)
```bash
curl -i -X POST http://localhost:5202/api/auth/logout \
  -H "Origin: http://localhost:5173" \
  -H "X-Requested-With: XMLHttpRequest" \
  -H "Cookie: refreshToken=<REFRESH_COOKIE>"
```
Revokes the refresh token in PostgreSQL and clears the cookie.

---

## Progress Status

- **Phase 0 (Architecture & Discovery)**: Complete & Approved.
- **Phase 1 (Workspace & Foundation)**: Complete & Approved.
- **Phase 2 (Database Modeling & Migrations)**: Complete & Approved.
- **Phase 3 (Backend Authentication — Register & Login)**: Complete & Approved.
- **Phase 4 (Session Management, Token Rotation & Protected Endpoints)**: Complete.
  - `POST /api/auth/refresh` with row-level locking (`SELECT ... FOR UPDATE`) and token family reuse detection.
  - `POST /api/auth/logout` with token revocation and cookie clearance.
  - `GET /api/auth/me` with JWT authentication and safe profile projection.
  - `CsrfProtectionMiddleware` validating custom headers and trusted origins.
  - All 36 integration and HTTP tests passing against live PostgreSQL.
