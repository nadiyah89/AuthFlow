# AuthFlow — Architecture Documentation

## 1. System Overview

AuthFlow is a full-stack authentication web application built using **ASP.NET Core Web API (.NET 10)** on the backend, **React 19 + TypeScript + Vite** on the frontend, and **PostgreSQL 17** for data persistence.

The application follows a clean 3-tier monolithic layered architecture designed for high maintainability, strict security, and complete clarity for technical evaluation and interviews.

---

## 2. Layer Responsibilities & Technical Concepts

```
[ Frontend: React / Vite ]
       │
       ▼ (Relative /api requests via Vercel rewrite / Vite dev proxy)
[ Presentation Layer: Controllers ]
       │ (Request/Response DTOs, Model Validation, Cookie Issuance)
       ▼
[ Business Layer: Services ]
       │ (IAuthService, ITokenService, PBKDF2 Password Hashing)
       ▼
[ Data Layer: Entity Framework Core ]
       │ (AppDbContext, Entities, Npgsql Provider)
       ▼
[ Database: PostgreSQL ]
```

### Educational Concepts: Interview Guide

#### What is an Entity?
An **Entity** is a C# domain class representing a business object with a distinct identity (`Id`). In EF Core, entities map directly to database tables. For example:
- `User` represents an individual registered user in the system (`Users` table).
- `RefreshToken` represents a temporary, rotating session credential tied to a user (`RefreshTokens` table).

#### What does `DbContext` do?
`AppDbContext` inherits from EF Core's `DbContext`. It acts as a combination of the **Unit of Work** and **Repository** design patterns:
1. **Connection Manager**: Opens and manages connections to the PostgreSQL database via Npgsql.
2. **Object-Relational Mapping (ORM)**: Translates LINQ queries into raw SQL and maps SQL tabular results back into strongly-typed C# objects.
3. **Change Tracker**: Tracks modifications (adds, updates, deletes) made to loaded entities and generates optimized transactional SQL when `SaveChangesAsync()` is invoked.
4. **Schema Blueprint**: Configures table schemas, relationships, constraints, and indexes through `OnModelCreating` via the Fluent API.

#### What do EF Core Migrations do?
**Migrations** provide version control for the database schema:
- When C# entity models change, running `dotnet ef migrations add <Name>` generates a C# migration script containing `Up()` and `Down()` operations.
- `dotnet ef database update` executes the `Up()` migration against the database and records the executed version in `__EFMigrationsHistory`.
- In AuthFlow, migrations are executed explicitly as a controlled operational step (never automatically during generic application startup) to prevent multi-instance race conditions and unexpected production schema locks.

#### How does the Application connect to PostgreSQL?
1. The application registers the `Npgsql.EntityFrameworkCore.PostgreSQL` provider in DI via `builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString))`.
2. When database queries execute, Npgsql establishes a pooled TCP connection over port 5432, exchanges authentication credentials via SCRAM-SHA-256, and executes parameterized SQL statements.

#### Why do Indexes and Database Constraints matter?
- **Constraints enforce data integrity at the lowest level**: Even if an application bug bypasses validation logic, the database refuses corrupt states (e.g. `NOT NULL`, `MaxLength`, Foreign Keys).
- **Unique Indexes prevent concurrency race conditions**: If two users simultaneously register with the same email, application-level checks might both pass, but the database's unique constraint (`IX_Users_NormalizedEmail`) guarantees that one transaction will succeed and the other will fail with a `23505 unique_violation`.
- **Foreign Keys maintain relational integrity**: `FK_RefreshTokens_Users_UserId` ensures a refresh token cannot exist without a valid user, and `OnDelete(DeleteBehavior.Cascade)` ensures that deleting a user cleanly evicts all their active refresh tokens.
- **Search Indexes speed up high-frequency lookups**: B-tree indexes on `NormalizedEmail`, `TokenHash`, `UserId`, and `FamilyId` turn slow full-table scans ($O(N)$) into logarithmic binary lookups ($O(\log N)$).

