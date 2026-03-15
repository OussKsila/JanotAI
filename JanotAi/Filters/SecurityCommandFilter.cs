using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace JanotAi.Filters;

/// <summary>
/// Filtre SK qui bloque les commandes shell/PowerShell dangereuses
/// avant qu'elles soient exécutées par le plugin.
/// </summary>
public class SecurityCommandFilter : IFunctionInvocationFilter
{
    // Plugins concernés par le filtrage de commandes
    private static readonly HashSet<string> ShellPlugins =
        ["ShellNative", "ShellMcp", "MCP__ShellMcp"];

    // Patterns de commandes dangereuses (regex, case-insensitive)
    private static readonly (string Pattern, string Raison)[] BlockList =
    [
        // Suppression de masse
        (@"\bdel\b.{0,40}/[fFsS].{0,20}/[fFsS]",         "suppression forcée récursive (del /f /s)"),
        (@"\brd\b.{0,20}/[sS]",                            "suppression récursive (rd /s)"),
        (@"\brmdir\b.{0,20}/[sS]",                         "suppression récursive (rmdir /s)"),
        (@"\bremove-item\b.{0,40}-recurse.{0,40}-force",   "suppression récursive PowerShell"),

        // Formatage / partitions
        (@"\bformat\s+[a-zA-Z]:",                          "formatage de disque"),
        (@"\bdiskpart\b",                                  "outil de partitionnement"),

        // Registre
        (@"\breg\s+(delete|add)\b",                        "modification du registre Windows"),
        (@"\bRemove-Item\b.{0,60}HKLM|HKCU",              "modification du registre PowerShell"),

        // Firewall / réseau
        (@"\bnetsh\s+(advfirewall|firewall|wlan\s+set)",   "modification firewall/réseau"),

        // Arrêt système
        (@"\bshutdown\b.{0,20}/[rsf]",                     "arrêt ou redémarrage système"),
        (@"\bStop-Computer\b|\bRestart-Computer\b",         "arrêt PowerShell"),

        // Boot / BIOS
        (@"\bbcdedit\b",                                   "modification configuration de démarrage"),

        // Gestion utilisateurs
        (@"\bnet\s+user\b.{0,40}/add",                     "création d'utilisateur"),
        (@"\bnet\s+localgroup\s+administrators\b",         "modification groupe administrateurs"),
        (@"\bAdd-LocalGroupMember\b",                      "modification groupe PowerShell"),

        // Exécution de code malveillant
        (@"-ExecutionPolicy\s+(Bypass|Unrestricted)",      "contournement politique d'exécution PowerShell"),
        (@"\bInvoke-Expression\b|\bIEX\b",                 "exécution de code PowerShell dynamique"),
        (@"\bDownloadString\b|\bDownloadFile\b|\bWebClient\b", "téléchargement via PowerShell"),
        (@"\bStart-BitsTransfer\b",                        "téléchargement BITS"),

        // Désactivation sécurité
        (@"\bSet-MpPreference\b|\bDisable-WindowsOptionalFeature\b", "modification sécurité Windows"),
        (@"\bnetsh\s+interface\s+ipv[46]\s+set",           "modification configuration réseau"),

        // Chiffrement / effacement (Windows)
        (@"\bcipher\s+/[wW]",                              "effacement sécurisé de l'espace libre"),

        // ── Unix / macOS ──────────────────────────────────────────────────────
        // Suppression récursive de la racine
        (@"\brm\s+-[rf]{1,3}f?\s+/(?:\s|$)",              "suppression récursive de la racine (rm -rf /)"),
        (@"\bsudo\s+rm\b.{0,30}-[rf]",                    "suppression récursive avec sudo"),

        // Escalade de privilèges
        (@"\bsudo\s+su\b",                                 "escalade de privilèges (sudo su)"),
        (@"\bsudo\s+-[iSs]\b",                             "shell root interactif"),
        (@"\bchmod\s+[0-7]*[67][0-7]*\s+/(?:etc|bin|usr|sbin)", "chmod sur répertoire système"),

        // Écriture directe sur disques
        (@"\bdd\b.{0,60}of=/dev/[srnh]d",                 "écriture directe sur disque (dd)"),
        (@"\bmkfs\b",                                      "formatage de partition (mkfs)"),
        (@"\bdiskutil\s+(erase|zeroDisk|format)",          "formatage disque macOS (diskutil)"),

        // Fork bomb
        (@":\(\s*\)\s*\{\s*:\s*\|",                       "fork bomb shell"),

        // Désactivation sécurité macOS
        (@"\bcsrutil\s+disable\b",                         "désactivation SIP macOS"),
        (@"\bspctl\s+--master-disable\b",                  "désactivation Gatekeeper macOS"),

        // Modification de fichiers système critiques
        (@"\bsudo\b.{0,40}/etc/(passwd|shadow|sudoers)",   "modification fichiers système sensibles"),
        (@"\bcrontab\s+-r\b",                              "suppression de toutes les tâches cron"),
    ];

