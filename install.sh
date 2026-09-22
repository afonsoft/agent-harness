#!/usr/bin/env bash
set -euo pipefail

# Instala o agent-harness: clona (se necessario), compila, instala o CLI taskctl,
# copia a skill manage-taskboard para os diretorios do agente e cria wrappers
# para executar o servidor, o MCP e o frontend.

DEFAULT_REPO="https://github.com/afonsoft/agent-harness.git"
# SPEC-20260922-harness-home-rename: HARNESS_* canonical; TASKBOARD_* legacy fallback.
REPO_URL="${HARNESS_REPO:-${TASKBOARD_REPO:-$DEFAULT_REPO}}"
HARNESS_HOME="${HARNESS_HOME:-${TASKBOARD_HOME:-$HOME/.agent-harness}}"
[ -n "${HARNESS_DIR:-${TASKBOARD_DIR:-}}" ] && HARNESS_HOME="${HARNESS_DIR:-$TASKBOARD_DIR}"
BIN_DIR="$HARNESS_HOME/bin"
REPO_DIR="$HARNESS_HOME/agent-harness"
NUGET_DIR="$REPO_DIR/artifacts/nuget"

DRY_RUN=false
MIGRATE=false
INSTALL_ALL=false
INSTALL_DEVIN=false
INSTALL_CLAUDE=false
INSTALL_CURSOR=false
INSTALL_OPENCODE=false
INSTALL_GEMINI=false
INSTALL_VSCODE=false

usage() {
    cat <<'EOF'
Uso: $0 [opcoes]

Opcoes:
  --all        Instalar a skill para todos os IDEs/CLIs suportados (padrao)
  --devin      Instalar a skill para Devin
  --claude     Instalar a skill para Claude Code
  --cursor     Instalar a skill para Cursor
  --opencode   Instalar a skill para OpenCode
  --gemini     Instalar a skill para Gemini CLI
  --vscode     Instalar a skill para VS Code / Copilot
  --migrate    Migrar instalacao legada ~/.taskboard → ~/.agent-harness
  --dry-run    Simular sem alterar arquivos
  --help       Exibir esta ajuda

Variaveis de ambiente (legadas TASKBOARD_* ainda aceitas):
  HARNESS_REPO    URL do repositorio (padrao: DEFAULT_REPO)
  HARNESS_HOME    Diretorio base da instalacao (padrao: $HOME/.agent-harness)
EOF
}

if [ $# -eq 0 ]; then
    INSTALL_ALL=true
fi

while [ $# -gt 0 ]; do
    case "$1" in
        --all) INSTALL_ALL=true ;;
        --devin) INSTALL_DEVIN=true ;;
        --claude) INSTALL_CLAUDE=true ;;
        --cursor) INSTALL_CURSOR=true ;;
        --opencode) INSTALL_OPENCODE=true ;;
        --gemini) INSTALL_GEMINI=true ;;
        --vscode) INSTALL_VSCODE=true ;;
        --migrate) MIGRATE=true ;;
        --dry-run) DRY_RUN=true ;;
        --help) usage; exit 0 ;;
        *) echo "Opcao desconhecida: $1" >&2; usage >&2; exit 1 ;;
    esac
    shift
done

if [ "$INSTALL_ALL" = true ]; then
    INSTALL_DEVIN=true
    INSTALL_CLAUDE=true
    INSTALL_CURSOR=true
    INSTALL_OPENCODE=true
    INSTALL_GEMINI=true
    INSTALL_VSCODE=true
fi

run() {
    if [ "$DRY_RUN" = true ]; then
        echo "[dry-run] $*" >&2
    else
        "$@"
    fi
}

write_file() {
    local file=$1
    if [ "$DRY_RUN" = true ]; then
        echo "[dry-run] escrever $file:" >&2
        cat
        return 0
    fi
    mkdir -p "$(dirname "$file")"
    cat > "$file"
}

check_dotnet() {
    if ! command -v dotnet &> /dev/null; then
        echo "Erro: .NET SDK nao encontrado. Instale o .NET 10 SDK." >&2
        exit 1
    fi

    local sdk_version
    sdk_version=$(dotnet --version)
    local major
    major=$(echo "$sdk_version" | cut -d. -f1)

    if [ "$major" -ne 10 ]; then
        echo "Erro: .NET SDK $sdk_version encontrado, mas e necessario o .NET 10 SDK." >&2
        exit 1
    fi

    echo ".NET SDK $sdk_version encontrado."
}

