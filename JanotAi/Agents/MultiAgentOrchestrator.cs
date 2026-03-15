#pragma warning disable SKEXP0110, SKEXP0001
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Spectre.Console;

namespace JanotAi.Agents;

/// <summary>
/// Système multi-agents avec collaboration via AgentGroupChat de Semantic Kernel.
///
/// Architecture :
///   ┌──────────────────────────────────────────┐
///   │            AgentGroupChat                │
///   │                                          │
///   │  ┌─────────────┐   ┌─────────────────┐  │
///   │  │ PlannerAgent │   │  ExecutorAgent  │  │
///   │  │  (planifie)  │──▶│  (exécute/MCP) │  │
///   │  └─────────────┘   └─────────────────┘  │
///   │              ▲            │              │
///   │              └────────────┘              │
///   │         (collaboration itérative)        │
///   └──────────────────────────────────────────┘
///
/// Cas d'usage idéal : tâches complexes multi-étapes
///   "Analyse mon projet, trouve les fichiers les plus importants,
///    liste leurs dépendances et propose des améliorations."
/// </summary>
public class MultiAgentOrchestrator
{
    private readonly Kernel _kernel;

    public MultiAgentOrchestrator(Kernel kernel)
    {
        _kernel = kernel;
    }

    /// <summary>
    /// Lance une tâche complexe avec le système multi-agents.
    /// Le PlannerAgent décompose la tâche, l'ExecutorAgent l'exécute avec les outils MCP.
    /// </summary>
    public async Task RunTaskAsync(string task, CancellationToken ct = default)
    {
        // ─── Créer les agents spécialisés ─────────────────────────────────
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature      = 0.2,
            MaxTokens        = 4096
        };

        // Agent 1 : Planificateur (pas d'outils, réfléchit uniquement)
        var plannerAgent = new ChatCompletionAgent
        {
            Name         = "Planificateur",
            Kernel       = _kernel.Clone(),   // clone sans plugins pour ne pas avoir les outils
            Instructions = """
                Tu es un expert en planification de tâches IA.
                Ton rôle : analyser la demande et créer un plan d'action précis et séquencé.
                Décris clairement QUOI faire et DANS QUEL ORDRE.
                Reste concis. Ne demande pas de confirmation. Planifie directement.
                """,
            Arguments    = new KernelArguments(new OpenAIPromptExecutionSettings { Temperature = 0.1 })
        };

        // Agent 2 : Exécuteur (a accès aux outils MCP)
        var executorAgent = new ChatCompletionAgent
        {
            Name         = "Executeur",
            Kernel       = _kernel,
            Instructions = """
                Tu es un agent d'exécution expert en systèmes Windows.
                Tu reçois un plan d'action du Planificateur et tu l'exécutes étape par étape
                en utilisant les outils disponibles (MCP).
                Signale chaque étape accomplie. Sois précis et factuel dans tes rapports.
                Quand toutes les étapes sont terminées, écris exactement : "TÂCHE TERMINÉE"
                """,
            Arguments    = new KernelArguments(executionSettings)
        };

        // ─── Configurer le groupe de chat ─────────────────────────────────
        // Stratégie d'arrêt : termine quand l'Exécuteur dit "TÂCHE TERMINÉE"
        var terminationStrategy = new KeywordTerminationStrategy(
            keyword: "TÂCHE TERMINÉE",
            agentName: executorAgent.Name,
            maxIterations: 12);

        // Stratégie de sélection : alterne Planificateur → Exécuteur → Exécuteur...
        // Le Planificateur parle en premier, puis l'Exécuteur prend la main
        var selectionStrategy = new RoundRobinWithWeightStrategy(
            plannerAgent.Name,
            executorAgent.Name);

        var groupChat = new AgentGroupChat(plannerAgent, executorAgent)
        {
            ExecutionSettings = new AgentGroupChatSettings
            {
                SelectionStrategy    = selectionStrategy,
                TerminationStrategy  = terminationStrategy
            }
        };

        // ─── Lancer la tâche ──────────────────────────────────────────────
        groupChat.AddChatMessage(new ChatMessageContent(AuthorRole.User, task));

        AnsiConsole.Write(new Rule("[yellow]Multi-Agents en action[/]").LeftJustified());
        AnsiConsole.WriteLine();

        await foreach (var response in groupChat.InvokeAsync(ct))
        {
            var color  = response.AuthorName == "Planificateur" ? "blue" : "green";
            var icon   = response.AuthorName == "Planificateur" ? "🧠" : "⚡";
            var author = Markup.Escape(response.AuthorName ?? "Agent");
            var content = Markup.Escape(response.Content ?? "");

            AnsiConsole.Write(new Panel(content)
            {
                Header  = new PanelHeader($" {icon} [{color}]{author}[/] "),
                Border  = BoxBorder.Rounded,
                Padding = new Padding(1, 0)
            });
            AnsiConsole.WriteLine();
        }

        AnsiConsole.Write(new Rule("[green]Tâche terminée[/]").LeftJustified());
    }
}

/// <summary>Arrête le groupe de chat quand un agent dit un mot-clé spécifique.</summary>
file class KeywordTerminationStrategy : TerminationStrategy
{
    private readonly string _keyword;
    private readonly string _agentName;

    public KeywordTerminationStrategy(string keyword, string agentName, int maxIterations)
    {
        _keyword   = keyword;
        _agentName = agentName;
        MaximumIterations = maxIterations;
    }

    protected override Task<bool> ShouldAgentTerminateAsync(
        Agent agent,
        IReadOnlyList<ChatMessageContent> history,
        CancellationToken ct)
    {
        var last = history.LastOrDefault();
        var shouldStop = last?.AuthorName == _agentName
                      && (last?.Content?.Contains(_keyword, StringComparison.OrdinalIgnoreCase) ?? false);
        return Task.FromResult(shouldStop);
    }
}

/// <summary>Planificateur parle en 1er, ensuite l'Exécuteur prend la main.</summary>
file class RoundRobinWithWeightStrategy : SelectionStrategy
{
    private readonly string _plannerName;
    private readonly string _executorName;
    private bool _plannerHasSpoken;

    public RoundRobinWithWeightStrategy(string plannerName, string executorName)
    {
        _plannerName = plannerName;
        _executorName = executorName;
    }

    protected override Task<Agent> SelectAgentAsync(
        IReadOnlyList<Agent> agents,
        IReadOnlyList<ChatMessageContent> history,
        CancellationToken ct)
    {
        Agent selected;

        if (!_plannerHasSpoken)
        {
            _plannerHasSpoken = true;
            selected = agents.First(a => a.Name == _plannerName);
        }
        else
        {
            selected = agents.First(a => a.Name == _executorName);
        }

        return Task.FromResult(selected);
    }
}
