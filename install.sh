#!/usr/bin/env bash
set -euo pipefail

# Instala o taskboard-ai: clona (se necessario), compila, instala o CLI taskctl,
# copia a skill manage-taskboard para os diretorios do agente e cria wrappers
# para executar o servidor, o MCP e o frontend.

DEFAULT_REPO="https://github.com/afonsoft/taskboard-ai.git"
REPO_URL="${TASKBOARD_REPO:-$DEFAULT_REPO}"
TASKBOARD_HOME="${TASKBOARD_HOME:-$HOME/.taskboard}"
[ -n "${TASKBOARD_DIR:-}" ] && TASKBOARD_HOME="$TASKBOARD_DIR"
BIN_DIR="$TASKBOARD_HOME/bin"
REPO_DIR="$TASKBOARD_HOME/taskboard-ai"
NUGET_DIR="$REPO_DIR/artifacts/nuget"

DRY_RUN=false
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
  --dry-run    Simular sem alterar arquivos
  --help       Exibir esta ajuda

Variaveis de ambiente:
  TASKBOARD_REPO    URL do repositorio (padrao: DEFAULT_REPO)
  TASKBOARD_HOME    Diretorio base da instalacao (padrao: $HOME/.taskboard)
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
        run mkdir -p "$TASKBOARD_HOME"
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
    run dotnet publish "$REPO_DIR/src/Taskboard.Server/Taskboard.Server.csproj" -c Release -o "$TASKBOARD_HOME/server"
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
    local password_file="$TASKBOARD_HOME/admin-password"
    local password

    if [ -f "$password_file" ]; then
        password=$(cat "$password_file")
    else
        password=$(openssl rand -hex 16 2>/dev/null || dd if=/dev/urandom bs=32 count=1 2>/dev/null | od -An -tx1 | tr -d ' \n')
        if [ -z "$password" ]; then
            password="$(date +%s%N | sha256sum | head -c 32)"
        fi
        run mkdir -p "$TASKBOARD_HOME"
        run sh -c "printf '%s' \"$password\" > '$password_file'"
        run chmod 600 "$password_file"
    fi

    echo "$password"
}

create_env_file() {
    echo "Gerando arquivo de ambiente..."
    local env_file="$TASKBOARD_HOME/env"
    local data_dir="$TASKBOARD_HOME/data"
    local password
    password=$(generate_admin_password)

    run mkdir -p "$data_dir"

    write_file "$env_file" <<EOF
# Ambiente gerado por install.sh do taskboard-ai
export PATH="$BIN_DIR:\$PATH"
export TASKBOARD_DATA_DIR="$data_dir"
export Taskboard__DataDir="$data_dir"
export TASKBOARD_ADMIN_USERNAME="admin"
export TASKBOARD_ADMIN_PASSWORD="$password"
export TASKBOARD_URL="http://127.0.0.1:47823"
EOF

    run chmod 600 "$env_file"
}

create_wrappers() {
    echo "Criando wrappers em $BIN_DIR..."
    run mkdir -p "$BIN_DIR"

    local server_dll
    server_dll="$TASKBOARD_HOME/server/Taskboard.Server.dll"
    local mcp_dll
    mcp_dll="$REPO_DIR/src/Taskboard.Mcp/bin/Release/net10.0/Taskboard.Mcp.dll"
    local server_content_root
    server_content_root="$TASKBOARD_HOME/server"

    write_file "$BIN_DIR/taskboard-server" <<EOF
#!/usr/bin/env bash
set -euo pipefail
ENV_FILE="\${TASKBOARD_HOME:-\$HOME/.taskboard}/env"
if [ -f "\$ENV_FILE" ]; then
    # shellcheck source=/dev/null
    source "\$ENV_FILE"
fi
export ASPNETCORE_URLS="\${ASPNETCORE_URLS:-http://127.0.0.1:47823}"
export ASPNETCORE_CONTENTROOT="\${ASPNETCORE_CONTENTROOT:-$server_content_root}"
exec dotnet exec "$server_dll"
EOF

    run chmod +x "$BIN_DIR/taskboard-server"

    write_file "$BIN_DIR/taskboard-mcp" <<EOF
#!/usr/bin/env bash
set -euo pipefail
ENV_FILE="\${TASKBOARD_HOME:-\$HOME/.taskboard}/env"
if [ -f "\$ENV_FILE" ]; then
    # shellcheck source=/dev/null
    source "\$ENV_FILE"
fi
exec dotnet exec "$mcp_dll"
EOF

    run chmod +x "$BIN_DIR/taskboard-mcp"
}

create_systemd_service() {
    if ! command -v systemctl &> /dev/null; then
        echo "systemctl nao encontrado; pulando criacao do servico."
        return 0
    fi

    local unit_dir="$HOME/.config/systemd/user"
    local unit_file="$unit_dir/taskboard-server.service"

    echo "Criando servico systemd para o servidor taskboard..."

    write_file "$unit_file" <<EOF
[Unit]
Description=Taskboard AI Web Server
After=network.target

[Service]
Type=simple
ExecStart=$BIN_DIR/taskboard-server
Restart=on-failure
RestartSec=5s
Environment="TASKBOARD_HOME=$TASKBOARD_HOME"
Environment="HOME=$HOME"

[Install]
WantedBy=default.target
EOF

    if [ "$DRY_RUN" = false ]; then
        run systemctl --user daemon-reload || true
        run systemctl --user enable taskboard-server || true
        run systemctl --user start taskboard-server || true
    fi
}

add_path_to_shell() {
    local shell_file=$1
    if [ ! -f "$shell_file" ]; then
        return 0
    fi

    local path_line
    path_line="export PATH=\"$BIN_DIR:\$PATH\" # taskboard-ai"

    if grep -qF "$path_line" "$shell_file" 2>/dev/null; then
        return 0
    fi

    echo "Adicionando $BIN_DIR ao PATH em $shell_file..."
    run sh -c "echo '$path_line' >> '$shell_file'"
}

print_summary() {
    local password_file="$TASKBOARD_HOME/admin-password"
    local env_file="$TASKBOARD_HOME/env"

    cat <<EOF

====================================
Instalacao concluida!
====================================

Repositorio:      $REPO_DIR
Binarios:         $BIN_DIR
Dados:            $TASKBOARD_HOME/data
Senha admin:      $password_file
Configuracao:     ~/.config/taskctl/settings.json

Comandos disponiveis:
  taskctl --help
  taskboard-server
  taskboard-mcp

Servico systemd:
  systemctl --user status taskboard-server

Para ativar o PATH neste shell, execute:
  source $env_file

Para iniciar o servidor (frontend estara em http://127.0.0.1:47823):
  source $env_file
  taskboard-server

Para executar o servidor MCP:
  source $env_file
  taskboard-mcp

EOF
}

main() {
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
