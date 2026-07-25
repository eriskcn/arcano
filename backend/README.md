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

### 3. API Verification Endpoints
- **Health Check:** `GET http://localhost:5000/health`
- **Upload File:** `POST http://localhost:5000/api/files/upload` (Form-data: `file` field containing your file)
- **Download File:** `GET http://localhost:5000/api/files/{id}`
- **Delete File:** `DELETE http://localhost:5000/api/files/{id}`
