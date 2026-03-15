#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════
#  JanotAi — Script d'installation macOS / Linux
#  Usage : bash install.sh
# ═══════════════════════════════════════════════════════════
set -e

INSTALL_DIR="$HOME/.local/share/janotai"
BIN_DIR="$HOME/.local/bin"
REPO_DIR="$(cd "$(dirname "$0")" && pwd)"

GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
RESET='\033[0m'

info()    { echo -e "${CYAN}[JanotAi]${RESET} $1"; }
success() { echo -e "${GREEN}✅ $1${RESET}"; }
warn()    { echo -e "${YELLOW}⚠  $1${RESET}"; }
error()   { echo -e "${RED}❌ $1${RESET}"; exit 1; }

echo ""
echo -e "${CYAN}╔══════════════════════════════════════════╗${RESET}"
echo -e "${CYAN}║         JanotAi — Installation          ║${RESET}"
echo -e "${CYAN}╚══════════════════════════════════════════╝${RESET}"
echo ""

# ─── 1. Vérifier .NET 8 ──────────────────────────────────────
info "Vérification de .NET 8..."
if ! command -v dotnet &>/dev/null; then
    warn ".NET non trouvé. Installation via Homebrew..."
    if command -v brew &>/dev/null; then
        brew install --cask dotnet
    else
        error ".NET 8 requis. Installe-le depuis https://dotnet.microsoft.com/download"
    fi
fi

DOTNET_VER=$(dotnet --version 2>/dev/null | cut -d'.' -f1)
if [[ "$DOTNET_VER" -lt 8 ]]; then
    error ".NET 8+ requis (version trouvée : $(dotnet --version)). Mets à jour via https://dotnet.microsoft.com/download"
fi
success ".NET $(dotnet --version) trouvé."

# ─── 2. Détection du runtime ─────────────────────────────────
OS="$(uname -s)"
ARCH="$(uname -m)"
if [[ "$OS" == "Darwin" ]]; then
    RUNTIME="osx-arm64"
elif [[ "$ARCH" == "aarch64" || "$ARCH" == "arm64" ]]; then
    RUNTIME="linux-arm64"
else
    RUNTIME="linux-x64"
fi
info "Runtime détecté : $RUNTIME"

# ─── 3. Build ────────────────────────────────────────────────
info "Compilation du projet..."
mkdir -p "$INSTALL_DIR"

dotnet publish "$REPO_DIR/JanotAi/JanotIA.csproj" \
    -c Release \
    -r "$RUNTIME" \
    --self-contained false \
    -o "$INSTALL_DIR/app" \
    --nologo -v quiet

# Build du serveur MCP Shell
dotnet publish "$REPO_DIR/ShellMcpServer/ShellMcpServer.csproj" \
    -c Release \
    -r "$RUNTIME" \
    --self-contained false \
    -o "$INSTALL_DIR/shell-mcp" \
    --nologo -v quiet

success "Compilation terminée."

# ─── 4. Copier appsettings.json (écrase toujours la version dev) ─
cp "$REPO_DIR/installer/appsettings.mac.json" "$INSTALL_DIR/app/appsettings.json"
success "Configuration Linux/macOS appliquée."

# ─── 4. Créer les scripts de lancement ───────────────────────
mkdir -p "$BIN_DIR"

# Script principal : janotai
cat > "$BIN_DIR/janotai" <<'SCRIPT'
#!/usr/bin/env bash
exec "$HOME/.local/share/janotai/app/janotai" "$@"
SCRIPT
chmod +x "$BIN_DIR/janotai"

# Script MCP Shell : janotai-shell-mcp
cat > "$BIN_DIR/janotai-shell-mcp" <<'SCRIPT'
#!/usr/bin/env bash
exec "$HOME/.local/share/janotai/shell-mcp/ShellMcpServer" "$@"
SCRIPT
chmod +x "$BIN_DIR/janotai-shell-mcp"

success "Commandes créées dans $BIN_DIR"

# ─── 5. Ajouter ~/.local/bin au PATH si nécessaire ───────────
SHELL_RC=""
if [[ "$SHELL" == *"zsh"* ]]; then
    SHELL_RC="$HOME/.zshrc"
elif [[ "$SHELL" == *"bash"* ]]; then
    SHELL_RC="$HOME/.bashrc"
fi

if [[ -n "$SHELL_RC" ]] && ! grep -q '\.local/bin' "$SHELL_RC" 2>/dev/null; then
    echo '' >> "$SHELL_RC"
    echo '# JanotAi' >> "$SHELL_RC"
    echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$SHELL_RC"
    warn "PATH mis à jour dans $SHELL_RC — relance ton terminal ou : source $SHELL_RC"
fi

# ─── 6. Créer le dossier wiki ────────────────────────────────
mkdir -p "$INSTALL_DIR/app/wiki"

echo ""
echo -e "${GREEN}╔══════════════════════════════════════════╗${RESET}"
echo -e "${GREEN}║      Installation terminée ! 🎉          ║${RESET}"
echo -e "${GREEN}╚══════════════════════════════════════════╝${RESET}"
echo ""
info "Lance JanotAi avec : ${CYAN}janotai${RESET}"
info "Dossier wiki RAG   : ${CYAN}$INSTALL_DIR/app/wiki/${RESET}"
info "Config             : ${CYAN}$INSTALL_DIR/app/appsettings.json${RESET}"
info "Clé API            : sauvegardée dans ${CYAN}~/.janotai/config.json${RESET} au premier lancement"
echo ""
