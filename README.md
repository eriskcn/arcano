# Arcano

A personal cloud-storage application monorepo with an ASP.NET Core backend and Angular client. The repository is currently at the foundation stage: backend layers and file API groundwork are in place, while the frontend dashboard and user-facing workflows remain planned work.

## Repository overview

- `backend/`: ASP.NET Core (.NET 10) Web API organized around Domain, Application, Infrastructure, and API layers. PostgreSQL stores metadata; SeaweedFS provides object storage.
- `frontend/`: Angular 22 client scaffold. File management UI, authentication flows, sharing, and previews are not yet implemented.
- `REPO_STATUS.md`: current repository-stage assessment, gaps, and recommended next steps.
- `backend/wbs/WBS.md` and `frontend/wbs/WBS.md`: implementation task tracking.

## Quickstart

### Backend

Prerequisites: .NET 10 SDK and Docker with Compose.

Start PostgreSQL and SeaweedFS, then run the API locally:

```bash
cd backend
docker compose up -d postgres seaweedfs
dotnet run --project src/ArcanoCore.API
```

See [backend/README.md](backend/README.md) for configuration and endpoint details. The backend README also describes running the full stack with Compose.

### Frontend

Prerequisites: Node.js and npm.

```bash
cd frontend
npm install
npm start
```

The development server runs at `http://localhost:4200/`. See [frontend/README.md](frontend/README.md) for Angular CLI commands.

## Current status

- Backend foundation and file upload/download/delete API work are marked complete in the backend WBS; build, tests, and runtime behavior still need independent verification.
- Frontend tasks are marked not started in the frontend WBS.
- The repository includes backend unit-test and frontend spec projects. Run the backend tests with `dotnet test backend/ArcanoCore.sln` and the frontend tests from `frontend/` with `npm test`.

## Before deployment

Review the Compose configuration: it currently includes fixed database and object-storage credentials. Replace these with appropriate secrets and environment-specific configuration before using the stack beyond local development.

> The project documentation and route configuration should be reconciled against the actual API implementation before relying on endpoint names or deployment behavior.

For a fuller assessment and prioritized follow-up work, see [REPO_STATUS.md](REPO_STATUS.md).

## License

See the [backend license](backend/LICENSE) and [frontend license](frontend/LICENSE).
