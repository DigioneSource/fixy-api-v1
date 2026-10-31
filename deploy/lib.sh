#!/usr/bin/env bash
# Hàm dùng chung cho các script trong deploy/. Được "source", không chạy trực tiếp.

# Về thư mục gốc repo (nơi có docker-compose.yml và .env)
cd "$(dirname "${BASH_SOURCE[0]}")/.." || exit 1

if [ ! -f .env ]; then
  echo "Không thấy file .env ở $(pwd). Chép từ .env.example rồi điền giá trị." >&2
  exit 1
fi

# Đọc 1 khoá từ .env mà KHÔNG source cả file (giá trị có &, ;, khoảng trắng... sẽ phá bash).
# Ưu tiên biến môi trường đang có, sau đó tới .env. Bỏ dấu nháy bao ngoài nếu có.
env_get() {
  local key="$1" default="${2:-}" val
  if [ -n "${!key:-}" ]; then
    printf '%s' "${!key}"
    return
  fi
  val="$(grep -E "^${key}=" .env | tail -n 1 | cut -d= -f2-)"
  val="${val%$'\r'}"
  if [[ "$val" == \"*\" || "$val" == \'*\' ]]; then
    val="${val:1:${#val}-2}"
  fi
  printf '%s' "${val:-$default}"
}

# Escape dấu nháy đơn cho chuỗi T-SQL N'...'
sql_escape() {
  printf '%s' "${1//\'/\'\'}"
}

# Chạy T-SQL đọc từ stdin trong container db, đăng nhập sa.
# Mật khẩu sa lấy từ biến môi trường của chính container, không lộ trên dòng lệnh máy chủ.
sqlcmd_sa() {
  docker compose exec -T db sh -c \
    'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -b -I'
}
