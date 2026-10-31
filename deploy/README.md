# Triển khai TAYZEN API trên VPS Ubuntu (Docker + Cloudflare Tunnel)

Tài liệu này hướng dẫn dựng backend TAYZEN (.NET 8) trên một VPS Ubuntu mới tại Việt Nam để thay Azure App Service.

```
Điện thoại / Web ──HTTPS──► Cloudflare ──(Tunnel, chiều ra)──► cloudflared ─► api:8080 ─┬─► db (SQL Server Express)
                                                                                         └─► redis
```

- VPS **không mở cổng 80/443**. Cổng duy nhất mở ra Internet là SSH. Lưu lượng web đi qua Cloudflare Tunnel, do `cloudflared` chủ động kết nối ra ngoài.
- `db` và `redis` nằm trên mạng nội bộ `backend` (`internal: true`): không publish cổng, cũng không có đường ra Internet.
- Mọi bí mật nằm trong file `.env` trên VPS, không đưa vào git hay image.

| File | Vai trò |
|---|---|
| `Dockerfile` | Build nhiều tầng: target `runtime` là API, target `migrator` là gói migration EF Core (`efbundle`) |
| `docker-compose.yml` | Gồm `api`, `db`, `redis`, `cloudflared`, cùng `migrate` (profile `tools`, chỉ chạy khi gọi) |
| `.env.example` | Danh sách đầy đủ biến cấu hình, có chú thích |
| `deploy/migrate.sh` | Tạo CSDL và login ứng dụng, rồi chạy migration |
| `deploy/set-admin-password.sh` | Đặt email và mật khẩu cho admin đầu tiên |
| `deploy/backup.sh` | Sao lưu CSDL và ảnh, giữ 7 bản |

---

## 0. Yêu cầu tối thiểu cho VPS

| Hạng mục | Tối thiểu | Khuyến nghị |
|---|---|---|
| CPU | 2 vCPU **x86_64 (amd64)** | 4 vCPU |
| RAM | 4 GB | 8 GB |
| Ổ đĩa | 40 GB SSD | 80 GB NVMe |
| Hệ điều hành | Ubuntu 22.04 hoặc 24.04 LTS | 24.04 LTS |

Vì sao cần chừng ấy:
- **SQL Server cần ít nhất 2 GB RAM** trên máy, nếu thiếu sẽ không khởi động được. Image SQL Server **chỉ có bản amd64**, nên **không thuê VPS ARM**.
- SQL Server được giới hạn khoảng 1,5 GB (`MSSQL_MEMORY_LIMIT_MB`). API dùng 300–600 MB, tăng thêm khi chạy nhận diện khuôn mặt ONNX. Redis dùng tối đa 256 MB.
- Bản Express giới hạn **10 GB cho mỗi CSDL**. Khi sắp chạm ngưỡng thì chuyển sang bản Standard (cần license) hoặc dọn bớt dữ liệu.
- Ảnh tải lên nằm trên ổ VPS (volume `api-uploads`). Mỗi ảnh tối đa 5 MB, nên cần theo dõi dung lượng ổ đĩa.

---

## 1. Chuẩn bị VPS (chạy bằng root, một lần)