#### How do `User` and `RefreshToken` relate?
- The relationship is **One-to-Many**: One `User` can have multiple active or historic `RefreshToken` entries (across different devices or browsers).
- `RefreshToken` contains a foreign key `UserId` referencing `Users.Id`.
- In C#:
  - `User.RefreshTokens` is a collection navigation property (`ICollection<RefreshToken>`).
  - `RefreshToken.User` is a reference navigation property pointing back to the parent `User`.
- `DeleteBehavior.Cascade` guarantees that if a user account is deleted, all their refresh tokens are purged in the same database transaction.

---

## 3. Database Schema Specification

### `Users` Table
| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `uuid` | `PRIMARY KEY` | Unique user identifier. |
| `FullName` | `character varying(100)` | `NOT NULL` | User's display name. |
| `Email` | `character varying(256)` | `NOT NULL` | Original user-entered email. |
| `NormalizedEmail`| `character varying(256)` | `NOT NULL, UNIQUE` | Uppercased email for case-insensitive duplicate prevention. |
| `PasswordHash` | `text` | `NOT NULL` | PBKDF2 with HMAC-SHA512 salted password hash. |
| `IsActive` | `boolean` | `NOT NULL, DEFAULT true` | Active account toggle. |
| `CreatedAtUtc` | `timestamp with time zone` | `NOT NULL` | Account creation timestamp in UTC. |
| `UpdatedAtUtc` | `timestamp with time zone` | `NOT NULL` | Account last modified timestamp in UTC. |
| `AccessFailedCount` | `integer` | `NOT NULL, DEFAULT 0`| Counter for failed login attempts (lockout protection). |
| `LockoutEndUtc` | `timestamp with time zone` | `NULL` | Expiration of temporary account lockout. |

### `RefreshTokens` Table
| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `uuid` | `PRIMARY KEY` | Unique token record identifier. |
| `UserId` | `uuid` | `NOT NULL, FK -> Users(Id)` | Owner user ID (Cascade Delete). |
| `TokenHash` | `character varying(128)` | `NOT NULL, UNIQUE` | SHA-256 hash of the random refresh token string. |
| `FamilyId` | `uuid` | `NOT NULL` | Identifier shared across tokens in a rotation lineage. |
| `CreatedAtUtc` | `timestamp with time zone` | `NOT NULL` | Token creation timestamp in UTC. |
| `ExpiresAtUtc` | `timestamp with time zone` | `NOT NULL` | Token expiration timestamp in UTC. |
| `RevokedAtUtc` | `timestamp with time zone` | `NULL` | Timestamp when revoked (null if active). |
| `ReplacedByTokenId`| `uuid` | `NULL` | ID of the successor token issued during rotation. |

---

## 4. Password Security & Cryptography

1. **No Plaintext Storage**: Plaintext passwords are never persisted or logged.
2. **Algorithm**: Standard ASP.NET Core Identity `PasswordHasher<User>`, utilizing **PBKDF2 (Password-Based Key Derivation Function 2)** with HMAC-SHA512.
3. **Salting**: Generates a cryptographically secure 128-bit salt per user automatically.
4. **Iteration Count**: Complies with modern cryptographic recommendations (100,000+ iterations), mitigating brute-force and rainbow table attacks.

---

## 5. Token & Session Management

- **Access Token (JWT)**:
  - Lifespan: 15 minutes.
  - Storage: In-memory React state (never in localStorage to prevent token harvesting from XSS).
- **Refresh Token**:
  - Lifespan: 7 days.
  - Transmission: `HttpOnly; Secure; SameSite=Lax; Path=/api/auth` cookie (prevents direct JavaScript access).
  - Storage: Only the SHA-256 `TokenHash` is persisted in the database; raw token values never touch persistent storage.
  - Rotation: Each refresh exchange revokes the existing token, marks `ReplacedByTokenId`, and issues a new token in the same `FamilyId`. If an already-revoked token is presented, the entire family is immediately invalidated (reuse detection).
