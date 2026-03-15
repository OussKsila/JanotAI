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

    /// <summary>Pose la question du dossier wiki et met à jour <paramref name="cfg"/>.</summary>
    private static void PromptWikiFolder(UserConfig cfg, string? fallback = null)
    {
        var defaultFolder = cfg.WikiFolder
            ?? fallback
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JanotAI", "wiki");

        AnsiConsole.MarkupLine($"[dim]Dossier wiki actuel : {Markup.Escape(defaultFolder)}[/]");

        var input = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Dossier wiki[/] (Entrée = garder la valeur ci-dessus) :")
                .DefaultValue(defaultFolder)
                .AllowEmpty());

        var chosen = string.IsNullOrWhiteSpace(input) ? defaultFolder : input.Trim();

        if (!Directory.Exists(chosen))
        {
            var create = AnsiConsole.Confirm($"Le dossier [yellow]{Markup.Escape(chosen)}[/] n'existe pas. Le créer ?");
            if (create)
            {
                Directory.CreateDirectory(chosen);
                AnsiConsole.MarkupLine($"[green]✓ Dossier créé : {Markup.Escape(chosen)}[/]");
            }
            else
            {
                return; // l'utilisateur a refusé, on ne sauvegarde pas
            }
        }
        else
        {
            // Vérifier que le dossier ne contient que des .md et .txt
            var invalidFiles = Directory.GetFiles(chosen, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".md",  StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .ToList();

            if (invalidFiles.Count > 0)
            {
                AnsiConsole.MarkupLine($"[red]✗ Le dossier contient des fichiers non supportés (uniquement .md et .txt acceptés) :[/]");
                foreach (var f in invalidFiles.Take(10))
                    AnsiConsole.MarkupLine($"  [red]• {Markup.Escape(f!)}[/]");
                if (invalidFiles.Count > 10)
                    AnsiConsole.MarkupLine($"  [red]... et {invalidFiles.Count - 10} autre(s)[/]");

                AnsiConsole.MarkupLine("[yellow]Veuillez choisir un autre dossier.[/]");
                PromptWikiFolder(cfg, fallback); // redemande
                return;
            }
        }

        cfg.WikiFolder = chosen;
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
