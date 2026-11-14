# AuthFlow — Architecture Documentation

## 1. System Overview

AuthFlow is a full-stack authentication web application built using **ASP.NET Core Web API (.NET 10)** on the backend, **React 19 + TypeScript + Vite** on the frontend, and **PostgreSQL 17** for data persistence.

The application follows a clean 3-tier monolithic layered architecture designed for maintainability, high security, and clear evaluation in a technical .NET interview.

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

#### What is a DTO (Data Transfer Object)?
A **DTO** is a plain object designed purely to carry data across architectural boundaries (e.g., between the HTTP client and the controller).
- **Decoupling**: Prevents leaking internal database structures (e.g. `PasswordHash`, `LockoutEndUtc`) to clients.
- **Over-Posting Prevention**: Prevents malicious users from submitting fields they shouldn't modify (e.g. `IsActive = true`).
- **Targeted Validation**: Enables DataAnnotation attributes specific to input rules (such as matching `Password` and `ConfirmPassword`) without cluttering persistent entities.

#### Why are Services used?
The **Service Layer** encapsulates core business logic, domain workflows, and algorithmic rules:
- **Separation of Concerns**: Controllers only handle HTTP concerns (status codes, headers, cookie issuance). Services handle business calculations (password hashing, credential verification, lockout logic, token generation).
- **Reusability & Testability**: Services can be tested independently in unit or integration tests without spinning up an HTTP server.

#### How does Dependency Injection connect Controllers and Services?
ASP.NET Core provides an **Inversion of Control (IoC) Container**:
1. Services are registered in DI inside `Program.cs` with specific lifetimes:
   - `builder.Services.AddScoped<IAuthService, AuthService>();`
   - `builder.Services.AddScoped<ITokenService, TokenService>();`
   - `builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();`
2. When an incoming request reaches `AuthController`, the runtime inspects its constructor:
   ```csharp
   public AuthController(IAuthService authService) { _authService = authService; }
   ```
3. The IoC container automatically resolves and injects an instance of `AuthService` (scoped to the lifetime of that specific HTTP request).
4. This ensures loose coupling: `AuthController` depends on the abstraction `IAuthService`, not the concrete implementation.

#### How Password Hashing Works (PBKDF2 with HMAC-SHA512)
1. **Never Plaintext**: Plaintext passwords are never persisted or logged.
2. **Per-User Salting**: When registering, ASP.NET Core Identity's `PasswordHasher<User>` generates a cryptographically secure 128-bit random salt for each user.
3. **Key Derivation**: The salt and password are fed into **PBKDF2** with **HMAC-SHA512** running 100,000+ iterations.
4. **Verification**: During login, the stored salt and iteration count are extracted from the stored hash and applied to the user-entered password. The results are compared using constant-time byte equality to prevent timing attacks.

#### How JWT Authentication Works
1. **Access Token Generation**:
   - `TokenService` creates a signed JSON Web Token (JWT) using `HmacSha256` and the application's symmetric secret key.
   - Claims embedded: `sub` (User Id), `email`, `jti` (unique token id), and `name`.
   - Lifespan: Short-lived (15 minutes).
2. **Transmission**: Sent in the JSON response body and retained in React in-memory state.
3. **Verification**: ASP.NET Core's `JwtBearerHandler` intercepts requests with `Authorization: Bearer <token>`, verifies the cryptographic HMAC signature, validates issuer and audience, and rejects expired tokens (`ClockSkew = TimeSpan.Zero`).

---

## 3. Token Lifecycle, Rotation, and Session Management

### Complete Token Lifecycle
```
[ Client ]                        [ API / Service ]                    [ PostgreSQL ]
   │                                     │                                    │
   │ ── 1. POST /api/auth/login ───────► │                                    │
   │ ◄── 2. JWT in body + Cookie ─────── │ ◄── Store Hashed Refresh Token ─── │
   │                                     │                                    │
   │ (15 mins later - JWT expires)       │                                    │
   │                                     │                                    │
   │ ── 3. POST /api/auth/refresh ─────► │ ── SELECT ... FOR UPDATE ────────► │ (Row locked)
   │       (Cookie auto-sent)            │ ── Revoke Token ─────────────────► │
   │                                     │ ── Insert Successor Token ───────► │
   │ ◄── 4. New JWT + New Cookie ─────── │ ── COMMIT Transaction ───────────► │
   │                                     │                                    │
   │ ── 5. GET /api/auth/me ───────────► │                                    │
   │       (Bearer <new JWT>)            │                                    │
   │ ◄── 6. 200 OK (User Profile) ────── │                                    │
   │                                     │                                    │
   │ ── 7. POST /api/auth/logout ──────► │ ── Mark Token Revoked ───────────► │
   │ ◄── 8. 200 OK + Clear Cookie ────── │                                    │
```

