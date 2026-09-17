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
    && case "$arch" in amd64) node_arch=x64 ;; arm64) node_arch=arm64 ;; *) echo "unsupported arch: $arch"; exit 1 ;; esac \
    && curl -fsSL "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-${node_arch}.tar.xz" -o /tmp/node.tar.xz \
    && tar -xJf /tmp/node.tar.xz -C /usr/local --strip-components=1 \
    && rm /tmp/node.tar.xz \
    && node --version && npm --version

# Agent CLIs (SPEC-20260917-cli-agents-terminal).
# npm-based CLIs install globally; devin/agy use their official installers.
# Binaries must live outside /data (the mounted volume) — devin installs under
# a build-time HOME and is linked into /usr/local/bin.
RUN npm install -g --allow-scripts=@anthropic-ai/claude-code,opencode-ai \
        @anthropic-ai/claude-code @openai/codex opencode-ai \
    && npm cache clean --force \
    && command -v claude && command -v codex && command -v opencode
# The devin installer ends with an interactive `devin setup` (login) which fails
# without a TTY — the binary is already in place by then, so tolerate the
# non-zero exit and verify the executable instead.
RUN mkdir -p /opt/cli-home \
    && (HOME=/opt/cli-home bash -c "curl -fsSL https://cli.devin.ai/install.sh | bash" || true) \
    && test -x /opt/cli-home/.local/bin/devin \
    && ln -sf /opt/cli-home/.local/bin/devin /usr/local/bin/devin
RUN curl -fsSL https://antigravity.google/cli/install.sh | bash -s -- --dir /usr/local/bin \
    && test -x /usr/local/bin/agy

# CLI state (credentials, caches) lives under HOME — persisted via the /data volume.
ENV HOME=/data/home
RUN mkdir -p /data/home

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:47823
EXPOSE 47823

ENTRYPOINT ["dotnet", "Taskboard.Server.dll"]
