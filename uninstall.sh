#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════
#  JanotAi — Script de désinstallation macOS / Linux
#  Usage : bash uninstall.sh
# ═══════════════════════════════════════════════════════════
set -e

INSTALL_DIR="$HOME/.local/share/janotai"
BIN_DIR="$HOME/.local/bin"
CONFIG_DIR="$HOME/.janotia"

GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
RESET='\033[0m'

info()    { echo -e "${CYAN}[JanotAi]${RESET} $1"; }
success() { echo -e "${GREEN}✅ $1${RESET}"; }
warn()    { echo -e "${YELLOW}⚠  $1${RESET}"; }

echo ""
echo -e "${RED}╔══════════════════════════════════════════╗${RESET}"
echo -e "${RED}║        JanotAi — Désinstallation        ║${RESET}"
echo -e "${RED}╚══════════════════════════════════════════╝${RESET}"
echo ""

warn "Cette opération va supprimer :"
echo "  • $INSTALL_DIR  (binaires + historique de conversation)"
echo "  • $BIN_DIR/janotai  (commande)"
echo "  • $BIN_DIR/janotai-shell-mcp  (commande)"
echo "  • $CONFIG_DIR  (clé API + config)"
echo ""
warn "Vos documents wiki NE seront PAS supprimés."
echo ""

read -rp "Confirmer la désinstallation ? [o/N] " confirm
if [[ "$confirm" != "o" && "$confirm" != "O" ]]; then
    echo "Annulé."
    exit 0
fi

echo ""

# ─── Binaires ────────────────────────────────────────────
if [ -d "$INSTALL_DIR" ]; then
    rm -rf "$INSTALL_DIR"
    success "Binaires supprimés : $INSTALL_DIR"
else
    info "Dossier binaires introuvable (déjà supprimé ?)."
fi

# ─── Commandes ───────────────────────────────────────────
for cmd in janotai janotai-shell-mcp; do
    if [ -f "$BIN_DIR/$cmd" ]; then
        rm -f "$BIN_DIR/$cmd"
        success "Commande supprimée : $BIN_DIR/$cmd"
    fi
done

# ─── Config utilisateur ──────────────────────────────────
if [ -d "$CONFIG_DIR" ]; then
    rm -rf "$CONFIG_DIR"
    success "Configuration supprimée : $CONFIG_DIR"
else
    info "Config introuvable (déjà supprimée ?)."
fi

# ─── Historique parasite (anciens emplacements) ──────────
for stray in \
    "$HOME/conversation_history.json" \
    "$HOME/.local/share/janotia/conversation_history.json"; do
    if [ -f "$stray" ]; then
        rm -f "$stray"
        success "Historique supprimé : $stray"
    fi
done

# ─── Nettoyer le PATH dans shell rc ─────────────────────
for rc in "$HOME/.zshrc" "$HOME/.bashrc"; do
    if [ -f "$rc" ] && grep -q '# JanotAi' "$rc" 2>/dev/null; then
        # Supprimer le bloc "# JanotAi" + la ligne export PATH suivante
        sed -i.bak '/# JanotAi/{N;d;}' "$rc"
        success "PATH nettoyé dans $rc"
    fi
done

echo ""
echo -e "${GREEN}╔══════════════════════════════════════════╗${RESET}"
echo -e "${GREEN}║      Désinstallation terminée ! 👋       ║${RESET}"
echo -e "${GREEN}╚══════════════════════════════════════════╝${RESET}"
echo ""
info "Vos documents wiki sont conservés."
echo ""