### Refresh-Token Rotation & Lineage
Every time a refresh token is exchanged at `/api/auth/refresh`:
1. The presented token is looked up by its SHA-256 hash using PostgreSQL row locking (`FOR UPDATE`).
2. The current token is immediately marked as revoked (`RevokedAtUtc = DateTime.UtcNow`).
3. A successor token is generated with a new cryptographic random string and hashed.
4. The successor token inherits the original `FamilyId`, and the original token's `ReplacedByTokenId` points to the successor.
5. Both updates commit in a single atomic database transaction.

### Replay & Reuse Detection (Family Revocation)
If a refresh token that has already been revoked (`RevokedAtUtc != null`) is presented at `/api/auth/refresh`:
- The system recognizes that an old token is being replayed (which can happen if a refresh token was intercepted or stolen).
- The service immediately invalidates the entire lineage:
  ```csharp
  await _context.RefreshTokens
      .Where(t => t.FamilyId == tokenRecord.FamilyId && t.RevokedAtUtc == null)
      .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow));
  ```
- Any active tokens in that family (even legitimate ones) are immediately revoked, requiring the user to re-authenticate from scratch.

### Concurrency Protection
To prevent race conditions where simultaneous refresh calls (e.g. two browser tabs waking up) create multiple valid successor tokens:
- AuthFlow uses **PostgreSQL row-level locking**:
  ```sql
  SELECT * FROM "RefreshTokens" WHERE "TokenHash" = @hash FOR UPDATE;
  ```
- The first request acquires the lock, rotates the token, and commits.
- The second request waits for the lock, reads the updated row, sees `RevokedAtUtc != null`, and is rejected, guaranteeing that exactly **one** successor is ever created.

---

## 4. CSRF Protection for Cookie-Authenticated Endpoints

State-changing cookie-authenticated endpoints (`POST /api/auth/refresh` and `POST /api/auth/logout`) are defended through depth-in-defense:

1. **Custom Request Header Requirement**:
   - The browser must send `X-Requested-With: XMLHttpRequest` or `X-AuthFlow-Client: Web`.
   - Cross-origin HTML forms (`<form>`) and scripts cannot send custom headers across domains without passing a preflight CORS check.
2. **Origin & Referer Validation**:
   - The middleware inspects `Origin` or `Referer` headers and verifies them against the configured whitelist of trusted origins (`http://localhost:5173`, production Vercel domains, etc.).
   - Requests without origin/referer or pointing to untrusted domains are rejected with `403 Forbidden`.
3. **SameSite=Lax Cookie Policy**:
   - Modern browsers refuse to attach `SameSite=Lax` cookies on cross-site sub-resource requests (e.g., cross-origin `fetch` or image tags).
4. **Middleware Ordering**:
   ```csharp
   app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
   app.UseCors(corsPolicy);            // 1. CORS handles OPTIONS preflights
   app.UseMiddleware<CsrfProtectionMiddleware>(); // 2. CSRF blocks invalid origins
   app.UseRateLimiter();               // 3. Rate limiting restricts throughput
   app.UseAuthentication();            // 4. JWT Authentication
   app.UseAuthorization();             // 5. Role/Policy Authorization
   app.MapControllers();
   ```

---

## 5. Database Schema Specification

### `Users` Table
| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `uuid` | `PRIMARY KEY` | Unique user identifier. |
| `FullName` | `character varying(100)` | `NOT NULL` | Display name. |
| `Email` | `character varying(256)` | `NOT NULL` | Original user-entered email. |
| `NormalizedEmail`| `character varying(256)` | `NOT NULL, UNIQUE` | Uppercased email for duplicate prevention. |
| `PasswordHash` | `text` | `NOT NULL` | PBKDF2 with HMAC-SHA512 salted hash. |
| `IsActive` | `boolean` | `NOT NULL, DEFAULT true` | Active account toggle. |
| `CreatedAtUtc` | `timestamp with time zone` | `NOT NULL` | Account creation timestamp in UTC. |
| `UpdatedAtUtc` | `timestamp with time zone` | `NOT NULL` | Account last modified timestamp in UTC. |
| `AccessFailedCount` | `integer` | `NOT NULL, DEFAULT 0`| Counter for failed login attempts. |
| `LockoutEndUtc` | `timestamp with time zone` | `NULL` | Expiration of temporary account lockout. |

### `RefreshTokens` Table
| Column | Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `uuid` | `PRIMARY KEY` | Unique token record identifier. |
| `UserId` | `uuid` | `NOT NULL, FK -> Users(Id)` | Owner user ID (Cascade Delete). |
| `TokenHash` | `character varying(128)` | `NOT NULL, UNIQUE` | SHA-256 hash of random refresh token. |
| `FamilyId` | `uuid` | `NOT NULL` | Identifier shared across tokens in a lineage. |
| `CreatedAtUtc` | `timestamp with time zone` | `NOT NULL` | Token creation timestamp in UTC. |
| `ExpiresAtUtc` | `timestamp with time zone` | `NOT NULL` | Token expiration timestamp in UTC. |
| `RevokedAtUtc` | `timestamp with time zone` | `NULL` | Timestamp when revoked (null if active). |
| `ReplacedByTokenId`| `uuid` | `NULL` | ID of successor token issued during rotation. |
