#!/bin/bash
# Runs once, when the Postgres data volume is first created (docker-entrypoint-initdb.d).
#
# The application never connects as the superuser. It gets its own role that owns one database and nothing else: no
# SUPERUSER, no CREATEDB, no CREATEROLE — so even a full compromise of the API cannot read other databases, create
# roles, or touch the server. The `vector` extension (pgvector, used for AI search) needs superuser to be created, so it
# is created here, ahead of the migrations; their `CREATE EXTENSION IF NOT EXISTS` is then a no-op.
set -euo pipefail

: "${APP_DB_USER:?}" "${APP_DB_PASSWORD:?}" "${APP_DB_NAME:?}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<SQL
CREATE ROLE "${APP_DB_USER}" LOGIN PASSWORD '${APP_DB_PASSWORD}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
CREATE DATABASE "${APP_DB_NAME}" OWNER "${APP_DB_USER}";
REVOKE ALL ON DATABASE "${APP_DB_NAME}" FROM PUBLIC;
SQL

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$APP_DB_NAME" <<SQL
CREATE EXTENSION IF NOT EXISTS vector;
SQL

echo "init-app-role: role '${APP_DB_USER}' and database '${APP_DB_NAME}' created."
