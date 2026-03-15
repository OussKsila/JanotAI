using System.ComponentModel;
using System.Diagnostics;
using Microsoft.SemanticKernel;

namespace JanotAi.Plugins;

/// <summary>
/// Plugin natif Semantic Kernel exposant des commandes shell.
///
/// APPROCHE 1 (native SK) : Le plugin est directement dans le processus principal.
/// C'est plus simple que MCP mais moins portable/modulaire.
///
/// Pour MCP (Approche 2), voir McpPluginLoader.cs
/// </summary>
public class ShellCommandPlugin
{
    [KernelFunction("execute_command")]
    [Description("Exécute une commande shell et retourne la sortie")]
    public async Task<string> ExecuteCommandAsync(
        [Description("La commande à exécuter")] string command,
        [Description("Répertoire de travail (optionnel)")] string? workingDirectory = null)
    {
        bool isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName               = isWindows ? "cmd.exe" : "bash",
            Arguments              = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            WorkingDirectory       = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0
            ? (string.IsNullOrEmpty(output) ? "(aucune sortie)" : output)
            : $"Erreur (code {process.ExitCode}):\n{error}\nSortie: {output}";
    }

    [KernelFunction("list_directory")]
    [Description("Liste les fichiers et dossiers d'un répertoire")]
    public Task<string> ListDirectoryAsync(
        [Description("Chemin du répertoire (défaut: répertoire courant)")] string? path = null)
    {
        var targetPath = path ?? Directory.GetCurrentDirectory();
        if (!Directory.Exists(targetPath))
            return Task.FromResult($"Répertoire introuvable: {targetPath}");

        var entries = Directory.GetFileSystemEntries(targetPath)
            .Select(e => Directory.Exists(e)
                ? $"[DIR]  {Path.GetFileName(e)}"
                : $"[FILE] {Path.GetFileName(e)}")
            .ToList();

        return Task.FromResult($"{targetPath}:\n" + string.Join("\n", entries));
    }

    [KernelFunction("read_file")]
    [Description("Lit le contenu d'un fichier texte")]
    public async Task<string> ReadFileAsync(
        [Description("Chemin du fichier à lire")] string filePath)
    {
        if (!File.Exists(filePath))
            return $"Fichier introuvable: {filePath}";

        return await File.ReadAllTextAsync(filePath);
    }

    [KernelFunction("get_system_info")]
    [Description("Retourne des informations sur le système (OS, utilisateur, etc.)")]
    public Task<string> GetSystemInfoAsync()
    {
        return Task.FromResult($"""
            OS        : {Environment.OSVersion}
            Machine   : {Environment.MachineName}
            User      : {Environment.UserName}
            CPU Count : {Environment.ProcessorCount}
            .NET      : {Environment.Version}
            CWD       : {Environment.CurrentDirectory}
            """);
    }
}
