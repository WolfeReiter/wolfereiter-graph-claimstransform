#!/usr/bin/env bash
# Runs the whole test suite, including the SqlServer and PgSql integration tests, against
# throwaway SQL Server 2022 and PostgreSQL 17 containers. Requires Docker.
set -euo pipefail

cd "$(dirname "$0")/.."

PG_NAME=grouprolemap-test-pgsql
SQL_NAME=grouprolemap-test-sqlserver
PG_PORT=${PG_PORT:-55432}
SQL_PORT=${SQL_PORT:-51433}
PASSWORD='Test_pass12!'

cleanup() { docker rm -f "$PG_NAME" "$SQL_NAME" >/dev/null 2>&1 || true; }
trap cleanup EXIT
cleanup

docker run -d --name "$PG_NAME" -e POSTGRES_PASSWORD="$PASSWORD" -p "$PG_PORT:5432" postgres:17 >/dev/null
docker run -d --name "$SQL_NAME" -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$PASSWORD" \
  -p "$SQL_PORT:1433" mcr.microsoft.com/mssql/server:2022-latest >/dev/null

echo "Waiting for PostgreSQL and SQL Server"
for _ in $(seq 1 60); do
  docker exec "$PG_NAME" pg_isready -U postgres >/dev/null 2>&1 \
    && docker logs "$SQL_NAME" 2>&1 | grep -q "Recovery is complete" && break
  sleep 3
done
sleep 2

export GROUPROLEMAP_TEST_PGSQL="Host=localhost;Port=$PG_PORT;Database=postgres;Username=postgres;Password=$PASSWORD"
export GROUPROLEMAP_TEST_SQLSERVER="Server=localhost,$SQL_PORT;Database=master;User Id=sa;Password=$PASSWORD;TrustServerCertificate=True"

dotnet test "$@"
