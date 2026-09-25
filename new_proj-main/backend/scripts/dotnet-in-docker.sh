#!/usr/bin/env bash
# Runs the backend toolchain inside the official .NET SDK container, attached to the docker-compose network
# (Postgres is reachable there as educenter-postgres:5432).
#
# Why: on a Windows host with Smart App Control enforced, freshly built unsigned assemblies can be refused
# ("An Application Control policy has blocked this file", 0x800711C7) — then neither `dotnet test`,
# `dotnet ef` nor `dotnet run` work on the host. A Linux container is not subject to that policy.
#
# Usage (from anywhere, Git Bash / WSL / Linux / macOS):
#   backend/scripts/dotnet-in-docker.sh build                # compile only, fastest check
#   backend/scripts/dotnet-in-docker.sh test                 # build + run every test (isolated educenter_test DB)
#   TEST_FILTER="FullyQualifiedName~RoomTests" backend/scripts/dotnet-in-docker.sh test   # only the matching tests
#   EDUCENTER_CLAMAV_HOST=educenter-clamav backend/scripts/dotnet-in-docker.sh test        # also run the test against a real clamd
#                                                            # (`docker compose up -d clamav` in backend/ first; skipped otherwise)
#   backend/scripts/dotnet-in-docker.sh migration <Name>     # add an EF migration, copied back into the repo
#   backend/scripts/dotnet-in-docker.sh update-db            # apply migrations to the dev database `educenter`
#   backend/scripts/dotnet-in-docker.sh api                  # run the API on http://localhost:5167 (detached)
#   backend/scripts/dotnet-in-docker.sh api-stop
#
# The source is copied into the container without bin/obj, so container builds never touch host build output.
# NuGet packages and the dotnet-ef tool are cached in named volumes, so only the first run downloads them.
set -euo pipefail

CMD="${1:-test}"
NAME="${2:-}"
IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"
NETWORK="backend_default"
API_CONTAINER="educenter-api"
DEV_DB="Host=educenter-postgres;Port=5432;Database=educenter;Username=educenter;Password=educenter_dev_only"

script_dir="$(cd "$(dirname "$0")" && pwd)"
if pwd -W >/dev/null 2>&1; then
  backend="$(cd "$script_dir/.." && pwd -W)"   # Git Bash: docker needs a Windows path (D:/...)
else
  backend="$(cd "$script_dir/.." && pwd)"
fi

if [[ "$CMD" == "migration" && -z "$NAME" ]]; then
  echo "usage: $0 migration <MigrationName>" >&2
  exit 2
fi

if [[ "$CMD" == "api-stop" ]]; then
  docker rm -f "$API_CONTAINER" >/dev/null 2>&1 && echo "stopped $API_CONTAINER" || echo "$API_CONTAINER is not running"
  exit 0
fi

# Git Bash would otherwise rewrite container paths such as /work into C:/Program Files/Git/work.
export MSYS_NO_PATHCONV=1

run_args=(--rm --network "$NETWORK"
  -e CMD="$CMD" -e NAME="$NAME" -e DEV_DB="$DEV_DB" -e TEST_FILTER="${TEST_FILTER:-}"
  -e EDUCENTER_TEST_DB_SERVER="Host=educenter-postgres;Port=5432"
  -e EDUCENTER_CLAMAV_HOST="${EDUCENTER_CLAMAV_HOST:-}"
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1
  -v "$backend:/repo"
  -v educenter-nuget:/root/.nuget/packages
  -v educenter-dotnet-tools:/root/.dotnet/tools)

if [[ "$CMD" == "api" ]]; then
  docker rm -f "$API_CONTAINER" >/dev/null 2>&1 || true
  # Uploaded files (knowledge base, chat attachments, speaking audio) live under the API's ContentRoot in
  # App_Data. That path is inside /work, which is a fresh copy of the source in a container this script
  # deletes and recreates on every `api` run — so without this volume every upload survives exactly until
  # the next restart, and the rows left behind in the database answer "Fayl saqlash joyida topilmadi".
  run_args=(-d --name "$API_CONTAINER" -p 5167:5167
    -e ASPNETCORE_ENVIRONMENT=Development -e ASPNETCORE_URLS=http://0.0.0.0:5167
    -e ConnectionStrings__Postgres="$DEV_DB"
    -e Seed__BulkDemoData=true
    -v educenter-app-data:/work/src/EduCenter.Api/App_Data "${run_args[@]:1}")
fi

docker run "${run_args[@]}" -w /work "$IMAGE" bash -c '
set -euo pipefail
mkdir -p /work
(cd /repo && tar --exclude=bin --exclude=obj --exclude=.vs -cf - .) | tar -xf - -C /work

ensure_ef() {
  export PATH="$PATH:/root/.dotnet/tools"
  command -v dotnet-ef >/dev/null || dotnet tool install --global dotnet-ef --version "10.0.*" >/dev/null
}

case "$CMD" in
  build)
    dotnet build EduCenter.slnx -v q --nologo
    ;;
  test)
    # TEST_FILTER="FullyQualifiedName~OrganizationSettingsTests" narrows the run while iterating; unset = everything.
    if [[ -n "$TEST_FILTER" ]]; then dotnet test EduCenter.slnx --filter "$TEST_FILTER"; else dotnet test EduCenter.slnx; fi
    ;;
  migration)
    ensure_ef
    dotnet build EduCenter.slnx -v q
    cd src/EduCenter.Infrastructure
    dotnet ef migrations add "$NAME" --startup-project ../EduCenter.Api --no-build
    cp Migrations/*_"$NAME".cs Migrations/*_"$NAME".Designer.cs Migrations/AppDbContextModelSnapshot.cs \
       /repo/src/EduCenter.Infrastructure/Migrations/
    echo "migration $NAME copied into the repository"
    ;;
  update-db)
    ensure_ef
    dotnet build EduCenter.slnx -v q
    cd src/EduCenter.Infrastructure
    ConnectionStrings__Postgres="$DEV_DB" dotnet ef database update --startup-project ../EduCenter.Api --no-build
    ;;
  api)
    dotnet run --project src/EduCenter.Api --no-launch-profile
    ;;
  *)
    echo "unknown command: $CMD (build | test | migration <Name> | update-db | api | api-stop)" >&2
    exit 2
    ;;
esac
'

if [[ "$CMD" == "api" ]]; then
  echo "API starting in container $API_CONTAINER — follow with: docker logs -f $API_CONTAINER"
fi
