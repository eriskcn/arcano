# ArcanoCore

Boilerplate ASP.NET Core Web API project targeting .NET 10 based on Clean Architecture (4-layer structure) with PostgreSQL and SeaweedFS object storage integration.

## Project Structure
- `ArcanoCore.Domain`: Base entities, results, interfaces without dependencies.
- `ArcanoCore.Application`: DTOs, business interfaces and the File, Folder, Share, Upload, Quota and Account services.
- `ArcanoCore.Infrastructure`: DbContext, migrations, Identity and JWT setup, cleanup job, SeaweedStorageService implementing S3.
- `ArcanoCore.API`: Endpoints, Dependency Injection registration, RFC 7807 Exception Middleware.

## Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Docker & Docker Compose](https://www.docker.com/products/docker-desktop/)

## Quickstart Guide

### 1. Build and Run via Docker Compose
To build the API docker image and orchestrate the whole stack (PostgreSQL, SeaweedFS, API):
```bash
docker compose up --build -d
```

### 2. Migrations
Migrations already exist in `ArcanoCore.Infrastructure` and are applied automatically when the API starts (`Database:MigrateOnStartup`, default `true`).

To add a migration, install the EF Core CLI at the version the projects use (`Microsoft.EntityFrameworkCore` 10.0.4 in Infrastructure):
```bash
dotnet tool install --global dotnet-ef --version 10.0.4
```
Then run, with `Jwt__Secret` set to at least 32 bytes (the API refuses to start without it):
```bash
export Jwt__Secret="$(openssl rand -hex 32)"
dotnet ef migrations add <Name> --project src/ArcanoCore.Infrastructure --startup-project src/ArcanoCore.API
```

### 3. API Endpoints
All routes are under `/api` except `/health`. "Bearer" means `Authorization: Bearer <token>` is required; files, folders and shares only act on data owned by the signed-in user.

