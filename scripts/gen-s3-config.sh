#!/usr/bin/env bash
# Writes backend/seaweedfs/s3.prod.json from S3_ACCESS_KEY / S3_SECRET_KEY (env, or env file as $1).
set -euo pipefail
cd "$(dirname "$0")/.."

if [ $# -ge 1 ]; then
  [ -f "$1" ] || { echo "env file not found: $1" >&2; exit 1; }
  set -a; . "$1"; set +a
fi

key="${S3_ACCESS_KEY:-}"; secret="${S3_SECRET_KEY:-}"
[ -n "$key" ] && [ -n "$secret" ] || { echo "S3_ACCESS_KEY and S3_SECRET_KEY must be set" >&2; exit 1; }
case "$key$secret" in
  *any_key*|*any_secret*|*CHANGE_ME*) echo "refusing dev/placeholder credentials" >&2; exit 1 ;;
esac
# Values go into JSON unescaped; hex/alphanumeric keys only.
[[ "$key$secret" =~ ^[A-Za-z0-9_+=./-]+$ ]] || { echo "credentials may only contain [A-Za-z0-9_+=./-]" >&2; exit 1; }

out=backend/seaweedfs/s3.prod.json
umask 077
cat > "$out" <<JSON
{
  "identities": [
    {
      "name": "arcano",
      "credentials": [
        { "accessKey": "$key", "secretKey": "$secret" }
      ],
      "actions": ["Admin", "Read", "Write", "List", "Tagging"]
    }
  ]
}
JSON
chmod 600 "$out"
echo "wrote $out"
