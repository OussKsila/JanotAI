// ═══════════════════════════════════════════════════════════════════════════
//  JanotAi — AI Agent with MCP (Model Context Protocol)
//  .NET 8 · Semantic Kernel · ModelContextProtocol · Spectre.Console
// ═══════════════════════════════════════════════════════════════════════════
//
//  ┌─────────────────────────────────────────────────────────────────────┐
//  │                    AgentGroupChat (Multi-Agents)                    │
//  │  ┌─────────────┐           ┌───────────────────────────────────┐   │
//  │  │  Planificateur│          │  Executeur (ChatCompletionAgent)  │   │
//  │  │   (réfléchit) │─────────▶│  Kernel + Plugins MCP             │   │
//  │  └─────────────┘           └─────────────────┬─────────────────┘   │
//  └─────────────────────────────────────────────┼─────────────────────┘
//                                                │ stdio JSON-RPC
//  ┌─────────────────────────────────────────────▼─────────────────────┐
//  │  McpServerRegistry ─── N serveurs MCP  (configurés via JSON)      │
//  │   • ShellMcpServer : cmd, PowerShell, fichiers, réseau, processus  │
//  │   • [Ajoutez vos propres serveurs MCP dans appsettings.json]       │
//  └────────────────────────────────────────────────────────────────────┘

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using JanotAi.Agents;
using JanotAi.Configuration;
using JanotAi.Mcp;
using JanotAi.Persistence;
using JanotAi.Filters;
using JanotAi.Plugins;
using JanotAi.Services;
using JanotAi.Http;
using JanotAi.UI;
using JanotAi.Setup;
using Spectre.Console;

// ─── 1. Configuration ────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var appConfig = config.Get<AppConfig>() ?? new AppConfig();
var llm       = appConfig.LLM;
var agent_cfg = appConfig.Agent;

// Lire la clé API depuis IConfiguration (couvre les env vars)
if (string.IsNullOrWhiteSpace(llm.ApiKey))
    llm.ApiKey = config["MISTRAL_API_KEY"];

// Premier lancement : wizard interactif si aucune clé trouvée
if (llm.Provider.ToLower() != "ollama")
    llm.ApiKey = FirstRunSetup.RunIfNeeded(llm.ResolvedApiKey);

// ─── Authentification ────────────────────────────────────────────────────────
var accountName = AuthManager.LoginOrRegister();
var accountDir  = FirstRunSetup.GetAccountDir(accountName);
Directory.CreateDirectory(accountDir);

// Chaque compte a son propre historique de conversation
appConfig.Persistence.FilePath = Path.Combine(accountDir, "conversation_history.json");

// ─── 2. Kernel Semantic Kernel ───────────────────────────────────────────────
await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .SpinnerStyle(Style.Parse("cyan"))
    .StartAsync("Initialisation du kernel...", async ctx =>
    {
        ctx.Status = "Connexion au LLM...";
        await Task.Delay(100); // laisse le spinner apparaître
    });

var kernelBuilder = Kernel.CreateBuilder();

// ─── Sélection du provider LLM ──────────────────────────────────────────────
//
//  Provider   │ Description                       │ Config nécessaire
//  ─────────────────────────────────────────────────────────────────
//  openai     │ OpenAI API (GPT-4o, GPT-4, etc.)  │ ApiKey
//  azure      │ Azure OpenAI Service               │ ApiKey + AzureEndpoint
//  ollama     │ LLM local (Llama3, Mistral, etc.)  │ BaseUrl (défaut localhost)
//
switch (llm.Provider.ToLowerInvariant())
{
    case "azure":
        var endpoint = llm.AzureEndpoint
            ?? throw new InvalidOperationException("LLM.AzureEndpoint requis pour Azure OpenAI.");
        kernelBuilder.AddAzureOpenAIChatCompletion(
            deploymentName: llm.Model,
            endpoint:       endpoint,
            apiKey:         llm.ResolvedApiKey);
        break;

    case "ollama":
        // Ollama expose une API compatible OpenAI sur http://localhost:11434/v1
        // Aucune clé API requise — fonctionne 100% localement !
        var ollamaUrl = llm.BaseUrl ?? "http://localhost:11434";
        kernelBuilder.AddOpenAIChatCompletion(
            modelId:    llm.Model,
            apiKey:     "ollama",   // valeur quelconque, ignorée par Ollama
            httpClient: new HttpClient { BaseAddress = new Uri(ollamaUrl + "/v1") });
        break;

    case "mistral":   // API Mistral (compatible OpenAI)
    case "groq":      // Groq (compatible OpenAI)
    case "openrouter":// OpenRouter (compatible OpenAI)
    default:          // openai natif + tout autre endpoint compatible
        if (!string.IsNullOrEmpty(llm.BaseUrl))
        {
            kernelBuilder.AddOpenAIChatCompletion(
                modelId:    llm.Model,
                apiKey:     llm.ResolvedApiKey,
                httpClient: new HttpClient(new MistralCompatibilityHandler(llm.ResolvedApiKey))
                {
                    BaseAddress = new Uri(llm.BaseUrl)
                });
        }
        else
        {
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: llm.Model,
                apiKey:  llm.ResolvedApiKey);
        }
        break;
}

kernelBuilder.Services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));

var kernel = kernelBuilder.Build();

// ─── Filtres de sécurité ─────────────────────────────────────────────────────
kernel.FunctionInvocationFilters.Add(new SecurityCommandFilter()); // blocklist + anti-injection
kernel.FunctionInvocationFilters.Add(new AuditLogFilter());        // journalisation

