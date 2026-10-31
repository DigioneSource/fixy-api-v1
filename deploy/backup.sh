#!/usr/bin/env bash
# =============================================================================
# Sao lưu hằng ngày: CSDL (BACKUP DATABASE -> .bak.gz) + ảnh tải lên (tar.gz).
# Giữ KEEP_DAYS bản gần nhất (mặc định 7). Kết quả nằm ở ./backups/ trên máy chủ.
#   ./deploy/backup.sh
# Cron (xem deploy/README.md):
#   30 2 * * * /opt/tayzen/TAYZEN-API/deploy/backup.sh >> $HOME/tayzen-backup.log 2>&1
# Nhắc: bản sao lưu nằm cùng VPS thì mất VPS là mất hết -> chép ra ngoài (rclone).
# =============================================================================
set -euo pipefail
source "$(dirname "$0")/lib.sh"

DB_NAME="$(env_get DB_NAME TAYZEN)"
KEEP_DAYS="$(env_get BACKUP_KEEP_DAYS 7)"
TS="$(date +%Y%m%d_%H%M%S)"
BAK="/var/opt/mssql/backup/${DB_NAME}_${TS}.bak"
PROJECT="$(docker compose config --format json 2>/dev/null | python3 -c 'import sys,json;print(json.load(sys.stdin)["name"])' 2>/dev/null || echo tayzen)"

mkdir -p backups/mssql backups/uploads

echo "[$(date '+%F %T')] Sao lưu CSDL [$DB_NAME] -> $BAK"
# SQL Server Express không hỗ trợ WITH COMPRESSION -> nén bằng gzip sau đó
sqlcmd_sa <<SQL
BACKUP DATABASE [$DB_NAME] TO DISK = N'$BAK' WITH INIT, FORMAT, CHECKSUM, NAME = N'$DB_NAME-$TS';
GO
RESTORE VERIFYONLY FROM DISK = N'$BAK' WITH CHECKSUM;
GO
SQL

# Nén + xoá bản cũ ngay trong container db (file thuộc user mssql)
docker compose exec -T db sh -c "gzip -f '$BAK' && find /var/opt/mssql/backup -name '*.bak.gz' -mtime +$((KEEP_DAYS - 1)) -delete"

echo "[$(date '+%F %T')] Sao lưu ảnh tải lên (volume ${PROJECT}_api-uploads)"
docker run --rm \
  -v "${PROJECT}_api-uploads:/data:ro" \
  -v "$(pwd)/backups/uploads:/backup" \
  alpine:3.20 sh -c "tar czf /backup/uploads_${TS}.tar.gz -C /data . && find /backup -name 'uploads_*.tar.gz' -mtime +$((KEEP_DAYS - 1)) -delete"

# (Tuỳ chọn) đẩy ra ngoài VPS nếu đã cấu hình rclone, ví dụ RCLONE_REMOTE=r2:tayzen-backup
RCLONE_REMOTE="$(env_get RCLONE_REMOTE)"
if [ -n "$RCLONE_REMOTE" ] && command -v rclone >/dev/null 2>&1; then
  echo "[$(date '+%F %T')] Đẩy bản sao lưu lên $RCLONE_REMOTE"
  rclone copy backups "$RCLONE_REMOTE" --max-age 25h
fi

echo "[$(date '+%F %T')] Xong. Hiện có:"
ls -lh backups/mssql | tail -n +2
ls -lh backups/uploads | tail -n +2
