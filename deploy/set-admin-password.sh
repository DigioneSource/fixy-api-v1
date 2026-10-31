#!/usr/bin/env bash
# =============================================================================
# Đặt email + mật khẩu cho tài khoản ADMIN đầu tiên.
#
# Migration của dự án tự tạo sẵn 1 admin (UserSeeder.cs, Id f1a7d8c1-...-2d7f4c5e1000)
# với email admin@fixy.com và mật khẩu MẶC ĐỊNH công khai trong mã nguồn.
# Script này ghi đè email + mật khẩu đó. BẮT BUỘC chạy ngay sau migration lần đầu.
#
#   ADMIN_EMAIL=ceo@tayzen.vn ADMIN_PASSWORD='...' ./deploy/set-admin-password.sh
# hoặc điền ADMIN_EMAIL / ADMIN_PASSWORD trong .env rồi chạy, XONG thì XOÁ ADMIN_PASSWORD khỏi .env.
# Không truyền gì: script hỏi mật khẩu (gõ ẩn).
# =============================================================================
set -euo pipefail
source "$(dirname "$0")/lib.sh"

ADMIN_USER_ID="f1a7d8c1-3e21-4a8c-9b11-2d7f4c5e1000"
DB_NAME="$(env_get DB_NAME TAYZEN)"
ADMIN_EMAIL="$(env_get ADMIN_EMAIL)"
ADMIN_PASSWORD="$(env_get ADMIN_PASSWORD)"

if [ -z "$ADMIN_EMAIL" ]; then
  read -r -p "Email admin: " ADMIN_EMAIL
fi
if [ -z "$ADMIN_PASSWORD" ]; then
  read -r -s -p "Mật khẩu admin (ít nhất 12 ký tự): " ADMIN_PASSWORD; echo
  read -r -s -p "Nhập lại: " ADMIN_PASSWORD2; echo
  [ "$ADMIN_PASSWORD" = "$ADMIN_PASSWORD2" ] || { echo "Hai lần nhập không khớp" >&2; exit 1; }
fi
if [ "${#ADMIN_PASSWORD}" -lt 12 ]; then
  echo "Mật khẩu admin phải có ít nhất 12 ký tự" >&2
  exit 1
fi
if ! [[ "$ADMIN_EMAIL" =~ ^[^[:space:]@\']+@[^[:space:]@\']+\.[^[:space:]@\']+$ ]]; then
  echo "Email không hợp lệ" >&2
  exit 1
fi

echo "==> Tạo băm BCrypt (giống BCrypt.Net trong API)..."
# htpasswd -B tạo băm bcrypt; mật khẩu đi qua stdin nên không hiện trong danh sách tiến trình
HASH="$(printf '%s' "$ADMIN_PASSWORD" | docker run --rm -i httpd:2.4-alpine htpasswd -niBC 11 admin | cut -d: -f2- | tr -d '\r\n')"
# $2y$ và $2a$ cùng thuật toán; đổi về $2a$ cho chắc tương thích BCrypt.Net
HASH="\$2a\$${HASH#\$2y\$}"
if ! [[ "$HASH" =~ ^\$2a\$11\$.{53}$ ]]; then
  echo "Tạo băm thất bại" >&2
  exit 1
fi

echo "==> Cập nhật tài khoản admin trong CSDL [$DB_NAME]..."
sqlcmd_sa <<SQL
SET NOCOUNT ON;
USE [$DB_NAME];
IF NOT EXISTS (SELECT 1 FROM Users WHERE Id = '$ADMIN_USER_ID')
    THROW 50001, N'Không thấy admin seed. Đã chạy ./deploy/migrate.sh chưa?', 1;
IF EXISTS (SELECT 1 FROM Users WHERE Email = N'$(sql_escape "$ADMIN_EMAIL")' AND Id <> '$ADMIN_USER_ID')
    THROW 50002, N'Email này đã thuộc về tài khoản khác.', 1;
UPDATE Users
SET Email = N'$(sql_escape "$ADMIN_EMAIL")',
    PasswordHash = N'$HASH',
    IsEmailVerified = 1,
    IsActive = 1
WHERE Id = '$ADMIN_USER_ID';
PRINT N'Đã cập nhật admin.';
GO
SQL

echo "==> Xong. Đăng nhập POST /api/auth/login với target = $ADMIN_EMAIL."
echo "    Nhớ XOÁ dòng ADMIN_PASSWORD khỏi .env nếu đã điền ở đó."
