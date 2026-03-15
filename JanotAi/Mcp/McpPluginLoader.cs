using System.Text.Json;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;

namespace JanotAi.Mcp;

/// <summary>
/// Convertit les outils d'un serveur MCP en KernelPlugin Semantic Kernel.
///
/// Fonctionnement :
/// 1. Interroge le serveur MCP pour lister ses outils (ListToolsAsync)
/// 2. Pour chaque outil, crée une KernelFunction avec les bons paramètres
/// 3. Regroupe toutes les fonctions dans un KernelPlugin
///
/// L'IA voit chaque outil MCP comme une fonction SK normale qu'elle peut appeler.
/// </summary>
public static class McpPluginLoader
{
    /// <summary>
    /// Charge tous les outils d'un client MCP en tant que KernelPlugin.
    /// </summary>
    public static async Task<KernelPlugin> LoadFromMcpClientAsync(
        string pluginName,
        IMcpClient mcpClient,
        CancellationToken cancellationToken = default)
    {
        // Récupère la liste des outils disponibles sur le serveur MCP
        var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

        Console.WriteLine($"  [MCP] {tools.Count} outil(s) chargé(s) depuis le serveur MCP");

        var functions = tools
            .Select(tool => CreateKernelFunctionFromMcpTool(tool, mcpClient))
            .ToList();

        return KernelPluginFactory.CreateFromFunctions(pluginName, functions);
    }

    /// <summary>
    /// Crée une KernelFunction à partir d'un outil MCP.
    ///
    /// Les paramètres SK sont construits à partir du JSON Schema de l'outil MCP,
    /// permettant à l'IA de savoir quels arguments passer.
    ///
    /// Semantic Kernel injecte automatiquement KernelArguments dans les fonctions,
    /// ce qui nous permet de lire les arguments dynamiquement au runtime.
    /// </summary>
    private static KernelFunction CreateKernelFunctionFromMcpTool(
        McpClientTool tool,
        IMcpClient mcpClient)
    {
        // Extraire les métadonnées des paramètres depuis le JSON Schema MCP
        var parametersMeta = ExtractParametersFromSchema(tool.JsonSchema);

        // Capturer les valeurs dans des closures pour la fonction delegate
        var capturedToolName = tool.Name;
        var capturedParamNames = parametersMeta.Select(p => p.Name).ToList();

        // Créer la fonction delegate qui appellera le serveur MCP
        // KernelArguments est injecté automatiquement par Semantic Kernel
        async Task<string> McpToolDelegate(KernelArguments kernelArgs, CancellationToken ct)
        {
            // Construire le dictionnaire d'arguments pour l'appel MCP
            var mcpArgs = new Dictionary<string, object?>();

            foreach (var paramName in capturedParamNames)
            {
                if (kernelArgs.TryGetValue(paramName, out var value) && value is not null)
                    mcpArgs[paramName] = value.ToString();
            }

            // Appeler l'outil sur le serveur MCP
            var result = await mcpClient.CallToolAsync(
                capturedToolName,
                mcpArgs,
                cancellationToken: ct);

            // Extraire le texte de la réponse MCP
            var textContent = result.Content
                .Where(c => c.Type == "text")
                .Select(c => c.Text)
                .FirstOrDefault();

            return textContent ?? "(aucune réponse textuelle)";
        }

        return KernelFunctionFactory.CreateFromMethod(
            method: McpToolDelegate,
            functionName: tool.Name,
            description: tool.Description ?? $"Outil MCP: {tool.Name}",
            parameters: parametersMeta
        );
    }

    /// <summary>
    /// Extrait les KernelParameterMetadata depuis un JSON Schema MCP.
    ///
    /// Exemple de schema MCP:
    /// {
    ///   "type": "object",
    ///   "properties": {
    ///     "command": { "type": "string", "description": "La commande" },
    ///     "workingDirectory": { "type": "string", "description": "..." }
    ///   },
    ///   "required": ["command"]
    /// }
    /// </summary>
    private static List<KernelParameterMetadata> ExtractParametersFromSchema(JsonElement? schema)
    {
        var parameters = new List<KernelParameterMetadata>();

        if (schema is null)
            return parameters;

        // Récupérer les champs requis
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (schema.Value.TryGetProperty("required", out var requiredEl))
        {
            foreach (var req in requiredEl.EnumerateArray())
            {
                var reqName = req.GetString();
                if (reqName is not null)
                    required.Add(reqName);
            }
        }

        // Récupérer les propriétés
        if (!schema.Value.TryGetProperty("properties", out var properties))
            return parameters;

        foreach (var prop in properties.EnumerateObject())
        {
            var description = "";
            if (prop.Value.TryGetProperty("description", out var descEl))
                description = descEl.GetString() ?? "";

            // Mapper les types JSON Schema vers les types .NET
            var dotnetType = typeof(string);
            if (prop.Value.TryGetProperty("type", out var typeEl))
            {
                dotnetType = typeEl.GetString() switch
                {
                    "integer" => typeof(int),
                    "number"  => typeof(double),
                    "boolean" => typeof(bool),
                    _         => typeof(string)
                };
            }

            var isRequired = required.Contains(prop.Name);

            parameters.Add(new KernelParameterMetadata(prop.Name)
            {
                Description   = description,
                ParameterType = dotnetType,
                IsRequired    = isRequired,
                DefaultValue  = isRequired ? null : (object?)""
            });
        }

        return parameters;
    }
}