```bash
# Cập nhật hệ thống, bật tự cập nhật bản vá bảo mật
apt update && apt -y upgrade
apt -y install ca-certificates curl git ufw fail2ban unattended-upgrades
dpkg-reconfigure -plow unattended-upgrades

# Múi giờ máy chủ (cron sao lưu chạy theo giờ VN)
timedatectl set-timezone Asia/Ho_Chi_Minh

# Tạo user deploy và cho dùng sudo
adduser deploy
usermod -aG sudo deploy
# Chép SSH key của bạn sang user deploy
mkdir -p /home/deploy/.ssh && cp ~/.ssh/authorized_keys /home/deploy/.ssh/
chown -R deploy:deploy /home/deploy/.ssh && chmod 700 /home/deploy/.ssh && chmod 600 /home/deploy/.ssh/authorized_keys

# Tắt đăng nhập SSH bằng mật khẩu và đăng nhập root
# (thử đăng nhập bằng user deploy ở một cửa sổ khác TRƯỚC khi làm bước này)
sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/; s/^#\?PermitRootLogin.*/PermitRootLogin no/' /etc/ssh/sshd_config
systemctl restart ssh

# Tường lửa: CHỈ mở SSH
ufw default deny incoming
ufw default allow outgoing
ufw allow OpenSSH
ufw enable
ufw status verbose

# Swap 2 GB, phòng khi thiếu RAM lúc build image
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

> Lưu ý: Docker tự thêm luật iptables cho **cổng được publish**, và ufw không chặn được các luật này. Compose của dự án **không publish cổng nào**, nên điều này không gây rủi ro. Đừng thêm `ports:` cho `db` hay `redis`.

## 2. Cài Docker

```bash
curl -fsSL https://get.docker.com | sh
usermod -aG docker deploy
# Đăng xuất rồi đăng nhập lại bằng user deploy, sau đó kiểm tra:
docker version && docker compose version     # cần Compose v2.20 trở lên
```

## 3. Lấy mã nguồn

Repo là private, nên tạo **deploy key** (chỉ quyền đọc) cho VPS:

```bash
# Đăng nhập bằng user deploy
ssh-keygen -t ed25519 -C "tayzen-vps" -f ~/.ssh/tayzen_deploy -N ""
cat ~/.ssh/tayzen_deploy.pub
# Dán vào GitHub: repo > Settings > Deploy keys > Add (KHÔNG tick "Allow write access")

cat >> ~/.ssh/config <<'EOF'
Host github-tayzen
  HostName github.com
  IdentityFile ~/.ssh/tayzen_deploy
  IdentitiesOnly yes
EOF

sudo mkdir -p /opt/tayzen && sudo chown deploy:deploy /opt/tayzen
cd /opt/tayzen
git clone git@github-tayzen:<owner>/TAYZEN-API.git
cd TAYZEN-API
git checkout tayzen/docker-selfhost      # hoặc nhánh đã gộp sau này
```

## 4. Điền cấu hình `.env`

```bash
cp .env.example .env
chmod 600 .env
nano .env
```

Sinh mật khẩu mạnh (chỉ gồm chữ và số, đủ hoa, thường, số):

```bash
openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 32; echo
```

Các biến **bắt buộc** để chạy được:

| Biến | Ghi chú |
|---|---|
| `MSSQL_SA_PASSWORD` | Mật khẩu sa |
| `DB_APP_PASSWORD` | Mật khẩu login `tayzen_app` mà API dùng |
| `REDIS_PASSWORD` | |
| `CLOUDFLARE_TUNNEL_TOKEN` | Lấy ở bước 5 |
| `JwtSettings__Secret` | Chuỗi ngẫu nhiên từ 32 ký tự trở lên |
| `JwtSettings__Issuer`, `JwtSettings__Audience` | Ví dụ `https://api.tayzen.vn`. **Nếu muốn token đang dùng trên Azure vẫn hợp lệ** thì dùng lại đúng Secret, Issuer, Audience cũ |
| `Storage__Local__PublicBaseUrl` | `https://api.tayzen.vn` |

Các biến còn lại (thanh toán, SMTP, SMS, Google, Facebook, Goong, Firebase) lấy giá trị từ cấu hình Azure App Service hiện tại: Azure Portal > App Service `fixy-api-v1` > Settings > Environment variables. Tên trên Azure dạng `JwtSettings__Secret` hoặc `JwtSettings:Secret` đều tương ứng với cùng một khoá trong `.env`.

> **Đừng để trống các khoá kiểu số hoặc kiểu đúng/sai** như `JwtSettings__AccessTokenExpirationMinutes`, `SmtpSettings__Port`, `SmsSettings__UseMock`. Chuỗi rỗng khiến .NET không chuyển được sang số và API sẽ lỗi. Nếu không cần đổi thì xoá hẳn dòng đó, compose sẽ dùng giá trị mặc định.

Tạo thư mục cho bí mật dạng file và cho bản sao lưu:

```bash
mkdir -p secrets backups/mssql backups/uploads
chmod 700 secrets
# SQL Server trong container chạy bằng UID 10001 và cần quyền ghi file .bak
sudo chown 10001:0 backups/mssql
# (Tuỳ chọn) Firebase push: chép file service account vào secrets/firebase.json, rồi trong .env đặt
#   Firebase__CredentialJsonPath=/run/secrets/tayzen/firebase.json
# Container chạy bằng UID 1654 nên file cần cho phép đọc:
#   chmod 644 secrets/firebase.json   (thư mục secrets đã ở chế độ 700 để chặn user khác trên VPS)
```

