# Arcano

Monorepo containing backend and frontend for Arcano.

## Structure

- `backend/`: ASP.NET Core (.NET 10) Web API, Clean Architecture, PostgreSQL, SeaweedFS.
- `frontend/`: Angular client application.

## Quickstart

### Backend

Prerequisites: .NET 10 SDK, Docker.

```bash
cd backend
docker compose up -d
dotnet run --project src/ArcanoCore.API
```

See [backend/README.md](backend/README.md) for details.

### Frontend

Prerequisites: Node.js, npm.

```bash
cd frontend
npm install
npm start
```

Runs at `http://localhost:4200/`. See [frontend/README.md](frontend/README.md) for details.