| Method | Path | Auth |
|---|---|---|
| GET | `/health` (checks database and storage; 503 if either fails) | Anonymous |
| GET | `/api/Auth/setup` (`{requiresKey}`; false only while there are no users) | Anonymous |
| POST | `/api/Auth/register` (`email`, `password`, `inviteKey`; the first account needs no key and becomes admin, later ones need a Join key, else 403 `Auth.InviteInvalid`; rate limited) | Anonymous |
| POST | `/api/Auth/reset` (`key`, `newPassword`; Reset key, 204; old tokens stop working; rate limited) | Anonymous |
| POST | `/api/Auth/login` (`email`, `password`; returns `token`, which carries `role: Admin` for admins; rate limited) | Anonymous |
| GET | `/api/Auth/session` | Bearer |
| POST | `/api/Auth/refresh` (new token; the session ends 24 hours after sign-in) | Bearer |
| POST | `/api/Auth/logout` (revokes all of the user's tokens) | Bearer |
| GET | `/api/Admin/users` (id, email, isAdmin, isDisabled, usedBytes, quotaBytes, effectiveQuotaBytes, createdAtUtc) | Admin |
| PUT | `/api/Admin/users/{id}/quota` (`quotaBytes`, null = server default) | Admin |
| POST | `/api/Admin/users/{id}/disable`, `/enable` | Admin |
| DELETE | `/api/Admin/users/{id}` (data first, then the user; 409 `Admin.LastAdmin` for the last active admin) | Admin |
| GET | `/api/Admin/keys` (open keys) | Admin |
| POST | `/api/Admin/keys` (`type` Join or Reset, `quotaBytes`, `userId` for Reset, `expiresInDays` default 7, max 30; the plain key is returned only here) | Admin |
| DELETE | `/api/Admin/keys/{id}` (revoke) | Admin |
| GET | `/api/Account/storage` (quota and usage) | Bearer |
| POST | `/api/Account/password` (change password; returns a new token; rate limited) | Bearer |
| DELETE | `/api/Account` (delete the account and its data; rate limited) | Bearer |
| POST | `/api/Files/upload` (form-data `file`, optional `folderId`) | Bearer |
| GET | `/api/Files` (query: `skip`, `take` (default 50, max 100), `folderId`, `view`, `q`) | Bearer |
| GET | `/api/Files/{id}` (download; supports HTTP Range, 206) | Bearer |
| DELETE | `/api/Files/{id}` (moves to the Trash) | Bearer |
| PUT | `/api/Files/{id}/name` | Bearer |
| PUT | `/api/Files/{id}/star` | Bearer |
| PUT | `/api/Files/{id}/folder` (move) | Bearer |
| POST | `/api/Files/{id}/copy` | Bearer |
| GET | `/api/Files/trash` (query: `skip`, `take`) | Bearer |
| POST | `/api/Files/{id}/restore` | Bearer |
| DELETE | `/api/Files/{id}/permanent` | Bearer |
| DELETE | `/api/Files/trash` (empty the Trash) | Bearer |
| POST | `/api/Folders` | Bearer |
| GET | `/api/Folders` | Bearer |
| PUT | `/api/Folders/{id}` | Bearer |
| PUT | `/api/Folders/{id}/star` (body `{isStarred}`) | Bearer |
| DELETE | `/api/Folders/{id}` (contents go to the Trash) | Bearer |
| GET | `/api/Folders/{id}/download` (zip, at most 5,000 files) | Bearer |
| POST | `/api/Share` (create a share link) | Bearer |
| GET | `/api/Share?fileId=` (list links of a file) | Bearer |
| DELETE | `/api/Share/{id}` (revoke) | Bearer |
| GET | `/api/Public/{token}/info` (rate limited) | Anonymous |
| GET | `/api/Public/{token}` (download as attachment; supports HTTP Range; rate limited) | Anonymous |
| POST | `/api/UserShares` (share a file or folder read-only with a registered user by email; 400 `UserShare.InvalidRecipient` for yourself, unknown or disabled email; rate limited, 20 per minute per user) | Bearer |
| GET | `/api/UserShares?fileId=` or `?folderId=` (who an item is shared with) | Bearer |
| DELETE | `/api/UserShares/{id}` (owner or recipient) | Bearer |
| GET | `/api/Shared` (incoming shares) | Bearer |
| GET | `/api/Shared/{shareId}/children?folderId=` (browse a shared folder) | Bearer |
| GET | `/api/Shared/{shareId}/files/{fileId}/download` | Bearer |
| GET | `/api/Shared/{shareId}/folders/{folderId}/download` (zip) | Bearer |
| POST | `/api/Uploads` (start a chunked upload) | Bearer |
| GET | `/api/Uploads/{id}` (status) | Bearer |
| PUT | `/api/Uploads/{id}/parts/{partNumber}` | Bearer |
| POST | `/api/Uploads/{id}/complete` | Bearer |
| DELETE | `/api/Uploads/{id}` (abort) | Bearer |

User shares are read-only. Access is re-checked on every request: the share exists, neither user is disabled, the item is inside the shared subtree and is not in the Trash. Deleting an account (own or by an admin) removes that user's shares. Folders have no public links. The error for an unknown email lets members probe whether an address is registered (accepted for a trusted group).

Failed multipart aborts keep the upload session row, and the cleanup job retries every pass (S3 `NoSuchUpload` counts as success; no attempt cap). Repeating `POST /api/Uploads/{id}/complete` after success returns the already-created file (its id is the upload session id).

Admin and key actions are written to the API log as `Audit {Event} actor= target=` lines (InviteKeyCreated, InviteKeyRevoked, InviteKeyRedeemed, ResetKeyUsed, UserDisabled, UserEnabled, UserDeleted, QuotaChanged with old and new values, AdminPromotedByBootstrap). No table; key material is never logged.

The upload limit is `Storage:MaxUploadBytes` (default 100 MB); larger requests are rejected with 413. Chunked uploads send 8 MiB parts.

### 4. Configuration
Set values in `appsettings.json` or as environment variables (`:` becomes `__`, for example `Jwt__Secret`).

| Key | Default | Notes |
|---|---|---|
| `Jwt:Secret` | none | Required, at least 32 UTF-8 bytes. Compose reads it from `JWT_SECRET`. |
| `Jwt:Issuer` / `Jwt:Audience` | `ArcanoCore` / `ArcanoCoreUsers` | |
| `Jwt:ExpiryInMinutes` | `60` | |
| `Storage:ServiceUrl` | `http://seaweedfs:8333` | SeaweedFS S3 gateway. |
| `Storage:AccessKey` / `Storage:SecretKey` | `any_key` / `any_secret` | Must match the SeaweedFS identity (`seaweedfs/s3.json` in dev). |
| `Storage:BucketName` | `arcanocore-bucket` | |
| `Storage:QuotaBytes` | `5368709120` (5 GiB) | Default per-user quota; an admin can set a different one per user. |
| `Storage:MaxUploadBytes` | `104857600` (100 MB) | Upload size limit. |
| `Cors:AllowedOrigins` | `["http://localhost:4200"]` | |
| `ForwardedHeaders:KnownProxies` | `[]` | Behind a reverse proxy set `ForwardedHeaders__KnownProxies__0` to the proxy IP so rate limiting sees client IPs; when empty, X-Forwarded-For is ignored. |
| (rate limits) | fixed | Sign-in/up 10/min per IP; `/api/Public` info 60 and download 20/min per IP; `/api/UserShares`, `/api/Shared`, `/api/Admin` 120/min per user (429 over the limit). Not configurable. |
| `Database:MigrateOnStartup` | `true` | Applies migrations at startup. |
| `Auth:BootstrapAdminEmail` | empty | Upgrades only: if users exist but none is an admin, this user is promoted at startup (not if disabled or locked out). Without it a sole user is promoted automatically; with several users an error is logged and nobody is promoted. |
| `Cleanup:Enabled` | `true` | Background job: abandoned upload sessions older than 24 h, trashed files older than 30 days, and soft-deleted folders older than 30 days once empty. |
| `Cleanup:IntervalMinutes` | `60` | |
| `DataProtection:KeysPath` | `/app/keys` in a container, framework default otherwise | Where DataProtection keys are persisted. |

`ConnectionStrings:DefaultConnection` points at the `postgres` Compose host; override it for a local run. Set `Logging` levels in `appsettings.Development.json`; EF Core logging is set to `Warning`.

### 5. Before deploying
- The dev Compose file (`backend/docker-compose.yml`) publishes Postgres (5432), SeaweedFS (9333, 8080, 8333) and the API (5000) on loopback only; do not expose 5432 or 8333 publicly. For production use `docker-compose.prod.yml` in the repository root (see `docs/DEPLOY.md`).
- Set `POSTGRES_PASSWORD`, `S3_ACCESS_KEY`, `S3_SECRET_KEY` and `JWT_SECRET` (the first three default to dev values).
- Replace `seaweedfs/s3.json` with your own identity; its keys must match `S3_ACCESS_KEY`/`S3_SECRET_KEY`.

### 6. Tests
```bash
dotnet test ArcanoCore.sln
```
Unit tests need nothing. The integration tests in `test/ArcanoCore.IntegrationTests` are skipped unless enabled:
- `ARCANO_IT_S3=1` runs the S3 tests against SeaweedFS on `localhost:8333` (the dev Compose stack).
- `ARCANO_IT_PG` runs the PostgreSQL tests; set it to an admin connection string, for example `Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=postgrespassword`. Each test class creates and drops its own `arcano_it_*` database (see `test/ArcanoCore.IntegrationTests/PgFixture.cs`).
