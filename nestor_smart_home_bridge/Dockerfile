ARG BUILD_FROM

###############################################################################
# Stage 1 — Build (always x64/arm64 auto-detected by Docker buildx)
###############################################################################
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build

ARG TARGETARCH
WORKDIR /src

# Map Docker arch names to .NET RID arch names (amd64 -> x64)
COPY src/NestorBridge/NestorBridge.csproj src/NestorBridge/
RUN DOTNET_ARCH=$([ "${TARGETARCH}" = "amd64" ] && echo "x64" || echo "${TARGETARCH}") \
  && dotnet restore src/NestorBridge/NestorBridge.csproj -r linux-${DOTNET_ARCH}

COPY src/NestorBridge/ src/NestorBridge/
RUN DOTNET_ARCH=$([ "${TARGETARCH}" = "amd64" ] && echo "x64" || echo "${TARGETARCH}") \
  && dotnet publish src/NestorBridge/NestorBridge.csproj \
  -c Release \
  -r linux-${DOTNET_ARCH} \
  --self-contained false \
  --no-restore \
  -o /app/publish

###############################################################################
# Stage 2 — Runtime on HA base (s6-overlay + bashio pre-installed)
###############################################################################
FROM ${BUILD_FROM}

# Install .NET 8 runtime on top of the HA base image
RUN apt-get update \
  && apt-get install -y --no-install-recommends \
  curl \
  ca-certificates \
  && curl -fsSL https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb \
  -o /tmp/ms-prod.deb \
  && dpkg -i /tmp/ms-prod.deb \
  && rm /tmp/ms-prod.deb \
  && apt-get update \
  && apt-get install -y --no-install-recommends aspnetcore-runtime-8.0 \
  && apt-get clean \
  && rm -rf /var/lib/apt/lists/*

# Copy the published .NET app
COPY --from=build /app/publish /app

# Copy s6 service definitions (run, finish)
COPY rootfs/ /

# Ensure scripts are executable (Windows checkouts may lose the +x bit)
RUN chmod a+x /etc/services.d/nestor-bridge/run \
  && chmod a+x /etc/services.d/nestor-bridge/finish

# NOTE: no ENTRYPOINT — the HA base image already provides /init (s6-overlay)