## 5. Tạo Cloudflare Tunnel cho `api.tayzen.vn`

Điều kiện: tên miền `tayzen.vn` đã dùng nameserver của Cloudflare.

1. Vào https://one.dash.cloudflare.com, chọn **Networks > Tunnels > Create a tunnel**, loại **Cloudflared**, đặt tên `tayzen-vps`.
2. Ở bước chọn môi trường, chọn **Docker**. Trong lệnh mẫu, chép phần token đứng sau `--token` rồi dán vào `.env` ở dòng `CLOUDFLARE_TUNNEL_TOKEN=`. Không cần chạy lệnh mẫu, vì compose đã có service `cloudflared`.
3. Tab **Public Hostname > Add a public hostname**:
   - Subdomain `api`, Domain `tayzen.vn`
   - Service: **Type `HTTP`**, URL **`api:8080`**
   - Không cần đổi gì thêm. WebSocket cho SignalR (`/hubs/*`) đã bật sẵn.
4. Trong Cloudflare, mục SSL/TLS của tên miền: chọn chế độ **Full**. Kiểm tra **Network > WebSockets** đang **On**.
5. (Nên làm) Mục **Security > WAF**: thêm luật giới hạn tần suất cho `/api/auth/*` và `/api/worker-profiles/verify-face`.

## 6. Chạy lần đầu

```bash
cd /opt/tayzen/TAYZEN-API

# 6.1 Dựng image API (lần đầu mất 5–10 phút)
docker compose build api

# 6.2 Tạo CSDL, login ứng dụng, chạy migration (tự bật db và chờ db sẵn sàng)
./deploy/migrate.sh

# 6.3 Đặt email và mật khẩu cho admin (BẮT BUỘC, xem mục 7)
./deploy/set-admin-password.sh

# 6.4 Bật toàn bộ
docker compose up -d
docker compose ps                 # api phải ở trạng thái "healthy"
curl -fsS https://api.tayzen.vn/health    # kết quả mong đợi: Healthy
```

## 7. Tài khoản ADMIN đầu tiên

Migration tự tạo sẵn một admin (`Infrastructure/Persistence/Seeds/UserSeeder.cs`) với email `admin@fixy.com`, số điện thoại `0000000000` và **mật khẩu mặc định ghi ngay trong mã nguồn**. Ai đọc được repo cũng biết mật khẩu này, nên **phải đổi ngay sau migration lần đầu**:

```bash
# Cách 1: script hỏi mật khẩu và gõ ẩn
./deploy/set-admin-password.sh

# Cách 2: truyền qua biến môi trường (dấu cách ở đầu dòng giúp lệnh không bị lưu vào lịch sử shell)
 ADMIN_EMAIL=ceo@tayzen.vn ADMIN_PASSWORD='MatKhauRatDai123' ./deploy/set-admin-password.sh
```

Script tạo băm BCrypt (tương thích với `BCrypt.Net` của API), rồi ghi đè email và mật khẩu của đúng tài khoản seed (Id `f1a7d8c1-3e21-4a8c-9b11-2d7f4c5e1000`). Mật khẩu không được ghi vào mã, vào git hay vào log. Nếu đã điền `ADMIN_PASSWORD` trong `.env` thì **xoá dòng đó** sau khi chạy xong.

Đăng nhập: `POST https://api.tayzen.vn/api/auth/login` với body `{"target":"<email>","password":"..."}`.

> ⚠️ Nếu sau này có người sửa chuỗi băm mật khẩu trong `UserSeeder.cs`, EF sẽ sinh migration ghi đè mật khẩu admin. Khi đó phải chạy lại `set-admin-password.sh` sau migrate.

## 8. Nạp danh mục dịch vụ ban đầu

- `scripts/SeedData.sql` **hiện chỉ là file mẫu rỗng**, chưa có dữ liệu.
- **Danh mục dịch vụ tại nhà** (`ServiceCategories`): tạo qua API admin, mỗi danh mục một lần gọi:

```bash
TOKEN=$(curl -s https://api.tayzen.vn/api/auth/login -H 'Content-Type: application/json' \
  -d '{"target":"ceo@tayzen.vn","password":"..."}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["data"]["accessToken"])')
# (in thử JSON trả về nếu tên trường khác)

curl -s https://api.tayzen.vn/api/service-categories -H "Authorization: Bearer $TOKEN" \
  -F 'Name=Massage body' -F 'Description=Massage toàn thân tại nhà' -F 'SortOrder=1' -F 'IsActive=true' \
  -F 'ImageFile=@./massage.jpg'
```

