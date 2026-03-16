using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

namespace JanotAi.Setup;

/// <summary>
/// Authentification locale multi-comptes avec hachage PBKDF2-SHA256.
///
/// Sécurité :
///  - PBKDF2 SHA-256, 100 000 itérations, sel 256 bits aléatoire unique par compte
///  - Comparaison en temps constant (CryptographicOperations.FixedTimeEquals)
///  - Verrouillage progressif après 5 tentatives échouées (5 min, extensible)
///  - Aucun mot de passe en clair stocké nulle part
/// </summary>
public static class AuthManager
{
    private const int  Pbkdf2Iterations = 100_000;
    private const int  SaltSize         = 32;   // 256 bits
    private const int  HashSize         = 32;   // 256 bits
    private const int  MaxAttempts      = 5;
    private const int  LockMinutes      = 5;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Point d'entrée principal ─────────────────────────────────────────────

    /// <summary>
    /// Affiche l'écran de connexion / inscription.
    /// Retourne le nom du compte authentifié.
    /// </summary>
    public static string LoginOrRegister()
    {
        PrintAuthBanner();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[yellow]Que voulez-vous faire ?[/]")
                    .AddChoices(
                        "🔐  Se connecter",
                        "📝  Créer un compte",
                        "❌  Quitter"));

            if (choice.Contains("Quitter"))
                Environment.Exit(0);

            if (choice.Contains("Se connecter"))
            {
                var account = TryLogin();
                if (account is not null) return account;
            }
            else
            {
                var account = TryRegister();
                if (account is not null) return account;
            }

