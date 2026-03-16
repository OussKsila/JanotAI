using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using JanotAi.Persistence;
using JanotAi.UI;
using Spectre.Console;

namespace JanotAi.Agents;

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

        while (!ct.IsCancellationRequested)
        {
            var input = AgentConsoleUI.ReadUserInput();

            if (string.IsNullOrWhiteSpace(input)) continue;

            // Sélecteur interactif quand l'utilisateur tape "/"
            var trimmed = input.Trim();
            if (trimmed == "/")
            {
                var picked = AgentConsoleUI.ShowCommandPicker();
                if (picked is null) continue;
                trimmed = picked;
            }

            switch (trimmed.ToLowerInvariant())
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

                case "/switch":
                    _history.Save(chatHistory);
                    AnsiConsole.MarkupLine("\n[dim]Déconnexion — relancez JanotAI pour vous connecter avec un autre compte.[/]");
                    return;

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
            var fullHistory = new ChatHistory(_agent.Instructions ?? "");
            foreach (var msg in chatHistory)
                fullHistory.Add(msg);
            fullHistory.AddUserMessage(trimmed);

            ChatMessageContent? response = null;

            try
            {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan"))
                    .StartAsync("Réflexion en cours...", async _ =>
                    {
                        response = await chatCompletion.GetChatMessageContentAsync(
                            fullHistory, executionSettings, _agent.Kernel, ct);
                    });

                var text = response?.Content ?? "";

                AgentConsoleUI.PrintAgentResponseStart();
                AgentConsoleUI.PrintAgentResponseChunk(text);
                AgentConsoleUI.PrintAgentResponseEnd();

                if (!string.IsNullOrEmpty(text))
                {
                    chatHistory.AddUserMessage(trimmed);
                    chatHistory.AddAssistantMessage(text);
                    _history.Save(chatHistory);
                }
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.WriteLine();
            }
            catch (Exception ex)
            {
                AgentConsoleUI.PrintError(ex.Message);
            }
        }
    }
}