- **Dữ liệu spa mẫu** (`POST /api/seed/spa-data`, yêu cầu ADMIN, nằm trong `SpaDataSeederController`): endpoint này tạo 5 danh mục spa cùng **5 spa đối tác giả ở Đà Nẵng**, kèm dịch vụ và khuyến mãi. Chỉ dùng cho **môi trường thử nghiệm**. **Không gọi trên production**, vì khách sẽ thấy spa không có thật. Endpoint tự bỏ qua nếu bảng `SpaServiceCategories` đã có dữ liệu.

```bash
curl -s -X POST https://api.tayzen.vn/api/seed/spa-data -H "Authorization: Bearer $TOKEN"   # CHỈ trên staging
```

- Danh mục spa thật: dùng `POST /api/spa-service-categories` (ADMIN), cách gọi tương tự như trên.

## 9. Sao lưu hằng ngày

`deploy/backup.sh` làm các việc sau:
1. Chạy `BACKUP DATABASE ... WITH CHECKSUM`, rồi `RESTORE VERIFYONLY` để kiểm tra bản sao lưu đọc lại được.
2. Nén thành `backups/mssql/TAYZEN_YYYYMMDD_HHMMSS.bak.gz`. Bản Express không có nén sẵn nên script dùng gzip.
3. Đóng gói volume ảnh thành `backups/uploads/uploads_*.tar.gz`.
4. Xoá các bản cũ hơn 7 ngày (đổi bằng `BACKUP_KEEP_DAYS`).
5. Nếu có `RCLONE_REMOTE` và máy đã cài rclone thì chép bản sao lưu ra ngoài VPS.

Đặt cron, chạy bằng root vì file .bak thuộc UID 10001:

```bash
sudo crontab -e
# thêm dòng (2h30 sáng mỗi ngày):
30 2 * * * /opt/tayzen/TAYZEN-API/deploy/backup.sh >> /var/log/tayzen-backup.log 2>&1
```

Chạy thử ngay một lần: `sudo ./deploy/backup.sh`.

> **Bản sao lưu nằm trên chính VPS thì mất VPS là mất luôn bản sao lưu.** Nên cài `rclone` (`sudo apt install rclone && rclone config`), trỏ tới Cloudflare R2 hoặc Google Drive, rồi điền `RCLONE_REMOTE=r2:tayzen-backup` trong `.env`.

### Khôi phục

```bash
# 1. Dừng API để không còn kết nối ghi
docker compose stop api cloudflared
# 2. Giải nén bản cần khôi phục ngay trong container db
docker compose exec db sh -c 'gunzip -k /var/opt/mssql/backup/TAYZEN_20261101_023000.bak.gz'
# 3. Khôi phục (ghi đè CSDL hiện tại)
docker compose exec -T db sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -b' <<'SQL'
ALTER DATABASE [TAYZEN] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [TAYZEN] FROM DISK = N'/var/opt/mssql/backup/TAYZEN_20261101_023000.bak' WITH REPLACE, CHECKSUM;
ALTER DATABASE [TAYZEN] SET MULTI_USER;
GO
SQL
# 4. Login tayzen_app có thể bị "mồ côi" sau khi khôi phục: chạy lại migrate.sh để gắn lại quyền
./deploy/migrate.sh
# 5. Khôi phục ảnh (nếu cần)
docker run --rm -v tayzen_api-uploads:/data -v "$PWD/backups/uploads:/backup" alpine:3.20 \
  sh -c 'rm -rf /data/* && tar xzf /backup/uploads_20261101_023000.tar.gz -C /data && chown -R 1654:1654 /data'
docker compose up -d
```

Nên tập khôi phục thử mỗi tháng một lần trên một VPS phụ.

## 10. Xem log

```bash
docker compose ps
docker compose logs -f api              # log console (Serilog)
docker compose logs --since 1h db
docker compose logs -f cloudflared      # lỗi kết nối tunnel
# File log Serilog (giữ 30 ngày, lỗi giữ 90 ngày) nằm trong volume api-logs:
docker compose exec api ls logs logs/errors
docker compose exec api tail -n 200 logs/errors/fixy-error-$(date +%Y%m%d).log
```