    // Patterns de prompt injection dans les données externes
    private static readonly string[] InjectionPatterns =
    [
        @"ignore\s+(all\s+)?(previous|prior|above)\s+instructions",
        @"system\s*:",
        @"<\s*system\s*>",
        @"\[INST\]|\[\/INST\]",
        @"you\s+are\s+now\s+(a\s+)?(?!janot)",
        @"act\s+as\s+(a\s+)?(?!janot)",
        @"new\s+instructions\s*:",
        @"###\s*(instruction|system|prompt)",
        @"forget\s+(everything|all|your\s+instructions)",
    ];

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        // ── Blocklist commandes shell ──────────────────────────────────────────
        if (IsShellFunction(context))
        {
            var command = GetCommandArg(context);
            if (command is not null)
            {
                var blocked = CheckBlockList(command);
                if (blocked is not null)
                {
                    context.Result = new FunctionResult(context.Function,
                        $"⛔ SÉCURITÉ : Commande bloquée — {blocked}\n" +
                        $"Commande refusée : {command[..Math.Min(command.Length, 100)]}");
                    return;
                }
            }
        }

        // ── Détection prompt injection sur les résultats de contenu externe ──
        if (IsExternalContentFunction(context))
        {
            await next(context);
            var result = context.Result?.GetValue<string>();
            if (result is not null && ContainsInjection(result))
            {
                context.Result = new FunctionResult(context.Function,
                    "[⚠️ Contenu filtré : injection de prompt détectée dans la source externe. " +
                    "Le contenu original a été bloqué par mesure de sécurité.]");
            }
            return;
        }

        await next(context);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsShellFunction(FunctionInvocationContext ctx) =>
        (ctx.Function.PluginName is not null && ShellPlugins.Contains(ctx.Function.PluginName)) ||
        ctx.Function.Name is "ExecuteCommand" or "ExecutePowerShell"
            or "execute_command" or "execute_powershell" or "run_command";

    private static bool IsExternalContentFunction(FunctionInvocationContext ctx) =>
        ctx.Function.Name is "get_transcript" or "get_twitter_trends"
            or "GetTwitterTrends" or "GetTranscript"
            or "search_wiki" or "SearchWikiAsync";

    private static string? GetCommandArg(FunctionInvocationContext ctx)
    {
        foreach (var key in new[] { "command", "script", "cmd", "input" })
            if (ctx.Arguments.TryGetValue(key, out var val) && val is not null)
                return val.ToString();
        return ctx.Arguments.FirstOrDefault().Value?.ToString();
    }

    private static string? CheckBlockList(string command)
    {
        foreach (var (pattern, raison) in BlockList)
            if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase))
                return raison;
        return null;
    }

    private static bool ContainsInjection(string text) =>
        InjectionPatterns.Any(p =>
            Regex.IsMatch(text, p, RegexOptions.IgnoreCase | RegexOptions.Multiline));
}