detect_repo_dir() {
    local script_dir
    script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)

    if [ -f "$script_dir/Taskboard.sln" ]; then
        REPO_DIR="$script_dir"
        echo "Repositorio detectado em: $REPO_DIR"
        return 0
    fi

    if [ -f "$PWD/Taskboard.sln" ]; then
        REPO_DIR="$PWD"
        echo "Repositorio detectado em: $REPO_DIR"
        return 0
    fi

    if [ ! -d "$REPO_DIR/.git" ]; then
        echo "Clonando $REPO_URL em $REPO_DIR..."
        run mkdir -p "$HARNESS_HOME"
        run git clone "$REPO_URL" "$REPO_DIR"
    else
        echo "Repositorio ja existe em $REPO_DIR."
    fi
}

build_solution() {
    echo "Compilando a solucao..."
    run dotnet build "$REPO_DIR/Taskboard.sln" -c Release

    # SPEC-20260915-wasm-post-migration-hardening: publish the server so the
    # launcher serves the WASM SPA + static web assets in Production — the
    # plain bin/Release output does not include them.
    echo "Publicando o servidor (SPA + static web assets)..."
    run dotnet publish "$REPO_DIR/src/Taskboard.Server/Taskboard.Server.csproj" -c Release -o "$HARNESS_HOME/server"
}

install_cli() {
    echo "Empacotando e instalando o CLI taskctl..."
    run mkdir -p "$NUGET_DIR"
    run dotnet pack "$REPO_DIR/src/Taskboard.Cli/Taskboard.Cli.csproj" -c Release -o "$NUGET_DIR" --no-build

    run mkdir -p "$BIN_DIR"

    if [ -f "$BIN_DIR/taskctl" ]; then
        run dotnet tool update taskctl --tool-path "$BIN_DIR" --add-source "$NUGET_DIR"
    else
        run dotnet tool install taskctl --tool-path "$BIN_DIR" --add-source "$NUGET_DIR"
    fi

    run chmod +x "$BIN_DIR/taskctl"
}

install_skill_for_ide() {
    local ide=$1
    local target_dir=$2

    if [ ! -d "$target_dir" ] && [ "$DRY_RUN" = false ]; then
        return 0
    fi

    echo "Instalando skill para $ide em $target_dir..."
    run mkdir -p "$target_dir"

    if [ -d "$target_dir/manage-taskboard" ]; then
        run rm -rf "$target_dir/manage-taskboard"
    fi

    run cp -R "$REPO_DIR/.claude/skills/manage-taskboard" "$target_dir/"
}

install_skills() {
    echo "Instalando a skill manage-taskboard..."

    if [ "$INSTALL_DEVIN" = true ]; then
        install_skill_for_ide "Devin" "$HOME/.devin/skills"
        install_skill_for_ide "Devin (config)" "$HOME/.config/devin/skills"
        install_skill_for_ide "Devin (cognition)" "$HOME/.cognition/skills"
    fi

    if [ "$INSTALL_CLAUDE" = true ]; then
        install_skill_for_ide "Claude Code" "$HOME/.claude/skills"
    fi

    if [ "$INSTALL_CURSOR" = true ]; then
        install_skill_for_ide "Cursor" "$HOME/.cursor/skills"
    fi

    if [ "$INSTALL_OPENCODE" = true ]; then
        install_skill_for_ide "OpenCode" "$HOME/.opencode/skills"
        install_skill_for_ide "OpenCode (config)" "$HOME/.config/opencode/skills"
    fi

    if [ "$INSTALL_GEMINI" = true ]; then
        install_skill_for_ide "Gemini CLI" "$HOME/.gemini/skills"
        install_skill_for_ide "Gemini (antigravity)" "$HOME/.gemini/antigravity-cli/skills"
    fi

    if [ "$INSTALL_VSCODE" = true ]; then
        install_skill_for_ide "VS Code / Copilot" "$HOME/.github/skills"
    fi
}

setup_config() {
    echo "Configurando taskctl..."
    write_file "$HOME/.config/taskctl/settings.json" <<'EOF'
{
  "baseUrl": "http://127.0.0.1:47823",
  "currentProject": null,
  "currentWorkspace": null,
  "cloudUrl": null
}
EOF
}

