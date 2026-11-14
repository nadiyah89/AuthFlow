# AuthFlow

A modern, full-stack authentication web application demonstrating industry-standard session management, secure credential handling, and deployment architecture.

## Tech Stack

- **Backend**: ASP.NET Core Web API (.NET 10, C#)
- **Data Access**: Entity Framework Core 10, Npgsql (PostgreSQL 17)
- **Password Security**: ASP.NET Core Identity `PasswordHasher<User>` (PBKDF2 with HMAC-SHA512)
- **Frontend**: React 19, TypeScript, Vite, React Router, Native Fetch API, CSS Modules
- **Authentication Strategy**: Short-lived in-memory JWT Access Tokens + Secure HttpOnly Refresh-Token Cookies
- **Hosting Targets**: Render (Backend), Vercel (Frontend), Managed PostgreSQL (Database)

---

## Project Structure

```
AuthFlow/
├── backend/
│   ├── AuthFlow.slnx
│   ├── src/
│   │   └── AuthFlow.Api/
│   │       ├── Controllers/          # API endpoints (Health, Auth)
│   │       ├── Data/                 # EF Core DbContext & Migrations
│   │       │   └── Migrations/       # 20261114134401_InitialCreate.cs
│   │       ├── DTOs/                 # Request & Response Contracts
│   │       ├── Middleware/           # Global Error Handling & Rate Limiting
│   │       ├── Models/               # Domain Entities (User, RefreshToken)
│   │       ├── Services/             # Auth, Token, and Hashing Services
│   │       ├── appsettings.json      # Base Configuration
│   │       └── Program.cs            # ASP.NET Core App Bootstrap
│   └── tests/
│       └── AuthFlow.IntegrationTests/# Live PostgreSQL integration test suite
├── frontend/
│   ├── src/
│   │   ├── components/           # Reusable UI & Route Guards
│   │   ├── context/              # AuthContext & Session State
│   │   ├── hooks/                # Custom React Hooks
│   │   ├── pages/                # Login, Register, Dashboard Pages
│   │   ├── services/             # Native Fetch Client & Interceptors
│   │   ├── styles/               # CSS Modules & Global Styles
│   │   └── types/                # TypeScript Interfaces
│   ├── vite.config.ts            # Vite config with local /api dev proxy
│   └── vercel.json               # Production reverse-proxy rewrites
├── docs/
│   └── ARCHITECTURE.md           # Detailed Architectural Design & Learning Guide
└── .gitignore                    # Comprehensive Exclusion Rules
```

---

## Database Management & Migrations

Migrations are controlled explicitly via the .NET EF CLI (not executed automatically on generic application startup):

```bash
# Add a new migration (when models change)
dotnet ef migrations add <MigrationName> --project src/AuthFlow.Api

# Apply migrations to database
dotnet ef database update --project src/AuthFlow.Api
```

---

## Running Integration Tests

AuthFlow includes integration tests that verify database constraints, unique indexes, cascading deletes, and UTC timestamps against live PostgreSQL:

```bash
cd backend
dotnet test
```

---

## Local Development Setup

### 1. Backend Setup
```bash
cd backend
dotnet restore
dotnet build
dotnet run --project src/AuthFlow.Api/AuthFlow.Api.csproj
```
The backend API starts on `http://localhost:5202`. Verify health check:
```bash
curl http://localhost:5202/api/health
```

### 2. Frontend Setup
```bash
cd frontend
npm install
npm run dev
```
The frontend dev server starts on `http://localhost:5173` with automatic `/api` proxying to `http://localhost:5202`.

---

## Progress Status

- **Phase 0 (Architecture & Discovery)**: Complete & Approved.
- **Phase 1 (Workspace & Foundation)**: Complete & Approved.
- **Phase 2 (Database Modeling & Migrations)**: Complete.
  - Entities defined: `User` and `RefreshToken`.
  - `AppDbContext` configured with Npgsql and Fluent API constraints.
  - `InitialCreate` migration generated, reviewed, and applied to `authflow_dev`.
  - All 6 integration tests passing against live PostgreSQL.