Log của Docker được xoay vòng với giới hạn 10 MB × 5 file cho mỗi service, nên không làm đầy ổ đĩa.

## 11. Cập nhật phiên bản mới

```bash
cd /opt/tayzen/TAYZEN-API
sudo ./deploy/backup.sh                         # 1. luôn sao lưu trước
git fetch && git checkout <nhánh-hoặc-tag> && git pull
export TAYZEN_TAG=$(git rev-parse --short HEAD)
docker compose build api                        # 2. dựng image mới, API cũ vẫn chạy
./deploy/migrate.sh                             # 3. áp dụng migration mới (nếu có)
docker compose up -d api                        # 4. thay container, gián đoạn vài giây
docker compose ps && curl -fsS https://api.tayzen.vn/health
docker image prune -f                           # dọn image cũ
```

Quay lại bản cũ: `TAYZEN_TAG=<tag-cũ> docker compose up -d api`. Image cũ vẫn còn nếu chưa chạy prune. Lưu ý: migration **không tự quay ngược**. Nếu bản mới đã đổi cấu trúc CSDL thì phải khôi phục từ bản sao lưu ở bước 1.

## 12. Chuyển dữ liệu từ Azure sang

1. **CSDL**: trên máy có `sqlpackage`, xuất Azure SQL ra file `.bacpac`:
   `sqlpackage /a:Export /ssn:<server>.database.windows.net /sdn:<db> /su:<user> /sp:<pass> /tf:tayzen.bacpac`.
   Sau đó nạp vào SQL Server trên VPS bằng `sqlpackage /a:Import`, trỏ tới `localhost,1433` (tạm mở `ports: ["127.0.0.1:1433:1433"]` cho db rồi đi qua SSH tunnel). Hoặc dùng Azure Data Studio, mục Import bacpac. Xong thì chạy `./deploy/migrate.sh` để tạo login ứng dụng.
2. **Ảnh**: tải toàn bộ container Azure Blob bằng `azcopy copy "https://<account>.blob.core.windows.net/<container>?<SAS>" ./blob --recursive`, chép vào volume `tayzen_api-uploads`, rồi đổi URL trong CSDL:
   ```sql
   UPDATE Media SET FileUrl = REPLACE(FileUrl, 'https://<account>.blob.core.windows.net/<container>/', 'https://api.tayzen.vn/media/');
   UPDATE Users SET AvatarUrl = REPLACE(AvatarUrl, 'https://<account>.blob.core.windows.net/<container>/', 'https://api.tayzen.vn/media/'),
                    FaceImageUrl = REPLACE(FaceImageUrl, 'https://<account>.blob.core.windows.net/<container>/', 'https://api.tayzen.vn/media/');
   -- làm tương tự cho các cột URL ảnh khác: ServiceCategories.ImageUrl, SpaServiceCategories.ImageUrl, SpaPartners.LogoUrl/CoverImageUrl, ảnh trong ChatMessages...
   ```
3. Đổi URL callback ở trang quản trị MoMo, VNPay, payOS, SePay sang `https://api.tayzen.vn/api/payment/callback/...` và `https://api.tayzen.vn/api/payment/webhook/sepay`. Đổi địa chỉ API trong app di động và web.
4. Cho chạy song song vài ngày rồi mới tắt Azure. Nhớ **tắt workflow deploy Azure** (hoặc xoá secret) để không còn hai nơi cùng ghi dữ liệu.

## 13. Lưu ý bảo mật đã biết (cần đội dev xử lý, không nằm trong phạm vi Docker)

- Swagger đang bật cho cả production (`app.UseSwagger()` không có điều kiện), nên ai cũng xem được danh sách API.
- `POST /api/worker-profiles/verify-face` cho phép gọi không cần đăng nhập và chạy ONNX tốn CPU. Nên giới hạn tần suất (WAF Cloudflare hoặc rate limiter).
- `GET /api/media/{id}`: bất kỳ ai đã đăng nhập đều lấy được URL của mọi ảnh, kể cả ảnh CCCD của người khác.
- Nếu để trống `SePay__WebhookToken` thì webhook SePay sẽ không xác thực.
- Bản quyền QuestPDF: `LicenseType.Community` chỉ miễn phí cho doanh nghiệp có doanh thu dưới 1 triệu USD/năm.
