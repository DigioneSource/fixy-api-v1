#!/usr/bin/env bash
# =============================================================================
# Tạo CSDL + login ứng dụng (nếu chưa có) rồi chạy migration EF Core.
# An toàn khi chạy lại nhiều lần: migration nào đã áp dụng sẽ bỏ qua.
#   ./deploy/migrate.sh
# Nên chạy ./deploy/backup.sh TRƯỚC khi migrate trên CSDL đang có dữ liệu thật.
# =============================================================================
set -euo pipefail
source "$(dirname "$0")/lib.sh"

DB_NAME="$(env_get DB_NAME TAYZEN)"
DB_APP_USER="$(env_get DB_APP_USER tayzen_app)"
DB_APP_PASSWORD="$(env_get DB_APP_PASSWORD)"

if [ -z "$DB_APP_PASSWORD" ]; then
  echo "Thiếu DB_APP_PASSWORD trong .env" >&2
  exit 1
fi
if ! [[ "$DB_NAME" =~ ^[A-Za-z0-9_]+$ && "$DB_APP_USER" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "DB_NAME / DB_APP_USER chỉ được chứa chữ, số, dấu gạch dưới" >&2
  exit 1
fi

echo "==> Khởi động SQL Server và chờ sẵn sàng..."
docker compose up -d --wait db

echo "==> Tạo CSDL [$DB_NAME] và login [$DB_APP_USER] (nếu chưa có)..."
PW_SQL="$(sql_escape "$DB_APP_PASSWORD")"
sqlcmd_sa <<SQL
IF DB_ID(N'$DB_NAME') IS NULL
    CREATE DATABASE [$DB_NAME];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$DB_APP_USER')
    CREATE LOGIN [$DB_APP_USER] WITH PASSWORD = N'$PW_SQL', DEFAULT_DATABASE = [$DB_NAME], CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [$DB_APP_USER] WITH PASSWORD = N'$PW_SQL';
GO
USE [$DB_NAME];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$DB_APP_USER')
    CREATE USER [$DB_APP_USER] FOR LOGIN [$DB_APP_USER];
-- API chỉ cần đọc/ghi dữ liệu; đổi cấu trúc bảng là việc của migration (chạy bằng sa)
ALTER ROLE db_datareader ADD MEMBER [$DB_APP_USER];
ALTER ROLE db_datawriter ADD MEMBER [$DB_APP_USER];
GO
SQL

echo "==> Dựng image migration..."
docker compose --profile tools build migrate

echo "==> Áp dụng migration (bảng __EFMigrationsHistory ghi lại cái đã chạy)..."
docker compose --profile tools run --rm migrate

echo "==> Xong. Nếu đây là lần đầu: chạy ./deploy/set-admin-password.sh để đổi mật khẩu admin mặc định."
