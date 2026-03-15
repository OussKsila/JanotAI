namespace JanotAi.Configuration;

/// <summary>Config racine de l'application</summary>
public class AppConfig
{
    public LlmConfig         LLM        { get; set; } = new();
    public List<McpServerConfig> McpServers { get; set; } = [];
    public AgentConfig       Agent      { get; set; } = new();
    public PersistenceConfig Persistence { get; set; } = new();
    public EmbeddingsConfig  Embeddings  { get; set; } = new();
}

/// <summary>Configuration du LLM (modèle, clé API, provider)</summary>
public class LlmConfig
{
    /// <summary>openai | mistral | groq | openrouter | azure | ollama</summary>
    public string  Provider      { get; set; } = "mistral";
    public string  Model         { get; set; } = "gpt-4o";
    public string? ApiKey        { get; set; }
    public string? BaseUrl       { get; set; }
    public string? AzureEndpoint { get; set; }

    public string ResolvedApiKey =>
        ApiKey
        ?? Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
        ?? ReadFromRegistry("MISTRAL_API_KEY")
        ?? JanotAi.Setup.FirstRunSetup.LoadSavedApiKey()
        ?? "";

    private static string? ReadFromRegistry(string name)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
#pragma warning disable CA1416
            return Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey("Environment")
                ?.GetValue(name) as string;
#pragma warning restore CA1416
        }
        catch { return null; }
    }
}

/// <summary>Configuration d'un serveur MCP (peut être n'importe quel serveur MCP externe)</summary>
public class McpServerConfig
{
    /// <summary>Nom affiché dans les logs</summary>
    public string        Name        { get; set; } = "";

    /// <summary>Description courte des outils exposés</summary>
    public string?       Description { get; set; }

    /// <summary>Nom du plugin SK créé depuis ce serveur</summary>
    public string?       PluginName  { get; set; }

    /// <summary>Commande pour lancer le serveur (ex: "dotnet", "npx", "python")</summary>
    public string        Command     { get; set; } = "";

    /// <summary>Arguments de la commande</summary>
    public List<string>  Args        { get; set; } = [];

    /// <summary>Variables d'environnement supplémentaires pour le serveur</summary>
    public Dictionary<string, string> Env { get; set; } = [];

    public bool Enabled { get; set; } = true;

    /// <summary>Timeout d'initialisation en secondes (utile pour npx au premier lancement). Défaut : 60s</summary>
    public int StartupTimeoutSeconds { get; set; } = 60;

    public string EffectivePluginName => PluginName ?? Name;
}

/// <summary>Configuration de l'agent</summary>
public class AgentConfig
{
    public string Name         { get; set; } = "AgentOS";
    public string Instructions { get; set; } = "Tu es un assistant utile.";
}

/// <summary>Persistance des conversations</summary>
public class PersistenceConfig
{
    public bool   Enabled     { get; set; } = true;
    public string FilePath    { get; set; } = "conversation_history.json";
    public int    MaxMessages { get; set; } = 50;
}

/// <summary>Configuration du moteur d'embeddings pour le RAG (wiki)</summary>
public class EmbeddingsConfig
{
    /// <summary>
    /// Activer le RAG vectoriel sur le dossier wiki/.
    /// Prérequis : installer Ollama (https://ollama.ai) puis : ollama pull nomic-embed-text
    /// </summary>
    public bool    Enabled  { get; set; } = false;

    /// <summary>ollama | openai (tout endpoint compatible OpenAI)</summary>
    public string  Provider { get; set; } = "ollama";

    /// <summary>Modèle d'embeddings. Ollama : nomic-embed-text. OpenAI : text-embedding-3-small</summary>
    public string  Model    { get; set; } = "nomic-embed-text";

    /// <summary>
    /// URL de l'API. Ollama : http://localhost:11434  |  OpenRouter : https://openrouter.ai/api/v1
    /// Laisser null pour utiliser l'URL du LLM principal.
    /// </summary>
    public string? BaseUrl  { get; set; } = "http://localhost:11434";

    /// <summary>Clé API (null = utilise la même que LLM). Ollama n'en a pas besoin.</summary>
    public string? ApiKey   { get; set; }
}
