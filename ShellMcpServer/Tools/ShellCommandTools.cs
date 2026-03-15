using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using ModelContextProtocol.Server;

namespace ShellMcpServer.Tools;

[McpServerToolType]
public static class ShellCommandTools
{
    // ═══════════════════════════════════════════════════════
    //  EXÉCUTION DE COMMANDES
    // ═══════════════════════════════════════════════════════

    [McpServerTool]
    [Description("Exécute une commande shell Windows (cmd.exe) et retourne la sortie complète.")]
    public static async Task<string> ExecuteCommand(
        [Description("La commande à exécuter (ex: 'dir', 'ipconfig', 'echo hello')")] string command,
        [Description("Répertoire de travail optionnel")] string? workingDirectory = null,
        [Description("Timeout en secondes (défaut: 30)")] int timeoutSeconds = 30)
    {
        var targetDir = workingDirectory ?? Directory.GetCurrentDirectory();
        if (!Directory.Exists(targetDir))
            return $"[Erreur] Répertoire introuvable: {targetDir}";

        bool isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName               = isWindows ? "cmd.exe" : "bash",
            Arguments              = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = targetDir
        };

        using var cts     = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask  = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error  = await errorTask;

            return process.ExitCode == 0
                ? (string.IsNullOrEmpty(output) ? "(commande exécutée sans sortie)" : output.TrimEnd())
                : $"[Code {process.ExitCode}]\n{(string.IsNullOrEmpty(error) ? output : error).TrimEnd()}";
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return $"[Timeout] La commande a dépassé {timeoutSeconds}s.";
        }
        catch (Exception ex)
        {
            return $"[Erreur] {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Exécute un script PowerShell et retourne la sortie.")]
    public static async Task<string> ExecutePowerShell(
        [Description("Le script PowerShell à exécuter")] string script,
        [Description("Timeout en secondes (défaut: 30)")] int timeoutSeconds = 30)
    {
        // PowerShell Core (pwsh) est disponible sur Mac/Linux, powershell.exe sur Windows uniquement
        string psExe = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        var psi = new ProcessStartInfo
        {
            FileName               = psExe,
            Arguments              = $"-NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        using var cts     = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask  = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error  = await errorTask;

            return string.IsNullOrEmpty(error) ? output.TrimEnd() : $"[Erreur PowerShell]\n{error.TrimEnd()}";
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return $"[Timeout] Script PowerShell arrêté après {timeoutSeconds}s.";
        }
        catch (Exception ex)
        {
            return $"[Erreur] {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════
    //  SYSTÈME DE FICHIERS
    // ═══════════════════════════════════════════════════════

    [McpServerTool]
    [Description("Liste les fichiers et dossiers dans un répertoire avec tailles et dates.")]
    public static Task<string> ListDirectory(
        [Description("Chemin du répertoire (défaut: répertoire courant)")] string? path = null,
        [Description("Inclure les fichiers cachés ?")] bool includeHidden = false)
    {
        var targetPath = path ?? Directory.GetCurrentDirectory();
        if (!Directory.Exists(targetPath))
            return Task.FromResult($"[Erreur] Répertoire introuvable: {targetPath}");

        try
        {
            var dirs = Directory.GetDirectories(targetPath)
                .Select(d => new DirectoryInfo(d))
                .Where(d => includeHidden || !d.Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(d => d.Name)
                .Select(d => $"  [DIR]  {d.Name,-40} {d.LastWriteTime:yyyy-MM-dd HH:mm}");

            var files = Directory.GetFiles(targetPath)
                .Select(f => new FileInfo(f))
                .Where(f => includeHidden || !f.Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(f => f.Name)
                .Select(f => $"  [FILE] {f.Name,-40} {FormatSize(f.Length),10}  {f.LastWriteTime:yyyy-MM-dd HH:mm}");

            var entries = dirs.Concat(files).ToList();
            return Task.FromResult(
                $"📁 {targetPath}\n{"─".PadRight(80, '─')}\n" +
                string.Join("\n", entries) +
                $"\n{"─".PadRight(80, '─')}\n  {entries.Count} élément(s)");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"[Erreur] {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Lit le contenu d'un fichier texte.")]
    public static async Task<string> ReadFile(
        [Description("Chemin absolu ou relatif du fichier")] string filePath,
        [Description("Nombre max de lignes à lire (0 = tout)")] int maxLines = 0)
    {
        if (!File.Exists(filePath))
            return $"[Erreur] Fichier introuvable: {filePath}";

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            var total = lines.Length;

            if (maxLines > 0 && lines.Length > maxLines)
                lines = [.. lines.Take(maxLines), $"... ({total - maxLines} lignes supplémentaires)"];

            return $"📄 {filePath} ({total} lignes)\n{"─".PadRight(60, '─')}\n" + string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"[Erreur] {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Écrit ou ajoute du texte dans un fichier.")]
    public static async Task<string> WriteFile(
        [Description("Chemin du fichier à écrire")] string filePath,
        [Description("Contenu à écrire")] string content,
        [Description("Si true, ajoute à la fin au lieu de remplacer")] bool append = false)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (append) await File.AppendAllTextAsync(filePath, content);
            else        await File.WriteAllTextAsync(filePath, content);

            var info = new FileInfo(filePath);
            return $"✅ Fichier {(append ? "mis à jour" : "écrit")}: {filePath} ({FormatSize(info.Length)})";
        }
        catch (Exception ex)
        {
            return $"[Erreur] {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Recherche des fichiers correspondant à un pattern dans un répertoire.")]
    public static Task<string> SearchFiles(
        [Description("Répertoire racine de la recherche")] string rootPath,
        [Description("Pattern (ex: '*.cs', '*.log', 'config*')")] string pattern,
        [Description("Rechercher dans les sous-dossiers ?")] bool recursive = true)
    {
        if (!Directory.Exists(rootPath))
            return Task.FromResult($"[Erreur] Répertoire introuvable: {rootPath}");

        try
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files  = Directory.GetFiles(rootPath, pattern, option).Take(100).ToList();

            return Task.FromResult(
                $"🔍 '{pattern}' dans {rootPath}:\n" +
                (files.Count == 0 ? "  (aucun fichier trouvé)" : string.Join("\n", files.Select(f => $"  {f}"))) +
                (files.Count >= 100 ? "\n  [limité à 100 résultats]" : ""));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"[Erreur] {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════
    //  SYSTÈME
    // ═══════════════════════════════════════════════════════

    [McpServerTool]
    [Description("Retourne des informations détaillées sur le système (OS, CPU, disques).")]
    public static Task<string> GetSystemInfo()
    {
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => $"    {d.Name} ({d.DriveType}) — {FormatSize(d.AvailableFreeSpace)} libres / {FormatSize(d.TotalSize)}")
            .ToList();

        return Task.FromResult($"""
            ════════════════ SYSTÈME ════════════════
            OS          : {Environment.OSVersion}
            Machine     : {Environment.MachineName}
            Utilisateur : {(OperatingSystem.IsWindows() ? $"{Environment.UserDomainName}\\" : "")}{Environment.UserName}
            CPU cores   : {Environment.ProcessorCount}
            .NET Runtime: {Environment.Version}

            ════════════════ DISQUES ════════════════
            {string.Join("\n", drives)}

            ══════════════ RÉPERTOIRES ══════════════
            Courant     : {Environment.CurrentDirectory}
            Bureau      : {Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}
            """);
    }

    [McpServerTool]
    [Description("Liste les processus en cours, triés par consommation mémoire.")]
    public static Task<string> ListProcesses(
        [Description("Filtre sur le nom (optionnel)")] string? filter = null,
        [Description("Nombre max de résultats (défaut: 20)")] int top = 20)
    {
        try
        {
            var processes = Process.GetProcesses()
                .Where(p => filter == null || p.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
                .Take(top)
                .Select(p =>
                {
                    try { return $"  {p.Id,6} │ {p.ProcessName,-28} │ {FormatSize(p.WorkingSet64),9}"; }
                    catch { return $"  {p.Id,6} │ {p.ProcessName,-28} │ (accès refusé)"; }
                })
                .ToList();

            return Task.FromResult(
                $"🖥️ Processus{(filter != null ? $" (filtre: '{filter}')" : "")}:\n" +
                $"{"PID",6} │ {"Nom",-28} │ {"RAM",9}\n{"─".PadRight(52, '─')}\n" +
                string.Join("\n", processes));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"[Erreur] {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Retourne les variables d'environnement du système.")]
    public static Task<string> GetEnvironmentVariables(
        [Description("Filtre sur le nom de la variable (optionnel)")] string? filter = null)
    {
        var vars = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(e => filter == null || e.Key.ToString()!.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Key.ToString())
            .Select(e => $"  {e.Key} = {e.Value}")
            .Take(50)
            .ToList();

        return Task.FromResult(
            $"🌍 Variables d'environnement{(filter != null ? $" (filtre: '{filter}')" : "")}:\n" +
            string.Join("\n", vars));
    }

    [McpServerTool]
    [Description("Retourne le répertoire de travail courant.")]
    public static Task<string> GetCurrentDirectory() =>
        Task.FromResult(Directory.GetCurrentDirectory());

    // ═══════════════════════════════════════════════════════
    //  RÉSEAU
    // ═══════════════════════════════════════════════════════

    [McpServerTool]
    [Description("Ping une adresse pour tester la connectivité réseau.")]
    public static async Task<string> PingHost(
        [Description("Nom d'hôte ou adresse IP à pinger")] string host,
        [Description("Nombre de pings (défaut: 4)")] int count = 4)
    {
        var results = new List<string>();
        using var ping = new Ping();

        for (int i = 0; i < count; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(host, 2000);
                results.Add(reply.Status == IPStatus.Success
                    ? $"  Réponse de {reply.Address}: {reply.RoundtripTime}ms"
                    : $"  Délai dépassé ({reply.Status})");
            }
            catch (Exception ex)
            {
                results.Add($"  Erreur: {ex.Message}");
                break;
            }
        }

        return $"🌐 Ping {host}:\n" + string.Join("\n", results);
    }

    [McpServerTool]
    [Description("Retourne les informations sur les interfaces réseau actives.")]
    public static Task<string> GetNetworkInfo()
    {
        var lines = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
            .SelectMany(ni =>
            {
                var ips = ni.GetIPProperties().UnicastAddresses
                    .Select(a => $"      IP: {a.Address}");
                return new[] { $"  🔌 {ni.Name} ({ni.NetworkInterfaceType}) — MAC: {ni.GetPhysicalAddress()}" }
                    .Concat(ips);
            });

        return Task.FromResult("🌐 Interfaces réseau actives:\n" + string.Join("\n", lines));
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }
}
