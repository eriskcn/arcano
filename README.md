# Arcano

A personal cloud-storage application monorepo, also usable by a small trusted group, with an ASP.NET Core backend and Angular client. The backend API and the Angular client cover the main workflows (sign-in, files and folders, trash, share links, previews, chunked uploads), and a production Compose setup exists. See `REPO_STATUS.md` for what is verified and what is still open.

## Repository overview

- `backend/`: ASP.NET Core (.NET 10) Web API organized around Domain, Application, Infrastructure, and API layers. PostgreSQL stores metadata; SeaweedFS provides object storage.
- `frontend/`: Angular 22 client: sign-in and sign-up, a file explorer with folders, drag and drop, trash, share links, previews, chunked uploads (including from a phone), folder zip download, starred items that persist, keyboard shortcuts, a right-click menu, an account settings dialog, and an admin dialog (invite and reset keys, per-user quotas; the first account is the admin).
- `docs/USER_MANUAL.md`: how to use the app in a browser, including the admin tasks.
- `docs/TECHNICAL.md`: architecture, data model, API routes, configuration and testing, for developers.
- `docs/DEPLOY.md`: production deployment, backup and restore.
- `REPO_STATUS.md`: current repository-stage assessment, gaps, and recommended next steps.
- `backend/wbs/WBS.md` and `frontend/wbs/WBS.md`: implementation task tracking.

## Quickstart

### Backend

Prerequisites: .NET 10 SDK and Docker with Compose. The API requires `Jwt:Secret` (environment variable `Jwt__Secret`) with at least 32 UTF-8 bytes; export `JWT_SECRET` before using the full-stack Compose setup.

The dev Compose file mounts `backend/seaweedfs/s3.json` and `backend/seaweedfs/security.toml`, which are gitignored. Create them from the examples before `docker compose up`, otherwise Docker creates a directory in their place and SeaweedFS fails to start:

```bash
cd backend
cp seaweedfs/s3.json.example seaweedfs/s3.json
cp seaweedfs/security.toml.example seaweedfs/security.toml
sed -i "s/CHANGE_ME/$(openssl rand -hex 32)/" seaweedfs/security.toml
```

Start PostgreSQL and SeaweedFS, then run the API locally:

```bash
docker compose up -d postgres seaweedfs
dotnet run --project src/ArcanoCore.API
```

Database migrations are applied automatically when the API starts (`Database:MigrateOnStartup`, default `true`). See [backend/README.md](backend/README.md) for configuration and endpoint details. The backend README also describes running the full stack with Compose.

### Frontend

Prerequisites: Node.js and npm.

```bash
cd frontend
npm install
npm start
```

The development server runs at `http://localhost:4200/`. See [frontend/README.md](frontend/README.md) for Angular CLI commands.

### Full-stack development

Run `./run-dev.sh` from the repository root to start the API, frontend, PostgreSQL, and SeaweedFS. Add `--log-to-file` to also save the script output to `run-dev.log` while continuing to display it in the terminal:

```bash
./run-dev.sh --log-to-file
```

Set `RUN_DEV_LOG_FILE` to change the log file path.

## Current status

- Backend: accounts (register, login, token refresh, logout, change password, delete account), files with folders, trash, star, copy and move, share links, read-only sharing of files and folders with other users by email, chunked and resumable uploads, HTTP Range downloads, storage quota, folder zip download, persisted folder star, a background cleanup job (abandoned uploads, old trash and soft-deleted folders) and a deep `/health` check. The upload limit is configurable (`Storage:MaxUploadBytes`, default 100 MB).
- Frontend: file explorer with folders, drag and drop, trash, share links, a People section in the Share dialog and a "Shared with me" view, previews (image, text, audio, video), chunked uploads with a progress bar (file picker works on phones), folder zip download, persisted star, keyboard shortcuts, a right-click menu, an account settings dialog, and an admin dialog (invite keys, reset keys and per-user quotas; the first account is the admin).
- Tests: backend unit tests, API tests and integration tests (S3 and PostgreSQL), and frontend specs. Run the backend tests with `dotnet test backend/ArcanoCore.sln` and the frontend tests from `frontend/` with `npm test`. Integration tests need `ARCANO_IT_S3` and `ARCANO_IT_PG` set; see [backend/README.md](backend/README.md#tests).

## Before deployment

For a production setup use `docker-compose.prod.yml`, `.env.production.example` and `scripts/`, as described in [docs/DEPLOY.md](docs/DEPLOY.md). The rest of this section applies to the dev Compose file in `backend/`, which includes fixed database and object-storage credentials. Replace these with appropriate secrets and environment-specific configuration before using the stack beyond local development. Postgres and SeaweedFS ports are bound to loopback; set `POSTGRES_PASSWORD`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `JWT_SECRET` and replace `backend/seaweedfs/s3.json` (see [backend/README.md](backend/README.md#5-before-deploying)).

For a fuller assessment and prioritized follow-up work, see [REPO_STATUS.md](REPO_STATUS.md). Deployment steps are in [docs/DEPLOY.md](docs/DEPLOY.md).

## License

[GNU General Public License v2.0 only](LICENSE) (`GPL-2.0-only`), the same license as the Linux kernel.
