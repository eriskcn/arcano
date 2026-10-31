#!/usr/bin/env bash
# Usage: scripts/restore.sh <backup dir> --yes
# DESTRUCTIVE: replaces the database and the seaweedfs volume contents with the backup.
set -euo pipefail
cd "$(dirname "$0")/.."

[ $# -eq 2 ] && [ "$2" = "--yes" ] || { echo "usage: $0 <backup dir> --yes" >&2; exit 2; }
src="$1"
[ -s "$src/db.dump" ] && [ -s "$src/seaweedfs.tar.gz" ] || { echo "missing db.dump or seaweedfs.tar.gz in $src" >&2; exit 1; }
src="$(cd "$src" && pwd)"

ENV_FILE="${ENV_FILE:-.env.production}" # the compose file has required variables, so every compose call needs it
[ -f "$ENV_FILE" ] || { echo "env file $ENV_FILE not found (set ENV_FILE=...)" >&2; exit 1; }
DC=(docker compose -p arcano --env-file "$ENV_FILE" -f docker-compose.prod.yml)
VOLUME=arcano_seaweedfs_data

"${DC[@]}" stop web api seaweedfs
"${DC[@]}" up -d postgres
"${DC[@]}" exec -T postgres pg_restore -U postgres -d arcanocore --clean --if-exists --no-owner < "$src/db.dump"
# wipe then extract (dotfiles included)
docker run --rm -v "$VOLUME":/data -v "$src":/backup:ro alpine \
  sh -c 'find /data -mindepth 1 -delete && tar xzf /backup/seaweedfs.tar.gz -C /data'
"${DC[@]}" up -d seaweedfs api web
echo "restored from $src"
