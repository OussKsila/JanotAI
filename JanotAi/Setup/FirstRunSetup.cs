using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace JanotAi.Setup;

/// <summary>
/// Wizard interactif au premier lancement : demande la clé API et le dossier wiki si absents,
/// et persiste les préférences utilisateur dans ~/.janotia/config.json
///
/// Architecture multi-comptes :
///   ~/.janotia/config.json               — clé API globale (partagée entre les comptes)
///   ~/.janotia/accounts/{nom}/           — répertoire de chaque compte
///     account.json                       — dossier wiki spécifique au compte
///     conversation_history.json          — historique de ce compte
///     wiki.vectors.json                  — cache des vecteurs de ce compte
/// </summary>
public static class FirstRunSetup
{
    private static readonly string ConfigDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".janotia");

    private static readonly string ConfigFile    = Path.Combine(ConfigDir, "config.json");
    private static readonly string AccountsDir   = Path.Combine(ConfigDir, "accounts");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Gestion des comptes ──────────────────────────────────────────────────

    /// <summary>Retourne le répertoire de données du compte.</summary>
    public static string GetAccountDir(string accountName)
        => Path.Combine(AccountsDir, accountName);

    /// <summary>
    /// S'assure que le dossier wiki du compte est configuré.
    /// Retourne le dossier à utiliser.
    /// </summary>
    public static string EnsureAccountWikiFolder(string accountName, string fallback)
    {
        var cfg = LoadAccountConfig(accountName);

        if (!string.IsNullOrWhiteSpace(cfg.WikiFolder) && Directory.Exists(cfg.WikiFolder))
            return cfg.WikiFolder;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Configuration — Dossier wiki[/]").LeftJustified());

        PromptWikiFolder(cfg, fallback);
        SaveAccountConfig(accountName, cfg);

        AnsiConsole.WriteLine();
        return cfg.WikiFolder ?? fallback;
    }

    // ── Public API (clé API globale) ─────────────────────────────────────────

    /// <summary>
    /// Si <paramref name="currentApiKey"/> est vide, lance le wizard interactif
    /// (clé API). Retourne la clé à utiliser.
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

        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>("[cyan]Clé API[/] (ex: MISTRAL_API_KEY) :")
                .Secret()
                .AllowEmpty());

        if (!string.IsNullOrWhiteSpace(apiKey))
            cfg.ApiKey = apiKey.Trim();

        Save(cfg);
        AnsiConsole.MarkupLine("[green]✓ Clé API sauvegardée dans ~/.janotia/config.json[/]");
        AnsiConsole.WriteLine();

        return apiKey ?? "";
    }

    /// <summary>Charge la clé API depuis le fichier de config global.</summary>
    public static string? LoadSavedApiKey() => Load().ApiKey;

    // ── Helpers privés — comptes ─────────────────────────────────────────────

    private static AccountConfig LoadAccountConfig(string accountName)
    {
        var file = Path.Combine(GetAccountDir(accountName), "account.json");
        try
        {
            if (File.Exists(file))
                return JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(file), JsonOpts) ?? new();
        }
        catch { }
        return new AccountConfig();
    }

    private static void SaveAccountConfig(string accountName, AccountConfig cfg)
    {
        var dir  = GetAccountDir(accountName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "account.json"), JsonSerializer.Serialize(cfg, JsonOpts));
    }

    // ── Helpers privés — wiki ────────────────────────────────────────────────

    private static void PromptWikiFolder(AccountConfig cfg, string? fallback = null)
    {
        var startDir = cfg.WikiFolder
            ?? fallback
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!Directory.Exists(startDir))
            startDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AnsiConsole.MarkupLine("[dim]Naviguez jusqu'au dossier wiki puis sélectionnez [cyan]✓ Choisir ce dossier[/].[/]");
        AnsiConsole.WriteLine();

        var chosen = BrowseFolder(startDir);
        if (chosen is null) return;

        cfg.WikiFolder = chosen;
        AnsiConsole.MarkupLine($"[green]✓ Dossier wiki configuré : {Markup.Escape(chosen)}[/]");
    }

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
                var dirName = selection.Trim().TrimEnd('/');
                current = Path.Combine(current, dirName);
            }

            AnsiConsole.WriteLine();
        }
    }

    // ── Config globale (clé API) ─────────────────────────────────────────────

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
        catch { }

        return new UserConfig();
    }

    private static void Save(UserConfig cfg)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(cfg, JsonOpts));
    }

    // ── Modèles ───────────────────────────────────────────────────────────────

    private sealed class UserConfig
    {
        [JsonPropertyName("apiKey")]
        public string? ApiKey { get; set; }
    }

    private sealed class AccountConfig
    {
        [JsonPropertyName("wikiFolder")]
        public string? WikiFolder { get; set; }
    }
}
