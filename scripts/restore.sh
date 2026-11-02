#!/usr/bin/env bash
# Usage: scripts/restore.sh <backup dir> --yes
# DESTRUCTIVE: replaces the database and the seaweedfs volume contents with the backup.
set -euo pipefail
cd "$(dirname "$0")/.."

[ $# -eq 2 ] && [ "$2" = "--yes" ] || { echo "usage: $0 <backup dir> --yes" >&2; exit 2; }
src="$1"
[ -s "$src/db.dump" ] && [ -s "$src/seaweedfs.tar.gz" ] || { echo "missing db.dump or seaweedfs.tar.gz in $src" >&2; exit 1; }
src="$(cd "$src" && pwd)"
# validate before anything is deleted (set -e aborts on failure)
tar tzf "$src/seaweedfs.tar.gz" > /dev/null
[ ! -f "$src/api_keys.tar.gz" ] || tar tzf "$src/api_keys.tar.gz" > /dev/null

ENV_FILE="${ENV_FILE:-.env.production}" # the compose file has required variables, so every compose call needs it
[ -f "$ENV_FILE" ] || { echo "env file $ENV_FILE not found (set ENV_FILE=...)" >&2; exit 1; }
DC=(docker compose -p arcano --env-file "$ENV_FILE" -f docker-compose.prod.yml)
VOLUME=arcano_seaweedfs_data
KEYS_VOLUME=arcano_api_keys

# pg_restore --list reads the dump without touching the database
"${DC[@]}" up -d postgres
"${DC[@]}" exec -T postgres pg_restore --list < "$src/db.dump" > /dev/null

"${DC[@]}" stop web api seaweedfs
trap 'echo "restore failed with the services stopped; fix the error and run this script again" >&2' ERR
# Empty the schema first: --clean only drops what the dump knows, so tables added by later migrations (and their foreign keys) would block it.
"${DC[@]}" exec -T postgres psql -U postgres -d arcanocore -v ON_ERROR_STOP=1 -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'
"${DC[@]}" exec -T postgres pg_restore -U postgres -d arcanocore --exit-on-error --single-transaction --no-owner < "$src/db.dump"
# wipe then extract (dotfiles included)
docker run --rm -v "$VOLUME":/data -v "$src":/backup:ro alpine \
  sh -c 'find /data -mindepth 1 -delete && tar xzf /backup/seaweedfs.tar.gz -C /data'
if [ -f "$src/api_keys.tar.gz" ]; then
  docker run --rm -v "$KEYS_VOLUME":/data -v "$src":/backup:ro alpine \
    sh -c 'find /data -mindepth 1 -delete && tar xzf /backup/api_keys.tar.gz -C /data'
fi
"${DC[@]}" up -d seaweedfs api web
echo "restored from $src"
