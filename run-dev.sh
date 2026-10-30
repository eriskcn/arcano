#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
FRONTEND_DIR="$ROOT_DIR/frontend"
BACKEND_DIR="$ROOT_DIR/backend"
API_PROJECT="$BACKEND_DIR/src/ArcanoCore.API/ArcanoCore.API.csproj"
JWT_SECRET="${JWT_SECRET:-$(openssl rand -hex 32)}"

api_pid=''
frontend_pid=''

cleanup() {
  trap - EXIT INT TERM
  [[ -z "$frontend_pid" ]] || kill "$frontend_pid" 2>/dev/null || true
  [[ -z "$api_pid" ]] || kill "$api_pid" 2>/dev/null || true
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM

for command in docker dotnet npm openssl; do
  if ! command -v "$command" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command" >&2
    exit 1
  fi
done

if [[ ! -d "$FRONTEND_DIR/node_modules" ]]; then
  (cd "$FRONTEND_DIR" && npm ci)
fi

JWT_SECRET="$JWT_SECRET" docker compose -f "$BACKEND_DIR/docker-compose.yml" up -d postgres seaweedfs

(
  cd "$BACKEND_DIR"
  ASPNETCORE_ENVIRONMENT=Development \
    Jwt__Secret="$JWT_SECRET" \
    dotnet run --project "$API_PROJECT" --urls http://localhost:5000
) &
api_pid=$!

(
  cd "$FRONTEND_DIR"
  npm start -- --host 0.0.0.0
) &
frontend_pid=$!

printf 'API:      http://localhost:5000\nFrontend: http://localhost:4200\nPress Ctrl+C to stop the API and frontend.\n'
wait -n "$api_pid" "$frontend_pid"
