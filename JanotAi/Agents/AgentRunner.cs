using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using JanotAi.Persistence;
using JanotAi.UI;
using Spectre.Console;

namespace JanotAi.Agents;

/// <summary>
/// Boucle de chat interactive avec l'agent.
/// Gère le streaming, l'affichage Spectre.Console,
/// la persistance de l'historique et le mode multi-agents.
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
        // Recharger l'historique depuis la session précédente
        var chatHistory = _history.Load();
        int restored = chatHistory.Count;

        var tools = _agent.Kernel.Plugins
            .SelectMany(p => p.Select(f => (p.Name, f.Name, f.Description ?? "")))
            .ToList();

        if (restored > 0)
            AgentConsoleUI.PrintSuccess($"{restored} message(s) restaurés depuis la session précédente.");

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
                        try
                        {
                            await _multiAgent.RunTaskAsync(task, ct);
                        }
                        catch (Exception ex)
                        {
                            AgentConsoleUI.PrintError($"Mode multi-agents : {ex.Message}");
                        }
                    }
                    continue;
            }

            // ─── Appel à l'agent ─────────────────────────────────────────
            chatHistory.AddUserMessage(input);

            AgentConsoleUI.PrintAgentResponseStart();

            string fullResponse    = "";
            bool   toolCallPrinted = false;

            try
            {
#pragma warning disable SKEXP0110, SKEXP0001
                var thread = new ChatHistoryAgentThread(chatHistory);
                await foreach (var item in _agent.InvokeStreamingAsync(thread, cancellationToken: ct))
#pragma warning restore SKEXP0110, SKEXP0001
                {
                    var chunk = item.Message;

                    // Afficher les tool calls
                    if (chunk.Items is not null)
                    {
                        foreach (var part in chunk.Items)
                        {
                            if (part is StreamingFunctionCallUpdateContent sfc && sfc.Name is not null)
                            {
                                if (!toolCallPrinted)
                                {
                                    var parts      = sfc.Name.Split('-', 2);
                                    var pluginName = parts.Length > 1 ? parts[0] : "Plugin";
                                    var funcName   = parts.Length > 1 ? parts[1] : sfc.Name;
                                    AgentConsoleUI.PrintToolCallStart(pluginName, funcName);
                                    toolCallPrinted = true;
                                }
                            }
                        }
                    }

                    // Stream du texte de réponse
                    if (!string.IsNullOrEmpty(chunk.Content))
                    {
                        if (toolCallPrinted)
                        {
                            AgentConsoleUI.PrintToolCallEnd();
                            toolCallPrinted = false;
                            AgentConsoleUI.PrintAgentResponseStart();
                        }
                        AgentConsoleUI.PrintAgentResponseChunk(chunk.Content);
                        fullResponse += chunk.Content;
                    }
                }

                AgentConsoleUI.PrintAgentResponseEnd();

                // ChatHistoryAgentThread gère déjà l'ajout de la réponse dans chatHistory
                if (!string.IsNullOrEmpty(fullResponse))
                    _history.Save(chatHistory);
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
