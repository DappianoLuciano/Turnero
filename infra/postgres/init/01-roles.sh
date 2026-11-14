#!/bin/bash
# Runs once, the first time the Postgres volume is created (docker-entrypoint-initdb.d).
# Creates the two database roles and the application database.
#   app_migrator: owns the database/schema/tables; runs EF migrations.
#   app_runtime : used by the API; NOT owner, NO BYPASSRLS, so Row-Level Security always applies.
# Table/schema grants for app_runtime are applied by the EF migrations (see InitialCreate),
# so the same setup works in Docker, CI and Testcontainers.
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-EOSQL
  CREATE ROLE app_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS
    PASSWORD '${APP_MIGRATOR_PASSWORD}';
  CREATE ROLE app_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS
    PASSWORD '${APP_RUNTIME_PASSWORD}';
  CREATE DATABASE "${APP_DB_NAME}" OWNER app_migrator;
EOSQL

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$APP_DB_NAME" <<-EOSQL
  ALTER SCHEMA public OWNER TO app_migrator;
  REVOKE ALL ON SCHEMA public FROM PUBLIC;
  GRANT CONNECT ON DATABASE "${APP_DB_NAME}" TO app_runtime;
EOSQL
