using Microsoft.SemanticKernel;

namespace JanotAi.Filters;

/// <summary>
/// Filtre SK qui journalise chaque appel d'outil dans agent_audit.log.
/// Format : [date heure] PLUGIN.FONCTION(args) → Xms | résultat tronqué
/// </summary>
public class AuditLogFilter : IFunctionInvocationFilter
{
    private static readonly string LogFile =
        Path.Combine(AppContext.BaseDirectory, "agent_audit.log");

    // Fonctions à ne pas loguer (trop fréquentes / peu d'intérêt)
    private static readonly HashSet<string> SkipFunctions =
        ["get_whatsapp_status", "GetWhatsAppStatus"];

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        if (SkipFunctions.Contains(context.Function.Name))
        {
            await next(context);
            return;
        }

        var start   = DateTimeOffset.Now;
        string? err = null;

        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            err = ex.Message;
            throw;
        }
        finally
        {
            var elapsed = (DateTimeOffset.Now - start).TotalMilliseconds;

            // Arguments (tronqués à 80 chars par arg)
            var args = string.Join(", ", context.Arguments
                .Where(a => a.Value is not null)
                .Select(a => $"{a.Key}={Trunc(a.Value!.ToString(), 80)}"));

            // Résultat (tronqué à 120 chars)
            var result = err is not null
                ? $"ERROR: {err}"
                : Trunc(context.Result?.GetValue<string>(), 120) ?? "(aucun résultat)";

            var line = $"[{start:yyyy-MM-dd HH:mm:ss}] " +
                       $"{context.Function.PluginName}.{context.Function.Name}" +
                       $"({args}) → {elapsed:0}ms | {result}";

            try { File.AppendAllText(LogFile, line + Environment.NewLine); }
            catch { /* ne jamais faire crasher l'agent à cause des logs */ }
        }
    }

    private static string? Trunc(string? s, int max) =>
        s is null ? null :
        s.Length <= max ? s :
        s[..max] + "…";
}
