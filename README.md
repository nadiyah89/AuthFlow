# AuthFlow

A modern, full-stack authentication web application demonstrating industry-standard session management, secure credential handling, and deployment architecture.

## Tech Stack

- **Backend**: ASP.NET Core Web API (.NET 10, C#)
- **Data Access**: Entity Framework Core 10, Npgsql (PostgreSQL)
- **Frontend**: React 19, TypeScript, Vite, React Router, Native Fetch API, CSS Modules
- **Authentication**: JWT access tokens in memory + HttpOnly refresh-token cookies
- **Hosting Targets**: Render (Backend), Vercel (Frontend), Managed PostgreSQL (Database)

---

## Project Structure

```
AuthFlow/
├── backend/
│   ├── AuthFlow.slnx
│   └── src/
│       └── AuthFlow.Api/
│           ├── Controllers/          # API endpoints (Health, Auth)
│           ├── Data/                 # EF Core DbContext & Migrations
│           ├── DTOs/                 # Request & Response Contracts
│           ├── Middleware/           # Global Error Handling & Rate Limiting
│           ├── Models/               # Domain Entities (User, RefreshToken)
│           ├── Services/             # Auth, Token, and Hashing Services
│           ├── appsettings.json      # Base Configuration
│           └── Program.cs            # ASP.NET Core App Bootstrap
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
│   └── ARCHITECTURE.md           # Detailed Architectural Design
└── .gitignore                    # Comprehensive Exclusion Rules
```

---

## Local Development Setup

### Prerequisites
- .NET 10 SDK
- Node.js (v20+) and npm
- PostgreSQL 17

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

## Current Status

- **Phase 0 (Architecture & Discovery)**: Complete & Approved.
- **Phase 1 (Workspace & Foundation)**: Complete.
  - Git initialized with strict `.gitignore`.
  - Backend project (.NET 10) created with EF Core 10 + Npgsql dependencies.
  - Frontend project (React + TS + Vite) scaffolded with `react-router-dom` and local proxy.
  - PostgreSQL port 5432 connectivity verified.
  - Health endpoint `/api/health` tested and operational.
