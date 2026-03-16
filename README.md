# JanotAI

An AI agent that runs directly in your terminal and controls your computer. Built with [Semantic Kernel](https://github.com/microsoft/semantic-kernel), powered by [Mistral AI](https://mistral.ai), and extensible via [MCP servers](https://modelcontextprotocol.io).

> Responds in the same language you write in — customizable in `appsettings.json`.

---

## What it can do

- **Execute shell commands** — cmd, PowerShell (Windows), bash (Mac/Linux)
- **Read/write files** — browse, edit, create files and directories
- **macOS automation** — open apps, install packages via Homebrew, send notifications, manage clipboard
- **RAG on your documents** — index your `.md` and `.txt` files, answer questions about them
- **YouTube transcription** — summarize any YouTube video
- **Multi-agent mode** — `/multi` command for complex tasks requiring planning + execution
- **Multi-account** — each user has their own conversation history, wiki vectors, and documents folder

---

## Prerequisites

| Requirement | Version | Download |
|---|---|---|
| .NET | 8.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Mistral API key | — | [console.mistral.ai/api-keys](https://console.mistral.ai/api-keys) *(free tier available)* |
| Node.js *(optional)* | 18+ | [nodejs.org](https://nodejs.org) — only for YouTube transcription |

---

## Install on Linux (Ubuntu / Debian)

**1. Install .NET 8:**
```bash
sudo apt update && sudo apt install -y dotnet-sdk-8.0
```

**2. Clone and install:**
```bash
git clone https://github.com/OussKsila/JanotAI.git
cd JanotAI
bash install.sh
```

**3. Open a new terminal, then launch:**
```bash
janotai
```

---

## Install on macOS

**1. Clone and install:**
```bash
git clone https://github.com/OussKsila/JanotAI.git
cd JanotAI
bash install.sh
```

**2. Open a new terminal, then launch:**
```bash
janotai
```

---

## Install on Windows

**1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download)**

**2. Clone and run:**
```powershell
git clone https://github.com/OussKsila/JanotAI.git
cd JanotAI\JanotAi
dotnet run
```

> On first launch, a setup wizard will ask for your Mistral API key, then prompt you to log in or create an account.

---

## Uninstall

**macOS / Linux:**
```bash
bash uninstall.sh
```
Removes binaries, launcher commands, config, and conversation history. Your wiki documents are **not** deleted.

**Windows:**
```powershell
# Delete the cloned repo (includes binaries)
rmdir /s /q C:\path\to\JanotAI

# Delete config, accounts, and API key
rmdir /s /q "%USERPROFILE%\.janotia"
```

---

## First launch — Setup wizard

On first run, JanotAI will:

1. **Ask for your Mistral API key** (stored in `~/.janotia/config.json`, never committed to git)
2. **Show the login screen** — create an account or log in
3. **Ask for your documents folder** (per account, on first login)

```
JanotAI — Authentification

  > Se connecter
  > Créer un compte        ← first time
  > Quitter

Création de compte
  Votre prénom / pseudo  : Alice
  Identifiant (login)    : alice
  Mot de passe           : ********    (min 8 chars, 1 uppercase, 1 digit)
  Confirmez              : ********

✓ Compte créé : Alice (alice)
```

---

## Multi-account

Each account is fully isolated:

```
~/.janotia/
├── config.json                        ← API key (shared)
└── accounts/
    └── alice/
        ├── auth.json                  ← hashed credentials (PBKDF2-SHA256)
        ├── account.json               ← wiki folder path
        ├── conversation_history.json  ← conversation history
        └── wiki.vectors.json          ← RAG vector cache
```

Switch account: type `/switch` or press `/` and select **Switch account**, then restart JanotAI.

---

## Usage

Press `/` to open the **interactive command picker** instantly:

```
Choisissez une commande :
❯ 📖  /help       Afficher l'aide
  🔧  /tools      Lister tous les outils disponibles
  🗑   /clear      Effacer l'historique de conversation
  📜  /history    Voir le nombre de messages en mémoire
  ⚡  /multi      Mode multi-agents (tâche complexe)
  🔄  /switch     Changer de compte
  🚪  exit        Quitter JanotAI
  ✕   Annuler
```

Or type commands directly:

```
/help      — show all commands
/tools     — list available tools
/multi     — multi-agent mode (complex tasks)
/clear     — clear conversation history
/history   — show history info
/switch    — log out and switch account
exit       — quit (history is auto-saved)
```

### Examples

```
> ouvre Safari et va sur github.com
> installe ffmpeg via homebrew
> renomme le fichier ~/Desktop/rapport.docx en rapport-final.docx
> quel est le processus qui consomme le plus de RAM ?
> résume cette vidéo youtube: https://...
> qu'est-ce qui est écrit dans mes notes sur le projet X ?
```

---

## RAG — Knowledge base

JanotAI indexes your `.md` and `.txt` files with Mistral embeddings (`mistral-embed`).

- All documents are sent to Mistral in a **single batch call** at startup
- The vector cache is stored per account in `~/.janotia/accounts/{name}/wiki.vectors.json`
- Cache is automatically invalidated when you add or modify files

**Re-index after adding documents** — delete the cache and restart:
```bash
# macOS/Linux
rm ~/.janotia/accounts/{your-account}/wiki.vectors.json

# Windows
del "%USERPROFILE%\.janotia\accounts\{your-account}\wiki.vectors.json"
```

Supported formats: `.md` (Markdown), `.txt` (plain text). Subdirectories are scanned recursively.

---

## Configuration

The main config file is `appsettings.json` next to the binary. The API key is **not** stored here.

```jsonc
{
  "LLM": {
    "Provider": "mistral",         // mistral | openai | groq | openrouter | azure | ollama
    "Model": "mistral-small-latest",
    "ApiKey": null,                // stored in ~/.janotia/config.json
    "BaseUrl": "https://api.mistral.ai/v1"
  },
  "Agent": {
    "Name": "JanotAi",
    "Instructions": "..."          // customize personality / language here
  },
  "Embeddings": {
    "Enabled": true,
    "Provider": "mistral",
    "Model": "mistral-embed",
    "BaseUrl": "https://api.mistral.ai/v1"
  },
  "McpServers": [ ... ]
}
```

### Use a local LLM — Ollama (no API key needed)

```jsonc
"LLM": {
  "Provider": "ollama",
  "Model": "llama3.2",
  "BaseUrl": "http://localhost:11434"
}
```

Requires [Ollama](https://ollama.ai): `ollama pull llama3.2`

---

## Add MCP servers

Any MCP-compatible server can be plugged in via `appsettings.json`:

```json
"McpServers": [
  {
    "Name": "MyServer",
    "Description": "My custom tools",
    "PluginName": "MyMcp",
    "Command": "npx",
    "Args": ["-y", "my-mcp-package"],
    "Enabled": true
  }
]
```

---

## Security

- **Authentication** — PBKDF2-SHA256 (100,000 iterations), unique 256-bit salt per account, constant-time comparison, account lockout after 5 failed attempts
- **Shell blocklist** — dangerous commands blocked before execution: `rm -rf /`, disk formatting, registry edits, privilege escalation, fork bombs, and more
- **Prompt injection filter** — content from external sources scanned for injection attempts
- **Destructive action warnings** — agent warns before any irreversible operation
- **API keys never in git** — stored in `~/.janotia/config.json` outside the project
- **Per-account isolation** — each account has its own history, vectors, and wiki folder

---

## Project structure

```
JanotAI/
├── JanotAi/                     # Main agent application
│   ├── Agents/                  # AgentRunner, MultiAgentOrchestrator
│   ├── Configuration/           # AppConfig (appsettings.json binding)
│   ├── Filters/                 # SecurityCommandFilter, AuditLogFilter
│   ├── Http/                    # MistralCompatibilityHandler, MistralEmbeddingService
│   ├── Mcp/                     # MCP server registry + plugin loader
│   ├── Persistence/             # Conversation history (JSON)
│   ├── Plugins/                 # Built-in SK plugins (Shell, WhatsApp, Wiki, etc.)
│   ├── Services/                # SimpleVectorMemory + WikiIndexer (RAG)
│   ├── Setup/                   # FirstRunSetup wizard + AuthManager
│   └── UI/                      # Spectre.Console terminal UI
├── ShellMcpServer/              # MCP server: shell, files, system, macOS tools
│   └── Tools/
│       ├── ShellCommandTools.cs # Windows + cross-platform tools
│       └── MacOsTools.cs        # macOS-specific tools
├── install.sh                   # macOS/Linux install script
├── uninstall.sh                 # macOS/Linux uninstall script
└── .gitignore
```

---

## License

MIT