#pragma warning disable SKEXP0001
kernel.AutoFunctionInvocationFilters.Add(new MaxToolCallsFilter(maxCalls: 5)); // anti-boucle
#pragma warning restore SKEXP0001

// ─── 3. Plugins natifs SK (toujours disponibles, sans MCP) ──────────────────
kernel.Plugins.AddFromObject(new ShellCommandPlugin(), "ShellNative");
kernel.Plugins.AddFromObject(new WebTrendsPlugin(),    "WebTrends");
kernel.Plugins.AddFromObject(new WhatsAppPlugin(),     "WhatsApp");

// ─── 3b. Wiki RAG (optionnel) ────────────────────────────────────────────────
var embCfg = appConfig.Embeddings;

if (embCfg.Enabled)
{
    // Dossier wiki : config du compte > demande interactif > fallback local
    var wikiFolder = FirstRunSetup.EnsureAccountWikiFolder(
        accountName,
        Path.Combine(AppContext.BaseDirectory, "wiki"));

    // Vérifier qu'au moins un fichier .md ou .txt est présent
    var wikiFiles = Directory.Exists(wikiFolder)
        ? Directory.GetFiles(wikiFolder, "*.*", SearchOption.AllDirectories)
              .Where(f => f.EndsWith(".md",  StringComparison.OrdinalIgnoreCase) ||
                          f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
              .ToArray()
        : [];

    if (wikiFiles.Length == 0)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            $"[red]Le dossier wiki est vide :[/] [bold]{Markup.Escape(wikiFolder)}[/]\n\n" +
            "Ajoutez au moins un fichier [bold].md[/] ou [bold].txt[/] dans ce dossier,\n" +
            "puis relancez JanotAI.\n\n" +
            "[dim]Exemple : notes.md, recettes.txt, projets.md[/]")
        {
            Header  = new PanelHeader(" [red]Wiki RAG — Dossier vide[/] "),
            Border  = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        });
        AnsiConsole.WriteLine();
        Environment.Exit(1);
    }

    try
    {
        var embBaseUrl = embCfg.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? (embCfg.BaseUrl ?? "http://localhost:11434") + "/v1"
            : embCfg.BaseUrl ?? llm.BaseUrl ?? "";

        var embApiKey = string.IsNullOrWhiteSpace(embCfg.ApiKey)
            ? (embCfg.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
                ? "ollama" : llm.ResolvedApiKey)
            : embCfg.ApiKey;


#pragma warning disable SKEXP0001
        // Service d'embeddings direct HTTP — bypass SDK OpenAI pour éviter les conflits d'auth
        ITextEmbeddingGenerationService embService =
            new JanotAi.Http.MistralEmbeddingService(embCfg.Model, embApiKey, embBaseUrl);
#pragma warning restore SKEXP0001

        var wikiMemory = new SimpleVectorMemory(embService);

        // Cache des vecteurs propre à ce compte
        var wikiCache = Path.Combine(accountDir, "wiki.vectors.json");
        int chunks = await WikiIndexer.IndexAsync(wikiMemory, wikiFolder, wikiCache);

        kernel.Plugins.AddFromObject(new WikiPlugin(wikiMemory), "Wiki");

        if (chunks > 0)
            AgentConsoleUI.PrintSuccess($"Wiki RAG: {chunks} chunks indexés depuis {wikiFolder}");
        else
            AgentConsoleUI.PrintSuccess($"Wiki RAG: prêt — ajoute des .md/.txt dans {wikiFolder}");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[yellow]⚠ Wiki RAG désactivé : {Markup.Escape(ex.Message)}[/]");
    }
}

// ─── 4. Connexion aux serveurs MCP ──────────────────────────────────────────
AnsiConsole.WriteLine();
AnsiConsole.Write(new Rule("[dim]Connexion aux serveurs MCP[/]").LeftJustified());

await using var mcpRegistry = new McpServerRegistry();

await AgentConsoleUI.WithSpinnerAsync("Chargement des serveurs MCP...", async () =>
{
    await mcpRegistry.LoadFromConfigAsync(appConfig.McpServers);
});

await mcpRegistry.RegisterAllPluginsAsync(kernel);

// ─── 5. Agent principal ──────────────────────────────────────────────────────
var mainAgent = new ChatCompletionAgent
{
    Name         = agent_cfg.Name,
    Kernel       = kernel,
    Instructions = agent_cfg.Instructions,
    Arguments    = new KernelArguments(new OpenAIPromptExecutionSettings
    {
        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
        Temperature      = 0.3,
        MaxTokens        = 4096
    })
};

// ─── 6. Persistance + Multi-agents ──────────────────────────────────────────
var persistence  = new ConversationHistory(appConfig.Persistence);
var multiAgent   = new MultiAgentOrchestrator(kernel);
var runner       = new AgentRunner(mainAgent, multiAgent, persistence);

// ─── 7. Affichage du header ──────────────────────────────────────────────────
var totalTools = kernel.Plugins.Sum(p => p.Count());
AgentConsoleUI.PrintHeader(
    agentName:   agent_cfg.Name,
    provider:    llm.Provider,
    model:       llm.Model,
    toolCount:   totalTools,
    accountName: accountName);

// ─── 8. Résumé des serveurs chargés ─────────────────────────────────────────
foreach (var (name, desc, toolCount) in mcpRegistry.ServerSummaries)
    AgentConsoleUI.PrintSuccess($"MCP [{name}]: {toolCount} outils — {desc ?? ""}");

AnsiConsole.WriteLine();

// ─── 9. Boucle de chat ───────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    if (!cts.IsCancellationRequested)
        cts.Cancel();
};

await runner.RunAsync(cts.Token);