generate_admin_password() {
    local password_file="$HARNESS_HOME/admin-password"
    local password

    if [ -f "$password_file" ]; then
        password=$(cat "$password_file")
    else
        password=$(openssl rand -hex 16 2>/dev/null || dd if=/dev/urandom bs=32 count=1 2>/dev/null | od -An -tx1 | tr -d ' \n')
        if [ -z "$password" ]; then
            password="$(date +%s%N | sha256sum | head -c 32)"
        fi
        run mkdir -p "$HARNESS_HOME"
        run sh -c "printf '%s' \"$password\" > '$password_file'"
        run chmod 600 "$password_file"
    fi

    echo "$password"
}

create_env_file() {
    echo "Gerando arquivo de ambiente..."
    local env_file="$HARNESS_HOME/env"
    local data_dir="$HARNESS_HOME/data"
    local password
    password=$(generate_admin_password)

    run mkdir -p "$data_dir"

    write_file "$env_file" <<EOF
# Ambiente gerado por install.sh do agent-harness
export PATH="$BIN_DIR:\$PATH"
export HARNESS_DATA_DIR="$data_dir"
export Taskboard__DataDir="$data_dir"
export HARNESS_ADMIN_USERNAME="admin"
export HARNESS_ADMIN_PASSWORD="$password"
export HARNESS_URL="http://127.0.0.1:47823"
EOF

    run chmod 600 "$env_file"
}

create_wrappers() {
    echo "Criando wrappers em $BIN_DIR..."
    run mkdir -p "$BIN_DIR"

    local server_dll
    server_dll="$HARNESS_HOME/server/Taskboard.Server.dll"
    local mcp_dll
    mcp_dll="$REPO_DIR/src/Taskboard.Mcp/bin/Release/net10.0/Taskboard.Mcp.dll"
    local server_content_root
    server_content_root="$HARNESS_HOME/server"

    write_file "$BIN_DIR/harness-server" <<EOF
#!/usr/bin/env bash
set -euo pipefail
HARNESS_DIR_RESOLVED="\${HARNESS_HOME:-\${TASKBOARD_HOME:-\$HOME/.agent-harness}}"
ENV_FILE="\$HARNESS_DIR_RESOLVED/env"
if [ ! -f "\$ENV_FILE" ] && [ -f "\$HOME/.taskboard/env" ]; then
    ENV_FILE="\$HOME/.taskboard/env"
fi
if [ -f "\$ENV_FILE" ]; then
    # shellcheck source=/dev/null
    source "\$ENV_FILE"
fi
export ASPNETCORE_URLS="\${ASPNETCORE_URLS:-http://127.0.0.1:47823}"
export ASPNETCORE_CONTENTROOT="\${ASPNETCORE_CONTENTROOT:-$server_content_root}"
exec dotnet exec "$server_dll"
EOF

    run chmod +x "$BIN_DIR/harness-server"

    write_file "$BIN_DIR/harness-mcp" <<EOF
#!/usr/bin/env bash
set -euo pipefail
HARNESS_DIR_RESOLVED="\${HARNESS_HOME:-\${TASKBOARD_HOME:-\$HOME/.agent-harness}}"
ENV_FILE="\$HARNESS_DIR_RESOLVED/env"
if [ ! -f "\$ENV_FILE" ] && [ -f "\$HOME/.taskboard/env" ]; then
    ENV_FILE="\$HOME/.taskboard/env"
fi
if [ -f "\$ENV_FILE" ]; then
    # shellcheck source=/dev/null
    source "\$ENV_FILE"
fi
exec dotnet exec "$mcp_dll"
EOF

    run chmod +x "$BIN_DIR/harness-mcp"
}

create_systemd_service() {
    if ! command -v systemctl &> /dev/null; then
        echo "systemctl nao encontrado; pulando criacao do servico."
        return 0
    fi

    local unit_dir="$HOME/.config/systemd/user"
    local unit_file="$unit_dir/harness-server.service"

    echo "Criando servico systemd para o servidor harness..."

    write_file "$unit_file" <<EOF
[Unit]
Description=Harness Web Server (agent-harness)
After=network.target

[Service]
Type=simple
ExecStart=$BIN_DIR/harness-server
Restart=on-failure
RestartSec=5s
Environment="HARNESS_HOME=$HARNESS_HOME"
Environment="HOME=$HOME"

[Install]
WantedBy=default.target
EOF

    if [ "$DRY_RUN" = false ]; then
        run systemctl --user daemon-reload || true
        run systemctl --user enable harness-server || true
        run systemctl --user start harness-server || true
    fi
}

