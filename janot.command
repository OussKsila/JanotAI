#!/bin/bash
# ═══════════════════════════════════════════════════════════════
#  Janot.ia — Lanceur Mac (double-clique pour démarrer)
# ═══════════════════════════════════════════════════════════════

APP_DIR="$(dirname "$0")"
cd "$APP_DIR"

# Charger les variables d'env (clé API)
source "$HOME/.zshrc"        2>/dev/null || true
source "$HOME/.bash_profile" 2>/dev/null || true
source "$HOME/.profile"      2>/dev/null || true
[[ -f /opt/homebrew/bin/brew ]] && eval "$(/opt/homebrew/bin/brew shellenv)"

# Ajouter le dossier de l'app au PATH pour que shell-mcp soit trouvé
export PATH="$APP_DIR:$PATH"

# Démarrer Ollama si pas actif (wiki RAG)
if ! curl -s http://localhost:11434/api/tags &>/dev/null; then
    ollama serve &>/dev/null &
    sleep 2
fi

clear
echo ""
echo "╔══════════════════════════════════════════════╗"
echo "║              Janot.ia  — Démarrage           ║"
echo "╚══════════════════════════════════════════════╝"
echo ""

./janot

echo ""
read -p "Appuie sur Entrée pour fermer..."
