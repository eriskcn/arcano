# ArcanoCore

Boilerplate ASP.NET Core Web API project targeting .NET 10 based on Clean Architecture (4-layer structure) with PostgreSQL and SeaweedFS object storage integration.

## Project Structure
- `ArcanoCore.Domain`: Base entities, results, interfaces without dependencies.
- `ArcanoCore.Application`: DTOs, Business Interfaces, FileService.
- `ArcanoCore.Infrastructure`: DbContext, Repositories, Unit of Work, SeaweedStorageService implementing S3.
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

### 2. Generate and Apply EF Core Migrations
Ensure EF Core CLI tool is installed:
```bash
dotnet tool install --global dotnet-ef --version 10.0.0-preview.1.25081.1
```
*(Note: Use version matching EntityFrameworkCore version in csproj)*

#### From Local Machine:
Update `ConnectionStrings:DefaultConnection` in `src/ArcanoCore.API/appsettings.Development.json` to reference `localhost`.

Generate a migration:
```bash
dotnet ef migrations add InitialCreate --project src/ArcanoCore.Infrastructure/ArcanoCore.Infrastructure.csproj --startup-project src/ArcanoCore.API/ArcanoCore.API.csproj
```

Apply migration:
```bash
dotnet ef database update --project src/ArcanoCore.Infrastructure/ArcanoCore.Infrastructure.csproj --startup-project src/ArcanoCore.API/ArcanoCore.API.csproj
```

### 3. API Endpoints
- **Health Check:** `GET http://localhost:5000/health` (anonymous)
- **Register:** `POST http://localhost:5000/api/Auth/register` (JSON: `email`, `password`)
- **Login:** `POST http://localhost:5000/api/Auth/login` (JSON: `email`, `password`; returns `token`)
- **Session:** `GET http://localhost:5000/api/Auth/session` (Bearer token)

The file endpoints below require `Authorization: Bearer <token>` and only act on files owned by the signed-in user:

- **List Files:** `GET http://localhost:5000/api/Files` (optional query: `skip` (default 0), `take` (default 50, max 100))
- **Upload File:** `POST http://localhost:5000/api/Files/upload` (Form-data: `file` field containing your file)
- **Download File:** `GET http://localhost:5000/api/Files/{id}`
- **Delete File:** `DELETE http://localhost:5000/api/Files/{id}`

Uploads are limited to 100 MB (`[RequestSizeLimit(104857600)]`); larger requests are rejected.

### 4. Configuration
- `Jwt:Secret` (env `Jwt__Secret`) is required and must be at least 32 UTF-8 bytes. Compose reads it from `JWT_SECRET`.
- `Storage:*` points the API at SeaweedFS's S3 gateway. The Compose SeaweedFS service loads its S3 identity from `backend/seaweedfs/s3.json`; the placeholder keys are for local development only.
- Behind a reverse proxy, set `ForwardedHeaders__KnownProxies__0` to the proxy IP so rate limiting sees client IPs; leave unset otherwise (X-Forwarded-For is then ignored).
- Set `Logging` levels in `appsettings.Development.json`; EF Core logging is set to `Warning`.

### 5. Before deploying
- Compose publishes Postgres (5432) and SeaweedFS (9333, 8080, 8333) on loopback only; do not expose 5432 or 8333 publicly.
- Set `POSTGRES_PASSWORD`, `S3_ACCESS_KEY`, `S3_SECRET_KEY` and `JWT_SECRET` (the first three default to dev values).
- Replace `seaweedfs/s3.json` with your own identity; its keys must match `S3_ACCESS_KEY`/`S3_SECRET_KEY`.