add_path_to_shell() {
    local shell_file=$1
    if [ ! -f "$shell_file" ]; then
        return 0
    fi

    local path_line
    path_line="export PATH=\"$BIN_DIR:\$PATH\" # agent-harness"

    if grep -qF "$path_line" "$shell_file" 2>/dev/null; then
        return 0
    fi

    echo "Adicionando $BIN_DIR ao PATH em $shell_file..."
    run sh -c "echo '$path_line' >> '$shell_file'"
}

print_summary() {
    local password_file="$HARNESS_HOME/admin-password"
    local env_file="$HARNESS_HOME/env"

    cat <<EOF

====================================
Instalacao concluida!
====================================

Repositorio:      $REPO_DIR
Binarios:         $BIN_DIR
Dados:            $HARNESS_HOME/data
Senha admin:      $password_file
Configuracao:     ~/.config/taskctl/settings.json

Comandos disponiveis:
  taskctl --help
  harness-server
  harness-mcp

Servico systemd:
  systemctl --user status harness-server

Para ativar o PATH neste shell, execute:
  source $env_file

Para iniciar o servidor (frontend estara em http://127.0.0.1:47823):
  source $env_file
  harness-server

Para executar o servidor MCP:
  source $env_file
  harness-mcp

EOF
}

migrate_legacy() {
    local legacy_home="$HOME/.taskboard"
    local new_home="$HARNESS_HOME"

    if [ ! -d "$legacy_home" ]; then
        echo "Nada a migrar: $legacy_home nao existe."
        return 0
    fi
    if [ -e "$new_home" ]; then
        echo "Erro: $new_home ja existe — remova ou escolha outro HARNESS_HOME." >&2
        exit 1
    fi

    echo "Migrando $legacy_home → $new_home..."

    if command -v systemctl &> /dev/null; then
        run systemctl --user stop taskboard-server || true
        run systemctl --user disable taskboard-server || true
    fi

    run mv "$legacy_home" "$new_home"

    # Rename the SQLite database (server startup would also do this).
    if [ -f "$new_home/data/taskboard.sqlite" ] && [ ! -f "$new_home/data/harness.sqlite" ]; then
        run mv "$new_home/data/taskboard.sqlite" "$new_home/data/harness.sqlite"
        for ext in -wal -shm; do
            if [ -f "$new_home/data/taskboard.sqlite$ext" ]; then
                run mv "$new_home/data/taskboard.sqlite$ext" "$new_home/data/harness.sqlite$ext"
            fi
        done
    fi

    # Rewrite the generated env file to the canonical HARNESS_* names.
    if [ -f "$new_home/env" ]; then
        run sed -i -e 's/^export TASKBOARD_/export HARNESS_/' "$new_home/env"
    fi

    # Drop legacy wrappers (new ones are written by create_wrappers).
    run rm -f "$new_home/bin/taskboard-server" "$new_home/bin/taskboard-mcp"

    # Replace the legacy systemd unit.
    local unit_dir="$HOME/.config/systemd/user"
    if [ -f "$unit_dir/taskboard-server.service" ]; then
        run rm -f "$unit_dir/taskboard-server.service"
    fi
    create_wrappers
    create_systemd_service

    if command -v systemctl &> /dev/null && [ "$DRY_RUN" = false ]; then
        run systemctl --user daemon-reload || true
        echo "Verificando saude do servico..."
        sleep 5
        if curl -fsS http://127.0.0.1:47823/health > /dev/null 2>&1; then
            echo "Migracao concluida — servico saudavel em http://127.0.0.1:47823"
        else
            echo "Aviso: /health nao respondeu — verifique 'systemctl --user status harness-server'." >&2
        fi
    fi
}

main() {
    if [ "$MIGRATE" = true ]; then
        migrate_legacy
        return 0
    fi

    check_dotnet
    detect_repo_dir
    build_solution
    install_cli
    install_skills
    setup_config
    create_env_file
    create_wrappers
    create_systemd_service
    add_path_to_shell "$HOME/.bashrc"
    if [ -f "$HOME/.zshrc" ]; then
        add_path_to_shell "$HOME/.zshrc"
    fi
    print_summary
}

main "$@"
