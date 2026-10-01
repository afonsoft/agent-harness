#!/usr/bin/env bash
set -euo pipefail

PAYLOAD_DIR="$HOME/.taskctl"
BIN_DIR="/usr/local/bin"
WRAPPER="$BIN_DIR/taskctl"
BASHRC="$HOME/.bashrc"
ALIAS_MARK="# taskctl-ai"

DRY_RUN=false

log() {
    echo "[install-cli] $*"
}

check_dotnet() {
    if ! command -v dotnet >/dev/null 2>&1; then
        echo "ERROR: .NET SDK not found. Install .NET 10 SDK first." >&2
        return 1
    fi

    local version
    version=$(dotnet --version 2>/dev/null | head -n1)
    if [[ ! $version == 10.* ]]; then
        echo "ERROR: .NET SDK $version found, but .NET 10 is required." >&2
        return 1
    fi

    log "Found .NET SDK $version"
}

get_rid() {
    local os arch
    os=$(uname -s)
    arch=$(uname -m)

    case "$os" in
        Linux)
            case "$arch" in
                x86_64) echo "linux-x64" ;;
                aarch64|arm64) echo "linux-arm64" ;;
                *) echo "ERROR: unsupported architecture on Linux: $arch" >&2; return 1 ;;
            esac
            ;;
        Darwin)
            case "$arch" in
                x86_64) echo "osx-x64" ;;
                arm64) echo "osx-arm64" ;;
                *) echo "ERROR: unsupported architecture on macOS: $arch" >&2; return 1 ;;
            esac
            ;;
        *)
            echo "ERROR: unsupported OS: $os" >&2
            return 1
            ;;
    esac
}

maybe_sudo() {
    if [[ -d "$BIN_DIR" && -w "$BIN_DIR" ]]; then
        "$@"
    else
        sudo "$@"
    fi
}

publish_cli() {
    local rid
    rid=$(get_rid)

    if [[ "$DRY_RUN" == true ]]; then
        log "[DRY-RUN] dotnet publish src/Taskboard.Cli -c Release -r $rid --self-contained false -o $PAYLOAD_DIR /p:PublishSingleFile=false"
        return
    fi

    rm -rf "$PAYLOAD_DIR"
    dotnet publish "src/Taskboard.Cli" \
        -c Release \
        -r "$rid" \
        --self-contained false \
        -o "$PAYLOAD_DIR" \
        /p:PublishSingleFile=false
}

install_wrapper() {
    local payload="$PAYLOAD_DIR/taskctl"
    if [[ "$DRY_RUN" == true ]]; then
        log "[DRY-RUN] create wrapper $WRAPPER -> $payload"
        return
    fi

    if [[ ! -f "$payload" ]]; then
        echo "ERROR: apphost not found at $payload. Publish may have failed." >&2
        return 1
    fi

    if [[ -e "$WRAPPER" && ! -L "$WRAPPER" && ! -s "$WRAPPER" ]]; then
        echo "ERROR: $WRAPPER exists and is not a regular wrapper. Refuse to overwrite." >&2
        return 1
    fi

    maybe_sudo mkdir -p "$BIN_DIR"
    maybe_sudo tee "$WRAPPER" >/dev/null <<EOF
#!/usr/bin/env bash
set -euo pipefail
exec "$payload" "\$@"
EOF
    maybe_sudo chmod +x "$WRAPPER"
    log "Installed wrapper $WRAPPER"
}

add_alias() {
    local alias_line
    alias_line="alias taskctl=\"/usr/local/bin/taskctl\" $ALIAS_MARK"

    if [[ "$DRY_RUN" == true ]]; then
        log "[DRY-RUN] ensure alias in $BASHRC"
        return
    fi

    if ! [[ -f "$BASHRC" ]]; then
        touch "$BASHRC"
    fi

    if grep -qF "$ALIAS_MARK" "$BASHRC"; then
        log "Alias already marked in $BASHRC"
        return
    fi

    echo "$alias_line" >> "$BASHRC"
    log "Alias added to $BASHRC"
}

main() {
    while [[ $# -gt 0 ]]; do
        local arg="$1"
        case "$arg" in
            --dry-run)
                DRY_RUN=true
                shift
                ;;
            -h|--help)
                echo "Usage: $0 [--dry-run]"
                exit 0
                ;;
            *)
                echo "Unknown option: $arg" >&2
                exit 1
                ;;
        esac
    done

    if [[ "$DRY_RUN" == true ]]; then
        log "Running in dry-run mode"
    fi

    check_dotnet
    publish_cli
    install_wrapper
    add_alias

    log "Done. Run 'source $BASHRC' or open a new shell to use 'taskctl'."
}

main "$@"
