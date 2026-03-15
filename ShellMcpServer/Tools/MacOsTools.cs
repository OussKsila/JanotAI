using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace ShellMcpServer.Tools;

[McpServerToolType]
public static class MacOsTools
{
    // ═══════════════════════════════════════════════════════
    //  APPLICATIONS
    // ═══════════════════════════════════════════════════════

    [McpServerTool]
    [Description("Ouvre une application macOS par son nom (ex: 'Safari', 'Terminal', 'Visual Studio Code').")]
    public static async Task<string> OpenApplication(
        [Description("Nom de l'application (ex: 'Safari', 'Finder', 'Calculator', 'Visual Studio Code')")] string appName)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var result = await RunShell($"open -a {Esc(appName)}");
        return result.ExitCode == 0
            ? $"✅ Application '{appName}' ouverte."
            : $"[Erreur] Impossible d'ouvrir '{appName}': {result.Stderr}";
    }

    [McpServerTool]
    [Description("Ferme/quitte une application macOS par son nom.")]
    public static async Task<string> QuitApplication(
        [Description("Nom de l'application à fermer (ex: 'Safari', 'Spotify')")] string appName)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var result = await RunShell($"osascript -e {Esc($"tell application \"{appName}\" to quit")}");
        return result.ExitCode == 0
            ? $"✅ Application '{appName}' fermée."
            : $"[Erreur] {result.Stderr}";
    }

    [McpServerTool]
    [Description("Liste les applications installées dans /Applications sur macOS.")]
    public static async Task<string> ListInstalledApps(
        [Description("Filtre optionnel sur le nom (ex: 'Adobe', 'Microsoft')")] string? filter = null)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var result = await RunShell("ls /Applications");
        if (result.ExitCode != 0) return $"[Erreur] {result.Stderr}";

        var apps = result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(a => filter == null || a.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(a => $"  • {a}")
            .ToList();

        return $"🖥️ Applications installées{(filter != null ? $" (filtre: '{filter}')" : "")}:\n" +
               string.Join('\n', apps) +
               $"\n\n{apps.Count} application(s) trouvée(s).";
    }

    [McpServerTool]
    [Description("Ouvre une URL dans le navigateur par défaut sur macOS.")]
    public static async Task<string> OpenUrl(
        [Description("URL à ouvrir (ex: 'https://google.com')")] string url)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var result = await RunShell($"open {Esc(url)}");
        return result.ExitCode == 0
            ? $"✅ URL ouverte : {url}"
            : $"[Erreur] {result.Stderr}";
    }

    [McpServerTool]
    [Description("Ouvre un fichier ou un dossier avec l'application par défaut sur macOS.")]
    public static async Task<string> OpenFile(
        [Description("Chemin absolu du fichier ou dossier à ouvrir")] string path)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var result = await RunShell($"open {Esc(path)}");
        return result.ExitCode == 0
            ? $"✅ Ouvert : {path}"
            : $"[Erreur] {result.Stderr}";
    }

    // ═══════════════════════════════════════════════════════
    //  HOMEBREW (gestionnaire de paquets macOS)
    // ═══════════════════════════════════════════════════════

    [McpServerTool]
    [Description("Installe un package via Homebrew sur macOS (ex: 'ffmpeg', 'wget', 'node').")]
    public static async Task<string> BrewInstall(
        [Description("Nom du package Homebrew à installer")] string packageName,
        [Description("Si true, installe une application GUI avec 'brew install --cask'")] bool cask = false,
        [Description("Timeout en secondes (défaut: 120)")] int timeoutSeconds = 120)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var flag = cask ? "--cask " : "";
        var result = await RunShell($"brew install {flag}{Esc(packageName)}", timeoutSeconds);
        return result.ExitCode == 0
            ? $"✅ '{packageName}' installé avec Homebrew."
            : $"[Erreur Homebrew] {result.Stderr}\n{result.Stdout}";
    }

    [McpServerTool]
    [Description("Désinstalle un package Homebrew sur macOS.")]
    public static async Task<string> BrewUninstall(
        [Description("Nom du package à désinstaller")] string packageName,
        [Description("Si true, désinstalle une application GUI (cask)")] bool cask = false)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var flag = cask ? "--cask " : "";
        var result = await RunShell($"brew uninstall {flag}{Esc(packageName)}");
        return result.ExitCode == 0
            ? $"✅ '{packageName}' désinstallé."
            : $"[Erreur Homebrew] {result.Stderr}";
    }

    [McpServerTool]
    [Description("Liste les packages installés via Homebrew sur macOS.")]
    public static async Task<string> BrewList(
        [Description("Si true, liste aussi les applications cask (GUI)")] bool includeCasks = true)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var formulae = await RunShell("brew list --formula");
        (int ExitCode, string Stdout, string Stderr)? casks =
            includeCasks ? await RunShell("brew list --cask") : null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("🍺 Packages Homebrew installés:");
        sb.AppendLine("\n📦 Formulae (CLI):");
        foreach (var p in formulae.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            sb.AppendLine($"  • {p}");

        if (casks.HasValue)
        {
            sb.AppendLine("\n🖥️ Casks (applications GUI):");
            foreach (var p in casks.Value.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                sb.AppendLine($"  • {p}");
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════
    //  FICHIERS ET DOSSIERS (macOS)
    // ═══════════════════════════════════════════════════════

    [McpServerTool]
    [Description("Renomme un fichier ou dossier sur macOS.")]
    public static Task<string> RenameItem(
        [Description("Chemin actuel du fichier/dossier")] string currentPath,
        [Description("Nouveau nom (juste le nom, pas le chemin complet)")] string newName)
    {
        if (!OperatingSystem.IsMacOS())
            return Task.FromResult("[Erreur] Cet outil est réservé à macOS.");

        try
        {
            var dir     = Path.GetDirectoryName(currentPath) ?? "";
            var newPath = Path.Combine(dir, newName);

            if (File.Exists(currentPath))
                File.Move(currentPath, newPath);
            else if (Directory.Exists(currentPath))
                Directory.Move(currentPath, newPath);
            else
                return Task.FromResult($"[Erreur] '{currentPath}' introuvable.");

            return Task.FromResult($"✅ Renommé : '{Path.GetFileName(currentPath)}' → '{newName}'");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"[Erreur] {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Crée un nouveau dossier sur macOS.")]
    public static Task<string> CreateFolder(
        [Description("Chemin absolu du dossier à créer")] string folderPath)
    {
        if (!OperatingSystem.IsMacOS())
            return Task.FromResult("[Erreur] Cet outil est réservé à macOS.");

        try
        {
            Directory.CreateDirectory(folderPath);
            return Task.FromResult($"✅ Dossier créé : {folderPath}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"[Erreur] {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Envoie un fichier ou dossier vers la Corbeille macOS (au lieu de le supprimer définitivement).")]
    public static async Task<string> MoveToTrash(
        [Description("Chemin absolu du fichier ou dossier à mettre à la corbeille")] string path)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var result = await RunShell($"osascript -e {Esc($"tell application \"Finder\" to delete POSIX file \"{path.Replace("\"", "\\\"")}\"")}");
        return result.ExitCode == 0
            ? $"🗑️ Déplacé vers la Corbeille : {path}"
            : $"[Erreur] {result.Stderr}";
    }

    // ═══════════════════════════════════════════════════════
    //  NOTIFICATIONS ET INTERACTION UTILISATEUR
    // ═══════════════════════════════════════════════════════

    [McpServerTool]
    [Description("Affiche une notification macOS (centre de notifications).")]
    public static async Task<string> ShowNotification(
        [Description("Titre de la notification")] string title,
        [Description("Message de la notification")] string message,
        [Description("Sous-titre optionnel")] string? subtitle = null)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var sub = subtitle != null ? $" subtitle \"{subtitle.Replace("\"", "\\\"")}\"" : "";
        var script = $"display notification \"{message.Replace("\"", "\\\"")}\"" +
                     $"{sub} with title \"{title.Replace("\"", "\\\"")}\"";
        var result = await RunShell($"osascript -e {Esc(script)}");
        return result.ExitCode == 0
            ? "✅ Notification envoyée."
            : $"[Erreur] {result.Stderr}";
    }

    [McpServerTool]
    [Description("Lit le contenu du presse-papiers (clipboard) sur macOS.")]
    public static async Task<string> GetClipboard()
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var result = await RunShell("pbpaste");
        return result.ExitCode == 0
            ? $"📋 Clipboard:\n{result.Stdout}"
            : $"[Erreur] {result.Stderr}";
    }

    [McpServerTool]
    [Description("Copie du texte dans le presse-papiers (clipboard) sur macOS.")]
    public static async Task<string> SetClipboard(
        [Description("Texte à copier dans le clipboard")] string text)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var psi = new ProcessStartInfo("bash", "-c \"pbcopy\"")
        {
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.StandardInput.WriteAsync(text);
        process.StandardInput.Close();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? "✅ Texte copié dans le clipboard."
            : "[Erreur] Impossible d'écrire dans le clipboard.";
    }

    // ═══════════════════════════════════════════════════════
    //  SYSTÈME macOS
    // ═══════════════════════════════════════════════════════

    [McpServerTool]
    [Description("Retourne des informations système macOS (version, modèle, mémoire, stockage).")]
    public static async Task<string> GetMacSystemInfo()
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var sw       = await RunShell("sw_vers");
        var model    = await RunShell("system_profiler SPHardwareDataType | grep 'Model Name'");
        var memory   = await RunShell("system_profiler SPHardwareDataType | grep 'Memory'");
        var disk     = await RunShell("df -h /");
        var uptime   = await RunShell("uptime");

        return $"""
            ════════════════ macOS ════════════════
            {sw.Stdout.TrimEnd()}

            ════════════════ MATÉRIEL ════════════════
            {model.Stdout.TrimEnd()}
            {memory.Stdout.TrimEnd()}

            ════════════════ DISQUE ════════════════
            {disk.Stdout.TrimEnd()}

            ════════════════ UPTIME ════════════════
            {uptime.Stdout.TrimEnd()}
            """;
    }

    [McpServerTool]
    [Description("Exécute une commande AppleScript sur macOS (pour automatiser des actions système ou des applications).")]
    public static async Task<string> RunAppleScript(
        [Description("Le script AppleScript à exécuter")] string script,
        [Description("Timeout en secondes (défaut: 30)")] int timeoutSeconds = 30)
    {
        if (!OperatingSystem.IsMacOS())
            return "[Erreur] Cet outil est réservé à macOS.";

        var result = await RunShell($"osascript -e {Esc(script)}", timeoutSeconds);
        return result.ExitCode == 0
            ? (string.IsNullOrEmpty(result.Stdout) ? "✅ AppleScript exécuté." : result.Stdout.TrimEnd())
            : $"[Erreur AppleScript] {result.Stderr}";
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Entoure un argument de guillemets simples et échappe les guillemets simples internes.
    /// Empêche l'injection de commandes shell.
    /// </summary>
    private static string Esc(string arg) => "'" + arg.Replace("'", "'\\''") + "'";

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunShell(
        string command, int timeoutSeconds = 30)
    {
        // On passe la commande comme argument littéral à bash -c
        // (évite une couche d'interprétation du wrapper)
        var psi = new ProcessStartInfo("bash")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);

        using var cts     = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cts.Token);

            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return (-1, "", $"[Timeout] Commande arrêtée après {timeoutSeconds}s.");
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
