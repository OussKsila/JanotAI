using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using JanotAi.Persistence;
using JanotAi.UI;
using Spectre.Console;

namespace JanotAi.Agents;

/// <summary>
/// Boucle de chat interactive avec l'agent.
/// Utilise IChatCompletionService directement pour éviter les duplications
/// liées aux bugs de ChatCompletionAgent.InvokeStreamingAsync.
/// </summary>
public class AgentRunner
{
    private readonly ChatCompletionAgent    _agent;
    private readonly MultiAgentOrchestrator _multiAgent;
    private readonly ConversationHistory    _history;

    public AgentRunner(
        ChatCompletionAgent    agent,
        MultiAgentOrchestrator multiAgent,
        ConversationHistory    history)
    {
        _agent      = agent;
        _multiAgent = multiAgent;
        _history    = history;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var chatHistory = _history.Load();
        int restored    = chatHistory.Count;

        var tools = _agent.Kernel.Plugins
            .SelectMany(p => p.Select(f => (p.Name, f.Name, f.Description ?? "")))
            .ToList();

        if (restored > 0)
            AgentConsoleUI.PrintSuccess($"{restored} message(s) restaurés depuis la session précédente.");

        var chatCompletion = _agent.Kernel.GetRequiredService<IChatCompletionService>();

#pragma warning disable SKEXP0010
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature      = 0.3,
            MaxTokens        = 4096
        };
#pragma warning restore SKEXP0010

        // ─── Boucle principale ──────────────────────────────────────────────
        while (!ct.IsCancellationRequested)
        {
            var input = AgentConsoleUI.ReadUserInput();

            if (string.IsNullOrWhiteSpace(input)) continue;

            // ─── Commandes internes ─────────────────────────────────────────
            switch (input.Trim().ToLowerInvariant())
            {
                case "exit":
                case "quit":
                    _history.Save(chatHistory);
                    AnsiConsole.MarkupLine("\n[dim]Historique sauvegardé. Au revoir ![/]");
                    return;

                case "/help":
                    AgentConsoleUI.PrintHelp();
                    continue;

                case "/tools":
                    AgentConsoleUI.PrintTools(tools);
                    continue;

                case "/clear":
                    chatHistory.Clear();
                    _history.Clear();
                    AgentConsoleUI.PrintSuccess("Historique effacé.");
                    continue;

                case "/history":
                    AgentConsoleUI.PrintHistory(chatHistory.Count, _history.HasSavedHistory);
                    continue;

                case "/multi":
                    var task = AgentConsoleUI.ReadMultiAgentTask();
                    if (!string.IsNullOrWhiteSpace(task))
                    {
                        try   { await _multiAgent.RunTaskAsync(task, ct); }
                        catch (Exception ex) { AgentConsoleUI.PrintError($"Mode multi-agents : {ex.Message}"); }
                    }
                    continue;
            }

            // ─── Appel au LLM ─────────────────────────────────────────────
            // Construire l'historique complet avec le system prompt + historique + nouveau message
            var fullHistory = new ChatHistory(_agent.Instructions ?? "");
            foreach (var msg in chatHistory)
                fullHistory.Add(msg);
            fullHistory.AddUserMessage(input);

            AgentConsoleUI.PrintAgentResponseStart();

            string fullResponse = "";

            try
            {
                await foreach (var chunk in chatCompletion.GetStreamingChatMessageContentsAsync(
                    fullHistory, executionSettings, _agent.Kernel, ct))
                {
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        AgentConsoleUI.PrintAgentResponseChunk(chunk.Content);
                        fullResponse += chunk.Content;
                    }
                }

                AgentConsoleUI.PrintAgentResponseEnd();

                if (!string.IsNullOrEmpty(fullResponse))
                {
                    chatHistory.AddUserMessage(input);
                    chatHistory.AddAssistantMessage(fullResponse);
                    _history.Save(chatHistory);
                }
            }
            catch (OperationCanceledException)
            {
                AgentConsoleUI.PrintAgentResponseEnd();
            }
            catch (Exception ex)
            {
                AgentConsoleUI.PrintAgentResponseEnd();
                AgentConsoleUI.PrintError(ex.Message);
            }
        }
    }
}
