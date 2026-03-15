using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol;
using JanotAi.Configuration;
using Spectre.Console;

namespace JanotAi.Mcp;

/// <summary>
/// Registre centralisé de serveurs MCP.
///
/// Permet de connecter l'agent à N serveurs MCP simultanément,
/// chacun exposant ses propres outils. Tous les outils deviennent
/// disponibles dans le kernel Semantic Kernel.
///
/// Exemple de flux :
///   Registry → connexion à 3 serveurs MCP
///   Kernel   → 3 plugins avec tous leurs outils
///   Agent    → peut appeler n'importe quel outil via function calling
/// </summary>
public sealed class McpServerRegistry : IAsyncDisposable
{
    private readonly List<LoadedServer> _servers = [];

    private record LoadedServer(McpServerConfig Config, IMcpClient Client, int ToolCount);

    /// <summary>Charge tous les serveurs MCP depuis la configuration.</summary>
    public async Task LoadFromConfigAsync(
        IEnumerable<McpServerConfig> configs,
        CancellationToken ct = default)
    {
        foreach (var config in configs.Where(c => c.Enabled))
        {
            await TryLoadServerAsync(config, ct);
        }
    }

    private async Task TryLoadServerAsync(McpServerConfig config, CancellationToken ct)
    {
        try
        {
            AnsiConsole.Markup($"  [dim]Connexion à [bold]{config.Name}[/]...[/] ");

            var client = await ConnectAsync(config, ct);
            var tools  = await client.ListToolsAsync(cancellationToken: ct);

            _servers.Add(new LoadedServer(config, client, tools.Count));

            AnsiConsole.MarkupLine($"[green]✓[/] [dim]({tools.Count} outils)[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(ex.Message)}[/]");
        }
    }

    /// <summary>Importe tous les outils de tous les serveurs MCP dans le kernel SK.</summary>
    public async Task RegisterAllPluginsAsync(Kernel kernel, CancellationToken ct = default)
    {
        foreach (var server in _servers)
        {
            var plugin = await McpPluginLoader.LoadFromMcpClientAsync(
                server.Config.EffectivePluginName,
                server.Client,
                ct);

            kernel.Plugins.Add(plugin);
        }
    }

    /// <summary>Nombre total d'outils MCP chargés sur tous les serveurs.</summary>
    public int TotalToolCount => _servers.Sum(s => s.ToolCount);

    /// <summary>Résumé des serveurs connectés pour l'affichage.</summary>
    public IEnumerable<(string Name, string? Description, int ToolCount)> ServerSummaries =>
        _servers.Select(s => (s.Config.Name, s.Config.Description, s.ToolCount));

    // ─── Connexion ────────────────────────────────────────────────────────────

    private static async Task<IMcpClient> ConnectAsync(McpServerConfig config, CancellationToken ct)
    {
        var options = new StdioClientTransportOptions
        {
            Command   = config.Command,
            Arguments = config.Args.ToArray(),
            Name      = config.Name
        };

        // Inject extra environment variables if configured
        if (config.Env.Count > 0)
        {
            options.EnvironmentVariables = config.Env;
        }

        var transport = new StdioClientTransport(options);

        var clientOptions = new McpClientOptions
        {
            InitializationTimeout = TimeSpan.FromSeconds(config.StartupTimeoutSeconds)
        };

        return await McpClientFactory.CreateAsync(transport, clientOptions, cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers)
        {
            try
            {
                if (server.Client is IAsyncDisposable ad) await ad.DisposeAsync();
                else if (server.Client is IDisposable d)  d.Dispose();
            }
            catch { /* ignore dispose errors */ }
        }
    }
}
