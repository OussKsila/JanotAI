#pragma warning disable SKEXP0001

using Microsoft.SemanticKernel;

namespace JanotAi.Filters;

/// <summary>
/// Filtre SK qui coupe la boucle d'auto-invocation après N appels d'outils
/// dans un même tour de conversation. Évite les boucles infinies du modèle.
/// </summary>
public class MaxToolCallsFilter(int maxCalls = 5) : IAutoFunctionInvocationFilter
{
    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        // RequestSequenceIndex = numéro du tour d'auto-invocation (0-based)
        if (context.RequestSequenceIndex >= maxCalls)
        {
            context.Terminate = true;
            return;
        }

        await next(context);
    }
}

#pragma warning restore SKEXP0001