            AnsiConsole.WriteLine();
        }
    }

    // ── Login ────────────────────────────────────────────────────────────────

    private static string? TryLogin()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan]Connexion[/]").LeftJustified());

        var username = AnsiConsole.Prompt(
            new TextPrompt<string>("[dim]Nom d'utilisateur :[/]")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Champ obligatoire.")));

        username = username.Trim().ToLowerInvariant();

        var authFile = GetAuthFile(username);
        if (!File.Exists(authFile))
        {
            AnsiConsole.MarkupLine("[red]✗ Compte introuvable.[/]");
            return null;
        }

        var creds = LoadCredentials(authFile);
        if (creds is null)
        {
            AnsiConsole.MarkupLine("[red]✗ Données du compte corrompues.[/]");
            return null;
        }

        // Vérifier le verrouillage
        if (creds.LockedUntil.HasValue && creds.LockedUntil > DateTime.UtcNow)
        {
            var remaining = (creds.LockedUntil.Value - DateTime.UtcNow).Minutes + 1;
            AnsiConsole.MarkupLine($"[red]✗ Compte verrouillé. Réessayez dans {remaining} minute(s).[/]");
            return null;
        }

        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("[dim]Mot de passe :[/]")
                .Secret()
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Champ obligatoire.")));

        if (VerifyPassword(password, creds))
        {
            // Réinitialiser les tentatives
            creds.FailedAttempts = 0;
            creds.LockedUntil    = null;
            SaveCredentials(authFile, creds);

            AnsiConsole.MarkupLine($"[green]✓ Bienvenue, [bold]{Markup.Escape(creds.DisplayName ?? username)}[/] ![/]");
            AnsiConsole.WriteLine();
            return username;
        }
        else
        {
            creds.FailedAttempts++;
            int remaining = MaxAttempts - creds.FailedAttempts;

            if (creds.FailedAttempts >= MaxAttempts)
            {
                creds.LockedUntil = DateTime.UtcNow.AddMinutes(LockMinutes);
                SaveCredentials(authFile, creds);
                AnsiConsole.MarkupLine($"[red]✗ Mot de passe incorrect. Compte verrouillé {LockMinutes} minutes.[/]");
            }
            else
            {
                SaveCredentials(authFile, creds);
                AnsiConsole.MarkupLine($"[red]✗ Mot de passe incorrect. {remaining} tentative(s) restante(s).[/]");
            }

            return null;
        }
    }

    // ── Inscription ──────────────────────────────────────────────────────────

    private static string? TryRegister()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[cyan]Création de compte[/]").LeftJustified());

        // Nom d'affichage
        var displayName = AnsiConsole.Prompt(
            new TextPrompt<string>("[dim]Votre prénom / pseudo :[/]")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Champ obligatoire.")));

        // Identifiant de connexion (normalisé)
        var username = AnsiConsole.Prompt(
            new TextPrompt<string>("[dim]Identifiant (login) :[/]")
                .DefaultValue(Normalize(displayName))
                .Validate(s =>
                {
                    if (string.IsNullOrWhiteSpace(s)) return ValidationResult.Error("Champ obligatoire.");
                    var norm = Normalize(s);
                    if (norm.Length < 3)   return ValidationResult.Error("Minimum 3 caractères.");
                    if (norm.Length > 32)  return ValidationResult.Error("Maximum 32 caractères.");
                    return ValidationResult.Success();
                }));

        username = Normalize(username);

        if (File.Exists(GetAuthFile(username)))
        {
            AnsiConsole.MarkupLine("[red]✗ Un compte avec cet identifiant existe déjà.[/]");
            return null;
        }

        // Mot de passe avec confirmation
        string password;
        while (true)
        {
            password = AnsiConsole.Prompt(
                new TextPrompt<string>("[dim]Mot de passe :[/]")
                    .Secret()
                    .Validate(s => ValidatePasswordStrength(s)));

            var confirm = AnsiConsole.Prompt(
                new TextPrompt<string>("[dim]Confirmez le mot de passe :[/]")
                    .Secret());

            if (password == confirm) break;
            AnsiConsole.MarkupLine("[red]✗ Les mots de passe ne correspondent pas.[/]");
        }

        // Hachage + création du compte
        var (hash, salt) = HashPassword(password);

        var creds = new Credentials
        {
            Username    = username,
            DisplayName = displayName.Trim(),
            PasswordHash = Convert.ToBase64String(hash),
            Salt         = Convert.ToBase64String(salt),
            Iterations   = Pbkdf2Iterations,
            CreatedAt    = DateTime.UtcNow
        };

        var accountDir = FirstRunSetup.GetAccountDir(username);
        Directory.CreateDirectory(accountDir);
        SaveCredentials(GetAuthFile(username), creds);

        AnsiConsole.MarkupLine($"[green]✓ Compte créé : [bold]{Markup.Escape(displayName.Trim())}[/] ([dim]{Markup.Escape(username)}[/])[/]");
        AnsiConsole.WriteLine();
        return username;
    }

    // ── Crypto ───────────────────────────────────────────────────────────────

    private static (byte[] hash, byte[] salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password:        password,
            salt:            salt,
            iterations:      Pbkdf2Iterations,
            hashAlgorithm:   HashAlgorithmName.SHA256,
            outputLength:    HashSize);
        return (hash, salt);
    }

    private static bool VerifyPassword(string password, Credentials creds)
    {
        try
        {
            var salt        = Convert.FromBase64String(creds.Salt);
            var storedHash  = Convert.FromBase64String(creds.PasswordHash);
            var iterations  = creds.Iterations > 0 ? creds.Iterations : Pbkdf2Iterations;

            var computedHash = Rfc2898DeriveBytes.Pbkdf2(
                password:      password,
                salt:          salt,
                iterations:    iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength:  storedHash.Length);

            // Comparaison en temps constant — résiste aux attaques de timing
            return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
        }
        catch
        {
            return false;
        }
    }

    // ── Validation du mot de passe ───────────────────────────────────────────

    private static ValidationResult ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return ValidationResult.Error("Champ obligatoire.");
        if (password.Length < 8)
            return ValidationResult.Error("Minimum 8 caractères.");
        if (!password.Any(char.IsUpper))
            return ValidationResult.Error("Au moins une majuscule requise.");
        if (!password.Any(char.IsDigit))
            return ValidationResult.Error("Au moins un chiffre requis.");
        return ValidationResult.Success();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetAuthFile(string username)
        => Path.Combine(FirstRunSetup.GetAccountDir(username), "auth.json");

    private static string Normalize(string name)
        => System.Text.RegularExpressions.Regex
            .Replace(name.Trim().ToLowerInvariant(), @"[^\w\-]", "_");

    private static Credentials? LoadCredentials(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Credentials>(json, JsonOpts);
        }
        catch { return null; }
    }

    private static void SaveCredentials(string path, Credentials creds)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(creds, JsonOpts)); }
        catch { /* ignore write errors */ }
    }

    // ── UI ───────────────────────────────────────────────────────────────────

    private static void PrintAuthBanner()
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            new Markup(
                "[bold cyan]  ╔═══════════════════════════════════╗[/]\n" +
                "[bold cyan]  ║          J a n o t A I            ║[/]\n" +
                "[bold cyan]  ║       AI Agent · MCP              ║[/]\n" +
                "[bold cyan]  ╚═══════════════════════════════════╝[/]"))
        {
            Border  = BoxBorder.None,
            Padding = new Padding(0, 0)
        });
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[dim]Authentification[/]").LeftJustified());
        AnsiConsole.WriteLine();
    }

    // ── Modèle ───────────────────────────────────────────────────────────────

    private sealed class Credentials
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("passwordHash")]
        public string PasswordHash { get; set; } = "";

        [JsonPropertyName("salt")]
        public string Salt { get; set; } = "";

        [JsonPropertyName("iterations")]
        public int Iterations { get; set; } = Pbkdf2Iterations;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("failedAttempts")]
        public int FailedAttempts { get; set; }

        [JsonPropertyName("lockedUntil")]
        public DateTime? LockedUntil { get; set; }
    }
}
