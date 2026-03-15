# Janot.ia

An AI agent that runs directly in your terminal and controls your computer. Built with [Semantic Kernel](https://github.com/microsoft/semantic-kernel), powered by [Mistral AI](https://mistral.ai), and extensible via [MCP servers](https://modelcontextprotocol.io).

> Responds in Tunisian dialect by default — customizable in `appsettings.json`.

---

## What it can do

- **Execute shell commands** — cmd, PowerShell (Windows), bash (Mac/Linux)
- **Read/write files** — browse, edit, create files and directories
- **macOS automation** — open apps, install packages via Homebrew, send notifications, manage clipboard
- **RAG on your documents** — index your `.md` and `.txt` files, answer questions about them
- **YouTube transcription** — summarize any YouTube video
- **Multi-agent mode** — `/multi` command for complex tasks requiring planning + execution

---

## Prerequisites

| Requirement | Version | Download |
|---|---|---|
| .NET | 8.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| Mistral API key | — | [console.mistral.ai/api-keys](https://console.mistral.ai/api-keys) |
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

> On first launch, a setup wizard will ask for your Mistral API key and your documents folder.

---

## First launch — Setup wizard

On first run, Janot.ia will ask:

```
Step 1/2 — Mistral API key
  Get your key at: https://console.mistral.ai/api-keys
  Mistral API Key › ****************

Step 2/2 — Documents folder (RAG)
  Path to folder (e.g. ~/Documents/notes) › ~/Documents/notes
  ✅ Folder configured: /Users/you/Documents/notes

✅ Configuration saved to ~/.janotia/config.json
```

The configuration is stored in **`~/.janotia/config.json`** — never in the project directory, never committed to git.

To reconfigure, delete `~/.janotia/config.json` and relaunch.

---

## Usage

```
/help      — show all commands
/tools     — list available tools
/multi     — multi-agent mode (complex tasks)
/clear     — clear conversation history
/history   — show history info
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

Janot.ia indexes your `.md` and `.txt` files with Mistral embeddings (`mistral-embed`).

**Setup at first launch** — you'll be asked for your documents folder path.

**Change folder later** — edit `~/.janotia/config.json`:
```json
{
  "MistralApiKey": "your-key-here",
  "WikiFolder": "/path/to/your/documents"
}
```

**Re-index after adding documents** — delete the cache file and restart:
```bash
# macOS/Linux
rm ~/.local/share/janotia/app/wiki.vectors.json

# Windows
del "%APPDATA%\JanotIA\wiki.vectors.json"
```

Supported formats: `.md` (Markdown), `.txt` (plain text). Subdirectories are scanned recursively.

---

## Configuration

The main config file is `appsettings.json` next to the binary. The API key is **not** stored here — it lives in `~/.janotia/config.json`.

```jsonc
{
  "LLM": {
    "Provider": "mistral",         // mistral | openai | groq | openrouter | azure | ollama
    "Model": "mistral-small-latest",
    "ApiKey": null,                // stored in ~/.janotia/config.json
    "BaseUrl": "https://api.mistral.ai/v1"
  },
  "Agent": {
    "Name": "Janot.ia",
    "Instructions": "..."          // customize personality / language here
  },
  "Embeddings": {
    "Enabled": true,
    "Model": "mistral-embed",
    "BaseUrl": "https://api.mistral.ai/v1"
  }
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

- **Shell blocklist** — dangerous commands are blocked before execution: `rm -rf /`, disk formatting, registry edits, firewall changes, privilege escalation, fork bombs, and more
- **Prompt injection filter** — content from external sources (YouTube, web, wiki) is scanned for injection attempts before being returned to the LLM
- **Destructive action warnings** — the agent explicitly warns before any irreversible operation
- **API keys never in git** — stored in `~/.janotia/config.json` outside the project
- **macOS command escaping** — all user-provided arguments to shell commands are properly escaped to prevent injection

---

## Project structure

```
JanotAI/
├── JanotAi/                     # Main agent application
│   ├── Agents/                  # AgentRunner, MultiAgentOrchestrator
│   ├── Configuration/           # AppConfig (appsettings.json binding)
│   ├── Filters/                 # SecurityCommandFilter, AuditLogFilter
│   ├── Http/                    # MistralCompatibilityHandler
│   ├── Mcp/                     # MCP server registry + plugin loader
│   ├── Persistence/             # Conversation history (JSON)
│   ├── Plugins/                 # Built-in SK plugins (Shell, WhatsApp, etc.)
│   ├── Services/                # SimpleVectorMemory + WikiIndexer (RAG)
│   ├── Setup/                   # FirstRunSetup wizard
│   └── UI/                      # Spectre.Console terminal UI
├── ShellMcpServer/              # MCP server: shell, files, system, macOS tools
│   └── Tools/
│       ├── ShellCommandTools.cs # Windows + cross-platform tools
│       └── MacOsTools.cs        # macOS-specific tools
├── install.sh                   # macOS/Linux install script
└── .gitignore
```

---

## License

MIT
