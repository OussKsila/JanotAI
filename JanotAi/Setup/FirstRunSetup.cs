using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace JanotAi.Setup;

/// <summary>
/// Wizard interactif au premier lancement : demande la clé API et le dossier wiki si absents,
/// et persiste les préférences utilisateur dans ~/.janotia/config.json
/// </summary>
public static class FirstRunSetup
{
    private static readonly string ConfigDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".janotia");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Si <paramref name="currentApiKey"/> est vide, lance le wizard interactif
    /// (clé API + dossier wiki). Retourne la clé à utiliser.
    /// </summary>
    public static string RunIfNeeded(string? currentApiKey)
    {
        if (!string.IsNullOrWhiteSpace(currentApiKey))
            return currentApiKey;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Premier lancement — Configuration[/]").LeftJustified());
        AnsiConsole.MarkupLine("[dim]Aucune clé API trouvée. Configurez JanotAI pour continuer.[/]");
        AnsiConsole.WriteLine();

        var cfg = Load();

        // ── Clé API ───────────────────────────────────────────────────────────
        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Clé API[/] (ex: MISTRAL_API_KEY) :")
                .Secret()
                .AllowEmpty());

        if (!string.IsNullOrWhiteSpace(apiKey))
            cfg.ApiKey = apiKey.Trim();

        // ── Dossier wiki ──────────────────────────────────────────────────────
        PromptWikiFolder(cfg);

        Save(cfg);
        AnsiConsole.MarkupLine("[green]✓ Configuration sauvegardée dans ~/.janotia/config.json[/]");
        AnsiConsole.WriteLine();

        return apiKey ?? "";
    }

    /// <summary>
    /// Demande le dossier wiki s'il n'est pas encore configuré.
    /// Retourne le dossier à utiliser (sauvegardé ou <paramref name="fallback"/>).
    /// </summary>
    public static string EnsureWikiFolder(string fallback)
    {
        var cfg = Load();

        if (!string.IsNullOrWhiteSpace(cfg.WikiFolder) && Directory.Exists(cfg.WikiFolder))
            return cfg.WikiFolder;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Configuration — Dossier wiki[/]").LeftJustified());

        PromptWikiFolder(cfg, fallback);
        Save(cfg);

        AnsiConsole.WriteLine();
        return cfg.WikiFolder ?? fallback;
    }

    /// <summary>Charge la clé API depuis le fichier de config utilisateur.</summary>
    public static string? LoadSavedApiKey() => Load().ApiKey;

    /// <summary>Charge le dossier wiki depuis le fichier de config utilisateur.</summary>
    public static string? LoadSavedWikiFolder() => Load().WikiFolder;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Navigateur interactif de dossiers avec SelectionPrompt.</summary>
    private static void PromptWikiFolder(UserConfig cfg, string? fallback = null)
    {
        var startDir = cfg.WikiFolder
            ?? fallback
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!Directory.Exists(startDir))
            startDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AnsiConsole.MarkupLine("[dim]Naviguez jusqu'au dossier wiki puis sélectionnez [cyan]✓ Choisir ce dossier[/].[/]");
        AnsiConsole.WriteLine();

        var chosen = BrowseFolder(startDir);
        if (chosen is null) return; // annulé

        cfg.WikiFolder = chosen;
        AnsiConsole.MarkupLine($"[green]✓ Dossier wiki configuré : {Markup.Escape(chosen)}[/]");
    }

    /// <summary>
    /// Navigation interactive par SelectionPrompt. Retourne le dossier choisi ou null si annulé.
    /// </summary>
    private static string? BrowseFolder(string startDir)
    {
        var current = Path.GetFullPath(startDir);

        while (true)
        {
            AnsiConsole.MarkupLine($"[cyan]📁 {Markup.Escape(current)}[/]");

            var choices = new List<string>();
            choices.Add("✓  Choisir ce dossier");

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is not null)
                choices.Add("↑  Remonter (dossier parent)");

            choices.Add("✏  Saisir un chemin manuellement");
            choices.Add("✕  Annuler");

            try
            {
                var subDirs = Directory.GetDirectories(current)
                    .Select(Path.GetFileName)
                    .Where(n => n is not null && !n.StartsWith('.'))
                    .OrderBy(n => n)
                    .Select(n => $"   {n}/")
                    .ToList();

                choices.AddRange(subDirs!);
            }
            catch { /* accès refusé à certains dossiers */ }

            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Sélectionnez une action ou un sous-dossier :[/]")
                    .PageSize(15)
                    .AddChoices(choices));

            if (selection.StartsWith("✓"))
            {
                return current;
            }
            else if (selection.StartsWith("↑"))
            {
                current = parent!;
            }
            else if (selection.StartsWith("✏"))
            {
                var input = AnsiConsole.Prompt(
                    new TextPrompt<string>("[cyan]Chemin du dossier :[/]")
                        .DefaultValue(current)
                        .AllowEmpty());

                var typed = string.IsNullOrWhiteSpace(input) ? current : input.Trim();

                if (!Directory.Exists(typed))
                {
                    var create = AnsiConsole.Confirm($"Le dossier [yellow]{Markup.Escape(typed)}[/] n'existe pas. Le créer ?");
                    if (create)
                    {
                        Directory.CreateDirectory(typed);
                        current = typed;
                    }
                }
                else
                {
                    current = Path.GetFullPath(typed);
                }
            }
            else if (selection.StartsWith("✕"))
            {
                return null;
            }
            else
            {
                // sous-dossier sélectionné — enlever le préfixe "   " et "/"
                var dirName = selection.Trim().TrimEnd('/');
                current = Path.Combine(current, dirName);
            }

            AnsiConsole.WriteLine();
        }
    }

    private static UserConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                return JsonSerializer.Deserialize<UserConfig>(json, JsonOpts) ?? new UserConfig();
            }
        }
        catch { /* config corrompue — on repart de zéro */ }

        return new UserConfig();
    }

    private static void Save(UserConfig cfg)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(cfg, JsonOpts));
    }

    // ── Model ─────────────────────────────────────────────────────────────────

    private sealed class UserConfig
    {
        [JsonPropertyName("apiKey")]
        public string? ApiKey { get; set; }

        [JsonPropertyName("wikiFolder")]
        public string? WikiFolder { get; set; }
    }
}
