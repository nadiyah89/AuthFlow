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

## Local Setup

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
