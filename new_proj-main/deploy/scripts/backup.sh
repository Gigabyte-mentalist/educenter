#!/usr/bin/env bash
# Backs up the production database and the uploaded files into deploy/backups/ (git-ignored).
#
#   deploy/scripts/backup.sh              writes db-<time>.dump and files-<time>.tar.gz, deletes ones older than 14 days
#   KEEP_DAYS=30 deploy/scripts/backup.sh
#
# Restore (database):  docker compose --env-file .env -f docker-compose.yml exec -T db \
#                        pg_restore -U postgres -d educenter --clean --if-exists < backups/db-<time>.dump
# The dumps contain every student's personal data — keep them on this machine, or encrypt them before they leave it.
set -euo pipefail

cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1

KEEP_DAYS="${KEEP_DAYS:-14}"
STAMP="$(date +%Y%m%d-%H%M%S)"
COMPOSE=(docker compose --env-file .env -f docker-compose.yml)
DB_NAME="$(grep -E '^APP_DB_NAME=' .env | cut -d= -f2)"; DB_NAME="${DB_NAME:-educenter}"

mkdir -p backups
if command -v icacls >/dev/null 2>&1 && command -v cygpath >/dev/null 2>&1; then
  icacls "$(cygpath -w backups)" /inheritance:r /grant:r "${USERNAME}:(OI)(CI)F" >/dev/null 2>&1 || true
fi
umask 077

"${COMPOSE[@]}" exec -T db pg_dump -U postgres -Fc "$DB_NAME" > "backups/db-${STAMP}.dump"
[[ -s "backups/db-${STAMP}.dump" ]] || { rm -f "backups/db-${STAMP}.dump"; echo "error: empty database dump" >&2; exit 1; }

# The uploads live in a named volume; read it through a throw-away container (the caddy image already has tar).
docker run --rm --entrypoint tar \
  -v educenter-prod_appdata:/data:ro -v "$(pwd -W 2>/dev/null || pwd)/backups:/backup" \
  caddy:2-alpine czf "/backup/files-${STAMP}.tar.gz" -C /data .

find backups -type f \( -name 'db-*.dump' -o -name 'files-*.tar.gz' \) -mtime +"$KEEP_DAYS" -delete

echo "Backup written: backups/db-${STAMP}.dump, backups/files-${STAMP}.tar.gz"
ls -lh backups | tail -n +2
