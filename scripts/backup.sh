#!/usr/bin/env bash
# Usage: scripts/backup.sh [--stop-api]
# Writes backups/<UTC timestamp>/{db.dump,seaweedfs.tar.gz,api_keys.tar.gz,env.production,seaweedfs-image.txt};
# keeps newest BACKUP_KEEP (default 7). If BACKUP_RCLONE_REMOTE is set, also copies the dir there with rclone.
# Consistency: DB and object data are copied at slightly different moments. For a point-in-time
# consistent pair pass --stop-api (api and seaweedfs are stopped while copying, then started);
# otherwise uploads during the backup can leave orphan objects (files without rows).
set -euo pipefail
cd "$(dirname "$0")/.."

ENV_FILE="${ENV_FILE:-.env.production}" # the compose file has required variables, so every compose call needs it
[ -f "$ENV_FILE" ] || { echo "env file $ENV_FILE not found (set ENV_FILE=...)" >&2; exit 1; }
DC=(docker compose -p arcano --env-file "$ENV_FILE" -f docker-compose.prod.yml)
KEEP="${BACKUP_KEEP:-7}"
VOLUME=arcano_seaweedfs_data # <project>_<volume>; check: docker compose -p arcano -f docker-compose.prod.yml config --volumes
KEYS_VOLUME=arcano_api_keys
STOP_API=0
[ "${1:-}" = "--stop-api" ] && STOP_API=1

umask 077
dir="backups/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$dir"

STOPPED=()
restart() { [ ${#STOPPED[@]} -eq 0 ] || "${DC[@]}" start "${STOPPED[@]}"; }
trap restart EXIT # restarts whatever was stopped, also on failure
if [ "$STOP_API" = 1 ]; then
  STOPPED=(seaweedfs api) # started in this order
  "${DC[@]}" stop api seaweedfs
else
  echo "warning: DB and files may not match (orphan objects possible); use --stop-api for a consistent pair" >&2
fi

"${DC[@]}" exec -T postgres pg_dump -U postgres -Fc arcanocore > "$dir/db.dump"
[ -s "$dir/db.dump" ] || { echo "empty db dump" >&2; exit 1; }
docker run --rm -v "$VOLUME":/data:ro -v "$PWD/$dir":/backup alpine tar czf /backup/seaweedfs.tar.gz -C /data .

docker run --rm -v "$KEYS_VOLUME":/data:ro -v "$PWD/$dir":/backup alpine tar czf /backup/api_keys.tar.gz -C /data .
cp "$ENV_FILE" "$dir/env.production"
chmod 600 "$dir/env.production"
# record the exact seaweedfs image so a restore can use the same one
img="$("${DC[@]}" config --images | grep -i seaweedfs)"
{ echo "$img"; docker image inspect --format '{{.Id}}' "$img"; } > "$dir/seaweedfs-image.txt"

# copying is done: bring services back now, not after the upload; the cleared list keeps the trap from starting them again
restart; STOPPED=()

# off-host copy; set -e makes a failure abort the script (and skip pruning)
if [ -n "${BACKUP_RCLONE_REMOTE:-}" ]; then
  rclone copy "$dir" "$BACKUP_RCLONE_REMOTE/$(basename "$dir")"
  echo "copied $dir to $BACKUP_RCLONE_REMOTE"
fi

# prune: timestamp dir names sort chronologically
ls -1d backups/*/ | sort | head -n -"$KEEP" | xargs -r rm -rf

echo "wrote $dir/db.dump"
echo "wrote $dir/seaweedfs.tar.gz, api_keys.tar.gz, env.production, seaweedfs-image.txt"
