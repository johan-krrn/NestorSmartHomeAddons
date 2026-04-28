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
# dotnet-install.sh auto-detects architecture (amd64, arm64, arm)
# which makes it work on aarch64 (Raspberry Pi 4) unlike the MS apt repo
RUN apt-get update \
  && apt-get install -y --no-install-recommends \
  curl \
  ca-certificates \
  libicu72 \
  libssl3 \
  zlib1g \
  && curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
  && chmod +x /tmp/dotnet-install.sh \
  && /tmp/dotnet-install.sh --runtime aspnetcore --channel 8.0 --install-dir /usr/share/dotnet \
  && ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
  && rm /tmp/dotnet-install.sh \
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
