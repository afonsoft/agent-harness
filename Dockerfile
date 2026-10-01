# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish src/Taskboard.Server/Taskboard.Server.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# git: skills repository sync (SPEC-20260915-skills-repo-sync)
# curl/ca-certificates: CLI installers; bsdutils: `script` PTY helper (SPEC-20260917-cli-agents-terminal)
RUN apt-get update && apt-get install -y --no-install-recommends \
    git curl ca-certificates bsdutils xz-utils \
    && rm -rf /var/lib/apt/lists/*

# Node.js LTS — required by `npx skills add` and the npm-based agent CLIs
ARG NODE_VERSION=24.21.0
RUN arch="$(dpkg --print-architecture)" \
    && case "$arch" in \
        amd64) node_arch=x64 ;; \
        arm64) node_arch=arm64 ;; \
        *) echo "unsupported arch: $arch"; exit 1 ;; \
    esac \
    && node_url="https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-${node_arch}.tar.xz" \
    && curl -fsSL "$node_url" -o /tmp/node.tar.xz \
    && tar -xJf /tmp/node.tar.xz -C /usr/local --strip-components=1 \
    && rm /tmp/node.tar.xz \
    && node --version && npm --version \
# Agent CLIs (SPEC-20260917-cli-agents-terminal).
# npm-based CLIs install globally; devin/agy use their official installers.
# Binaries must live outside /data (the mounted volume) — devin installs under
# a build-time HOME and is linked into /usr/local/bin.
    && npm install -g --allow-scripts=@anthropic-ai/claude-code,opencode-ai \
        @anthropic-ai/claude-code @openai/codex opencode-ai \
    && npm cache clean --force \
    && command -v claude && command -v codex && command -v opencode \
# The devin installer ends with an interactive `devin setup` (login) which fails
# without a TTY — the binary is already in place by then, so tolerate the
# non-zero exit and verify the executable instead.
    && mkdir -p /opt/cli-home \
    && (HOME=/opt/cli-home bash -c "curl -fsSL https://cli.devin.ai/install.sh | bash" || true) \
    && test -x /opt/cli-home/.local/bin/devin \
    && ln -sf /opt/cli-home/.local/bin/devin /usr/local/bin/devin \
    && curl -fsSL https://antigravity.google/cli/install.sh | bash -s -- --dir /usr/local/bin \
    && test -x /usr/local/bin/agy

# CLI state (credentials, caches) lives under HOME — persisted via the /data volume.
ENV HOME=/data/home
# appsettings.Production.json sets Taskboard:DataDir=/var/agent-harness/data —
# outside the volume. HARNESS_DATA_DIR (read directly by TaskboardEnvironment)
# overrides it so the SQLite DB, admin.json, overrides and skills-cache persist.
ENV HARNESS_DATA_DIR=/data
# Bind-mounted host repos have a different owner — git refuses to operate without
# this. Safe in a single-tenant self-hosted container.
RUN git config --system --add safe.directory '*' \
    && mkdir -p /data/home/repos

VOLUME /data

# Non-root runtime (S6471): the server, its SQLite DB and agent CLI state all
# live under /data; /app only holds read-only binaries. Named volumes inherit
# this ownership on first creation — for EXISTING root-owned volumes run once:
#   docker run --rm -u root -v <volume>:/data alpine chown -R 1000:1000 /data
RUN groupadd --system --gid 1000 harness \
    && useradd --system --uid 1000 --gid harness --home-dir /data/home harness \
    && mkdir -p /data/home \
    && chown -R harness:harness /data /app

COPY --from=build --chown=harness:harness /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:47823
EXPOSE 47823

USER harness

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:47823/health || exit 1

ENTRYPOINT ["dotnet", "Taskboard.Server.dll"]
