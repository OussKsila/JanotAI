#!/bin/bash
# ═══════════════════════════════════════════════════════════════
#  Janot.ia — Installeur Mac (double-clique pour lancer)
#  Première fois : clic droit → Ouvrir → Ouvrir quand même
# ═══════════════════════════════════════════════════════════════

cd "$(dirname "$0")"

G='\033[0;32m'; B='\033[0;34m'; Y='\033[1;33m'; R='\033[0;31m'; N='\033[0m'
ok()   { echo -e "${G}✅ $1${N}"; }
info() { echo -e "${B}→  $1${N}"; }
warn() { echo -e "${Y}⚠  $1${N}"; }
err()  { echo -e "${R}❌ $1${N}"; }
step() { echo -e "\n${B}━━━ $1 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${N}"; }

clear
echo ""
echo "╔══════════════════════════════════════════════╗"
echo "║         Installation de Janot.ia — Mac       ║"
echo "╚══════════════════════════════════════════════╝"
echo ""

# ── 1. Homebrew ─────────────────────────────────────────────────
step "1/4 · Homebrew"
if command -v brew &>/dev/null; then
    ok "Homebrew déjà installé"
else
    info "Installation de Homebrew..."
    /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
    [[ -f /opt/homebrew/bin/brew ]] && eval "$(/opt/homebrew/bin/brew shellenv)"
    echo 'eval "$(/opt/homebrew/bin/brew shellenv)"' >> "$HOME/.zprofile"
    ok "Homebrew installé"
fi

# ── 2. Node.js (pour YouTube MCP) ───────────────────────────────
step "2/4 · Node.js"
if command -v node &>/dev/null; then
    ok "Node.js $(node --version) déjà installé"
else
    info "Installation de Node.js..."
    brew install node
    ok "Node.js installé"
fi

# ── 3. Ollama (wiki RAG) ────────────────────────────────────────
step "3/4 · Ollama (wiki RAG)"
if command -v ollama &>/dev/null; then
    ok "Ollama déjà installé"
else
    info "Installation d'Ollama..."
    brew install ollama
    ok "Ollama installé"
fi

if ! curl -s http://localhost:11434/api/tags &>/dev/null; then
    info "Démarrage d'Ollama..."
    ollama serve &>/dev/null &
    sleep 4
fi

if ollama list 2>/dev/null | grep -q "nomic-embed-text"; then
    ok "nomic-embed-text déjà téléchargé"
else
    info "Téléchargement nomic-embed-text (~274 MB)..."
    ollama pull nomic-embed-text
    ok "nomic-embed-text prêt"
fi

# ── 4. Clé API Mistral ──────────────────────────────────────────
step "4/4 · Clé API Mistral"

[[ "$SHELL" == *"zsh"* ]]  && PROFILE="$HOME/.zshrc" || PROFILE="$HOME/.bash_profile"

if grep -q "MISTRAL_API_KEY" "$PROFILE" 2>/dev/null || [[ -n "$MISTRAL_API_KEY" ]]; then
    ok "Clé API déjà configurée"
else
    echo ""
    echo "  Crée un compte sur : https://console.mistral.ai"
    echo "  API Keys → Create API Key → copie la clé"
    echo ""
    read -p "  Colle ta clé API ici : " API_KEY
    if [[ -n "$API_KEY" ]]; then
        echo "" >> "$PROFILE"
        echo "export MISTRAL_API_KEY=\"$API_KEY\"" >> "$PROFILE"
        export MISTRAL_API_KEY="$API_KEY"
        ok "Clé sauvegardée dans $PROFILE"
    else
        warn "Pas de clé — à ajouter plus tard dans $PROFILE"
    fi
fi

# ── Rendre les binaires exécutables ─────────────────────────────
chmod +x "$(dirname "$0")/janot" 2>/dev/null
chmod +x "$(dirname "$0")/shell-mcp" 2>/dev/null

echo ""
echo "╔══════════════════════════════════════════════╗"
echo "║   ✅  Installation terminée !                ║"
echo "║   → Double-clique sur  janot.command         ║"
echo "╚══════════════════════════════════════════════╝"
echo ""
read -p "Appuie sur Entrée pour fermer..."
