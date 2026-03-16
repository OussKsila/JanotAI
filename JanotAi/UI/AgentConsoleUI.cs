using Spectre.Console;

namespace JanotAi.UI;

/// <summary>
/// Interface console enrichie avec Spectre.Console.
/// Toute la présentation visuelle de l'application passe par ici.
/// </summary>
public static class AgentConsoleUI
{
    // ─── Header ──────────────────────────────────────────────────────────────

    private static readonly string[] LogoLines =
    [
        @"",
        @"   ╔═══════════════════════════════════╗",
        @"   ║                                   ║",
        @"   ║          J a n o t A I            ║",
        @"   ║                                   ║",
        @"   ║       AI Agent · MCP              ║",
        @"   ╚═══════════════════════════════════╝",
        @"",
    ];

    public static void PrintHeader(string agentName, string provider, string model, int toolCount, bool multiAgent = false, string? accountName = null)
    {
        AnsiConsole.Clear();

        // Athlète + wordmark
        foreach (var line in LogoLines)
            AnsiConsole.MarkupLine($"[bold cyan]{Markup.Escape(line)}[/]");

        // Barre d'info
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(
            new Markup($"[dim]Agent:[/]  [bold cyan]{Markup.Escape(agentName)}[/]"),
            new Markup($"[dim]LLM:[/]    [bold]{Markup.Escape(provider)}/{Markup.Escape(model)}[/]"),
            new Markup($"[dim]Outils:[/] [bold green]{toolCount}[/]"),
            new Markup(accountName is not null
                ? $"[dim]Compte:[/] [bold magenta]{Markup.Escape(accountName)}[/]"
                : ""),
            new Markup(multiAgent ? "[bold yellow]⚡ Multi-Agents[/]" : "[dim]Mode: Simple[/]")
        );

        AnsiConsole.Write(new Panel(grid)
        {
            Border  = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Tapez [bold]/help[/] pour les commandes, [bold]/multi[/] pour le mode multi-agents, [bold]exit[/] pour quitter[/]");
        AnsiConsole.Write(new Rule().RuleStyle("dim grey"));
        AnsiConsole.WriteLine();
    }

    // ─── Messages ─────────────────────────────────────────────────────────────

    public static string ReadUserInput()
    {
        AnsiConsole.Markup("[dim](  /  commandes)[/]  [bold cyan]Vous[/] [dim]›[/] ");

        var buffer = new System.Text.StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            // Entrée → valider
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            // Backspace
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Remove(buffer.Length - 1, 1);
                    Console.Write("\b \b");
                }
                continue;
            }

            // "/" en première position → ouvrir le picker instantanément
            if (key.KeyChar == '/' && buffer.Length == 0)
            {
                Console.WriteLine();
                var picked = ShowCommandPicker();
                return picked ?? "";
            }

            // Caractère normal
            if (key.KeyChar != '\0')
            {
                buffer.Append(key.KeyChar);
                Console.Write(key.KeyChar);
            }
        }
    }

    public static void PrintAgentResponseStart()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[bold green]Agent[/] [dim]›[/] ");
    }

    public static void PrintAgentResponseChunk(string chunk)
    {
        // Écriture directe pour le streaming (Spectre n'interrompt pas)
        Console.Write(chunk);
    }

    public static void PrintAgentResponseEnd()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    // ─── Tool Calls ──────────────────────────────────────────────────────────

    private static long _toolCallStart;

    public static void PrintToolCallStart(string pluginName, string functionName)
    {
        _toolCallStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        AnsiConsole.WriteLine();
        AnsiConsole.Markup(
            $"  [yellow]⚡[/] [dim]Outil:[/] [bold yellow]{Markup.Escape(pluginName)}.{Markup.Escape(functionName)}[/] " +
            "[dim]…[/]");
    }

    public static void PrintToolCallEnd()
    {
        var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _toolCallStart;
        AnsiConsole.MarkupLine($" [dim green]✓[/] [dim]({elapsed}ms)[/]");
    }

    // ─── Status / Spinner ─────────────────────────────────────────────────────

    public static async Task WithSpinnerAsync(string message, Func<Task> action)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync(message, async _ => await action());
    }

    // ─── Commandes système ────────────────────────────────────────────────────

    public static void PrintHelp()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[bold]Commande[/]").Width(18))
            .AddColumn("[bold]Description[/]");

        table.AddRow("[cyan]/help[/]",    "Afficher cette aide");
        table.AddRow("[cyan]/tools[/]",   "Lister tous les outils disponibles");
        table.AddRow("[cyan]/clear[/]",   "Effacer l'historique de conversation");
        table.AddRow("[cyan]/multi[/]",   "Mode multi-agents (tâche complexe)");
        table.AddRow("[cyan]/history[/]", "Voir le nombre de messages en mémoire");
        table.AddRow("[cyan]/switch[/]",  "Changer de compte (relance le sélecteur)");
        table.AddRow("[red]exit[/]",      "Quitter l'application");
        table.AddEmptyRow();
        table.AddRow("[dim]Exemples[/]",  "[dim]\"Exécute ipconfig\"[/]");
        table.AddRow("",                  "[dim]\"Liste les fichiers dans C:\\Users\"[/]");
        table.AddRow("",                  "[dim]\"Quels processus tournent en ce moment ?\"[/]");

        AnsiConsole.Write(table);
    }

    public static void PrintTools(IEnumerable<(string Plugin, string Function, string Description)> tools)
    {
        var table = new Table()
            .Title("[bold]Outils disponibles[/]")
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[bold]Plugin[/]").Width(16))
            .AddColumn(new TableColumn("[bold]Fonction[/]").Width(24))
            .AddColumn("[bold]Description[/]");

        foreach (var (plugin, function, description) in tools)
        {
            table.AddRow(
                $"[yellow]{Markup.Escape(plugin)}[/]",
                $"[bold]{Markup.Escape(function)}[/]",
                Markup.Escape(description));
        }

        AnsiConsole.Write(table);
    }

    public static void PrintHistory(int messageCount, bool isPersisted)
    {
        AnsiConsole.MarkupLine(
            $"[dim]Historique: [bold]{messageCount}[/] message(s) " +
            (isPersisted ? "[green](sauvegardé)[/]" : "[grey](en mémoire uniquement)[/]") +
            "[/]");
    }

    // ─── Erreurs ──────────────────────────────────────────────────────────────

    public static void PrintError(string message)
    {
        AnsiConsole.Write(new Panel($"[red]{Markup.Escape(message)}[/]")
        {
            Header  = new PanelHeader(" [red]Erreur[/] "),
            Border  = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        });
    }

    public static void PrintWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]⚠[/] [dim]{Markup.Escape(message)}[/]");
    }

    public static void PrintSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");
    }

    // ─── Sélecteur de commandes interactif ───────────────────────────────────

    private static readonly (string Command, string Description, string Icon)[] Commands =
    [
        ("/help",    "Afficher l'aide",                          "📖"),
        ("/tools",   "Lister tous les outils disponibles",       "🔧"),
        ("/clear",   "Effacer l'historique de conversation",     "🗑 "),
        ("/history", "Voir le nombre de messages en mémoire",    "📜"),
        ("/multi",   "Mode multi-agents (tâche complexe)",       "⚡"),
        ("/switch",  "Changer de compte",                        "🔄"),
        ("exit",     "Quitter JanotAI",                         "🚪"),
    ];

    /// <summary>
    /// Affiche un sélecteur interactif de commandes.
    /// Retourne la commande choisie (ex: "/help") ou null si annulé.
    /// </summary>
    public static string? ShowCommandPicker()
    {
        AnsiConsole.WriteLine();

        var choices = Commands
            .Select(c => $"{c.Icon}  [bold]{c.Command,-10}[/] [dim]{c.Description}[/]")
            .Append("[dim]✕  Annuler[/]")
            .ToList();

        var raw = Commands
            .Select(c => c.Command)
            .Append("__cancel__")
            .ToArray();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[yellow]Choisissez une commande :[/]")
                .PageSize(10)
                .UseConverter(s => s)
                .AddChoices(choices));

        var idx = choices.IndexOf(selected);
        var cmd = raw[idx];

        AnsiConsole.WriteLine();
        return cmd == "__cancel__" ? null : cmd;
    }

    // ─── Multi-agent ─────────────────────────────────────────────────────────

    public static string ReadMultiAgentTask()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            "[dim]Décrivez une tâche complexe. Les agents vont collaborer pour la réaliser automatiquement.\n" +
            "Exemple : [italic]\"Analyse mon bureau, liste les fichiers les plus récents et donne-moi un résumé\"[/][/]")
        {
            Header  = new PanelHeader(" [yellow]⚡ Mode Multi-Agents[/] "),
            Border  = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        });
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[bold yellow]Tâche[/] [dim]›[/] ");
        return Console.ReadLine() ?? "";
    }

    // ─── Separator ────────────────────────────────────────────────────────────

    public static void PrintSeparator() =>
        AnsiConsole.Write(new Rule().RuleStyle("dim grey"));

    public static void PrintSectionTitle(string title) =>
        AnsiConsole.Write(new Rule($"[dim]{Markup.Escape(title)}[/]").LeftJustified());
}
