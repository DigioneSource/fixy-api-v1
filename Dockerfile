# syntax=docker/dockerfile:1
# =============================================================================
# TAYZEN API (.NET 8) — image tự host
#   docker build -t tayzen-api .                    -> image chạy API (target "runtime")
#   docker build --target migrator -t tayzen-migrator .  -> image chạy migration EF Core
# =============================================================================

# ---------- 1. Build & publish ----------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /src

# Chép csproj trước để cache bước restore
COPY FIXY.sln ./
COPY API/API.csproj API/
COPY Application/Application.csproj Application/
COPY Infrastructure/Infrastructure.csproj Infrastructure/
COPY Domain/Domain.csproj Domain/
RUN RID=linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 ) \
 && dotnet restore API/API.csproj -r $RID

COPY . .
RUN RID=linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 ) \
 && dotnet publish API/API.csproj -c Release -r $RID --self-contained false --no-restore -o /app/publish \
 # Model ONNX (eKYC khuôn mặt) và template email Razor bắt buộc phải có trong output
 && test -f /app/publish/AI/Models/version-RFB-320.onnx \
 && test -f /app/publish/AI/Models/arcface_mobilefacenet.onnx \
 && test -f /app/publish/Services/Email/Templates/RegisterOtpTemplate.cshtml \
 && test -f /app/publish/Services/Email/Templates/ForgotPasswordOtpTemplate.cshtml

# ---------- 2. Gói migration EF Core (efbundle) ----------
FROM build AS bundle
ARG TARGETARCH
RUN dotnet tool install --global dotnet-ef --version 8.0.*
ENV PATH="$PATH:/root/.dotnet/tools"
RUN RID=linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 ) \
 && dotnet ef migrations bundle \
      --project Infrastructure/Infrastructure.csproj \
      --startup-project API/API.csproj \
      --configuration Release \
      --target-runtime $RID \
      --output /app/efbundle --force

# ---------- 3. Nền chạy chung ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
# fontconfig + fonts-dejavu-core: QuestPDF (xuất PDF báo cáo) cần font hệ thống có dấu tiếng Việt
# tzdata: múi giờ Asia/Ho_Chi_Minh; curl: HEALTHCHECK
# ONNX Runtime và ImageSharp không cần thêm thư viện native (libonnxruntime.so đi kèm gói NuGet)
RUN apt-get update \
 && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core tzdata curl \
 && rm -rf /var/lib/apt/lists/* \
 && fc-cache -f

# ---------- 4. Image migration ----------
FROM base AS migrator
WORKDIR /migrator
COPY --from=bundle /app/efbundle ./efbundle
USER $APP_UID
# Chuỗi kết nối truyền qua biến ConnectionStrings__DefaultConnection hoặc --connection
ENTRYPOINT ["./efbundle"]

# ---------- 5. Image chạy API (mặc định) ----------
FROM base AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Storage__Local__RootPath=/app/data/uploads
COPY --from=build /app/publish ./
# Thư mục ghi được cho user không phải root: log Serilog (logs/) và ảnh tải lên (data/uploads)
RUN mkdir -p /app/logs/errors /app/data/uploads \
 && chown -R $APP_UID:$APP_UID /app/logs /app/data
# User "app" (UID 1654) có sẵn trong image aspnet:8.0
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "API.dll"]
