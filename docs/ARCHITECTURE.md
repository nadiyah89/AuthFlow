# AuthFlow — Architecture Documentation

## 1. System Overview

AuthFlow is a full-stack authentication web application built using **ASP.NET Core Web API (.NET 10)** on the backend, **React 19 + TypeScript + Vite** on the frontend, and **PostgreSQL 17** for data persistence.

The application adheres to a clean 3-tier monolithic layered architecture designed for maintainability, high security, and clear evaluation in a technical .NET interview.

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
- **Why not use Domain Entities directly?**
  - **Decoupling**: Prevents leaking internal database structures (e.g. `PasswordHash`, `LockoutEndUtc`) to clients.
  - **Over-Posting Prevention**: Prevents malicious users from submitting fields they shouldn't modify (e.g. `IsActive = true`).
  - **Targeted Validation**: Allows DataAnnotation attributes specific to input rules (such as matching `Password` and `ConfirmPassword`) without cluttering persistent entities.

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
4. **Output Format**: The resulting hash includes the algorithm version subheader, salt, and derived key encoded in Base64:
   ```
   [Header: 1 byte][PRF: 4 bytes][Iteration count: 4 bytes][Salt length: 4 bytes][Salt: N bytes][Subkey: N bytes]
   ```
5. **Verification**: During login, the stored salt and iteration count are extracted from the stored hash and applied to the user-entered password. The results are compared using constant-time byte equality to prevent timing attacks.

#### How JWT Authentication Works
1. **Access Token Generation**:
   - `TokenService` creates a signed JSON Web Token (JWT) using `HmacSha256` and the application's symmetric secret key.
   - Claims embedded: `sub` (User Id), `email`, `jti` (unique token id), and `name`.
   - Lifespan: Short-lived (15 minutes).
2. **Transmission**: Sent in the JSON response body and retained in React in-memory state.
3. **Verification**: ASP.NET Core's `JwtBearerHandler` intercepts requests with `Authorization: Bearer <token>`, verifies the cryptographic HMAC signature, validates issuer and audience, and rejects expired tokens (`ClockSkew = TimeSpan.Zero`).

---

## 3. Request Lifecycle Walkthroughs

### Walkthrough 1: How Registration Travels Through All Layers
1. **Client**: User submits full name, email, and password. React dispatches `POST /api/auth/register`.
2. **Reverse Proxy**: Vercel/Vite proxy forwards the request to `/api/auth/register`.
3. **Global Exception & Rate Limiter**: Rate limiting policy (`auth-limit`) verifies IP throughput.
4. **Model Binding & Validation**: ASP.NET Core binds the JSON body to `RegisterRequestDto`. DataAnnotation attributes enforce email format and password matching. If invalid, ASP.NET Core returns `400 Bad Request` immediately.
5. **Controller Layer**: `AuthController.Register` delegates to `_authService.RegisterAsync(request)`.
6. **Business Layer (`AuthService`)**:
   - Normalizes email (`Trim().ToUpperInvariant()`).
   - Checks if `_context.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail)`. If duplicate, returns `409 Conflict`.
   - Hashes password using `_passwordHasher.HashPassword(user, request.Password)`.
   - Generates high-entropy refresh token and computes its SHA-256 hash.
   - Saves `User` and `RefreshToken` entities to PostgreSQL via `_context.SaveChangesAsync()`.
   - Catches any concurrent database collisions (`PostgresException 23505`) and maps them to `409 Conflict`.
   - Issues short-lived signed JWT access token via `_tokenService.GenerateAccessToken(user)`.
7. **Controller Response**:
   - Attaches `rawRefreshToken` as an `HttpOnly; Path=/api/auth; SameSite=Lax` cookie in the HTTP response.
   - Returns `201 Created` with `{ accessToken, expiresInMinutes: 15, user: { id, fullName, email } }`.

### Walkthrough 2: How Login Verifies Credentials
1. **Client**: User enters email and password -> `POST /api/auth/login`.
2. **Controller & Service**: `AuthController.Login` calls `_authService.LoginAsync(request)`.
3. **User Lookup**: Looks up user by `NormalizedEmail`.
   - *Account Enumeration Mitigation*: If the user does not exist, a dummy password hash verification runs to consume equivalent CPU time, then returns generic `401 Unauthorized`.
4. **Account State Checks**:
   - Inactive: Returns `403 Forbidden` ("Account is inactive").
   - Locked: If `LockoutEndUtc > DateTime.UtcNow`, calculates remaining minutes and returns `423 Locked`.
5. **Password Verification**:
   - Verifies password with `_passwordHasher.VerifyHashedPassword`.
   - If invalid: Increments `AccessFailedCount`. If $\ge 5$, sets `LockoutEndUtc = DateTime.UtcNow.AddMinutes(15)` and returns `423 Locked`. Otherwise returns `401 Unauthorized`.
   - If valid: Resets `AccessFailedCount = 0` and clears `LockoutEndUtc`.
6. **Session Creation**:
   - Generates a new refresh token and saves its SHA-256 hash.
   - Generates JWT access token.
   - Attaches `HttpOnly` refresh token cookie and returns `200 OK` with user profile and access token.

---

## 4. Database Schema Specification

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

---

## 5. Security Trade-offs & Mitigations

1. **In-Memory JWT Access Tokens**: Mitigates Cross-Site Scripting (XSS) token harvesting by never storing access tokens in `localStorage` or `sessionStorage`.
2. **HttpOnly Cookies for Refresh Tokens**: Protects raw refresh tokens from client-side script inspection.
3. **Hashed Refresh Tokens**: Even if the database is compromised, attackers only obtain SHA-256 hashes of high-entropy random strings, which cannot be converted back into valid cookies.
4. **Account Lockout & Rate Limiting**: 5 consecutive failed attempts trigger a 15-minute lockout, and built-in rate limiting restricts IP bursts on `/api/auth/*`.
5. **Constant-Time Timing Mitigation**: Unknown emails undergo dummy password verification to neutralize timing-based user enumeration.
