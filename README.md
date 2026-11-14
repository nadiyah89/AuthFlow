# AuthFlow

A modern, full-stack authentication web application demonstrating industry-standard session management, secure credential handling, and deployment architecture.

## Tech Stack

- **Backend**: ASP.NET Core Web API (.NET 10, C#)
- **Data Access**: Entity Framework Core 10, Npgsql (PostgreSQL 17)
- **Password Security**: ASP.NET Core Identity `PasswordHasher<User>` (PBKDF2 with HMAC-SHA512)
- **Token Security**: Signed JWT access tokens (15m) + Cryptographically secure HttpOnly Refresh-Token cookies (7d)
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
│   │       ├── Controllers/          # AuthController (Register, Login), HealthController
│   │       ├── Data/                 # EF Core DbContext & Migrations
│   │       ├── DTOs/                 # RegisterRequestDto, LoginRequestDto, AuthResponseDto
│   │       ├── Middleware/           # GlobalExceptionHandlerMiddleware
│   │       ├── Models/               # User, RefreshToken, JwtSettings
│   │       ├── Services/             # AuthService, TokenService
│   │       ├── appsettings.json      # Base Configuration
│   │       └── Program.cs            # ASP.NET Core App Bootstrap
│   └── tests/
│       └── AuthFlow.IntegrationTests/# 23 PostgreSQL integration & HTTP pipeline tests
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

AuthFlow includes 23 integration and unit tests covering database constraints, cascading deletes, password hashing, JWT claims and signatures, and HTTP endpoints:

```bash
cd backend
dotnet test
```

---

## Manual API Testing Instructions

### 1. Start the Backend API
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
    "fullName": "Jane Doe",
    "email": "jane@example.com",
    "password": "P@ssword123!Secure",
    "confirmPassword": "P@ssword123!Secure"
  }'
```
**Expected Response**: `201 Created` with JSON payload (`accessToken`, `user`) and `Set-Cookie: refreshToken=...; Path=/api/auth; HttpOnly`.

### 3. User Login
```bash
curl -i -X POST http://localhost:5202/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "jane@example.com",
    "password": "P@ssword123!Secure"
  }'
```
**Expected Response**: `200 OK` with JSON payload (`accessToken`, `user`) and `Set-Cookie: refreshToken=...; Path=/api/auth; HttpOnly`.

### 4. Duplicate Registration
Submitting the same email again returns `409 Conflict` with `ProblemDetails`.

### 5. Invalid Password
Submitting an invalid password returns `401 Unauthorized` with generic message `Invalid email or password.`.

---

## Progress Status

- **Phase 0 (Architecture & Discovery)**: Complete & Approved.
- **Phase 1 (Workspace & Foundation)**: Complete & Approved.
- **Phase 2 (Database Modeling & Migrations)**: Complete & Approved.
- **Phase 3 (Backend Authentication — Register & Login)**: Complete.
  - `POST /api/auth/register` with duplicate detection and concurrent DB collision handling.
  - `POST /api/auth/login` with lockout protection and constant-time enumeration mitigation.
  - `TokenService` issuing short-lived JWTs and cryptographically random refresh tokens.
  - `GlobalExceptionHandlerMiddleware` returning RFC 7807 `ProblemDetails`.
  - All 23 integration and HTTP tests passing against live PostgreSQL.
