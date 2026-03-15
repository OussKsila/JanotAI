using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

namespace JanotAi.Mcp;

/// <summary>
/// Gère le cycle de vie d'une connexion à un serveur MCP.
///
/// Lance le serveur MCP comme sous-processus et établit une connexion
/// client via le transport STDIO (stdin/stdout).
///
/// Architecture :
///   [SemanticKernelAgentsDemo]
///         |-- stdio -->  [ShellMcpServer]
///         |<- stdio --         |
///                         (Exécute commandes)
/// </summary>
public sealed class McpServerManager : IAsyncDisposable
{
    private IMcpClient? _client;

    /// <summary>
    /// Le client MCP connecté (disponible après Connect())
    /// </summary>
    public IMcpClient Client => _client ?? throw new InvalidOperationException("Pas encore connecté. Appelez ConnectAsync() d'abord.");

    /// <summary>
    /// Se connecte à un serveur MCP via transport STDIO.
    /// Lance le serveur comme sous-processus avec la commande donnée.
    /// </summary>
    /// <param name="serverCommand">Commande pour lancer le serveur (ex: "dotnet")</param>
    /// <param name="serverArgs">Arguments de la commande (ex: "run --project ShellMcpServer")</param>
    public async Task ConnectAsync(
        string serverCommand,
        IEnumerable<string> serverArgs,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"  [MCP] Connexion au serveur: {serverCommand} {string.Join(" ", serverArgs)}");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command   = serverCommand,
            Arguments = serverArgs.ToArray(),
            // Le nom est utilisé pour identifier le serveur dans les logs
            Name      = "ShellMcpServer"
        });

        _client = await McpClientFactory.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        Console.WriteLine("  [MCP] Connexion établie !");
    }

    /// <summary>
    /// Connexion simplifiée : lance le serveur ShellMcpServer en mode 'dotnet run'.
    /// Utile pour le développement. En production, pointez vers l'exe publié.
    /// </summary>
    public async Task ConnectToShellServerAsync(
        string mcpServerProjectPath,
        CancellationToken cancellationToken = default)
    {
        await ConnectAsync(
            serverCommand: "dotnet",
            serverArgs: ["run", "--project", mcpServerProjectPath, "--no-build"],
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Connexion via l'exe publié du serveur MCP (plus rapide qu'un 'dotnet run').
    /// </summary>
    public async Task ConnectToShellServerExeAsync(
        string exePath,
        CancellationToken cancellationToken = default)
    {
        await ConnectAsync(
            serverCommand: exePath,
            serverArgs: [],
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (_client is IDisposable disposable)
            disposable.Dispose();
    }
}
