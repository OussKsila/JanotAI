using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// ============================================================
//  ShellMcpServer - Serveur MCP exposant des outils shell
// ============================================================
//
// Ce programme implémente un serveur MCP (Model Context Protocol)
// via le transport STDIO. Il est lancé comme sous-processus par
// l'application principale (SemanticKernelAgentsDemo).
//
// Communication: STDIN → requêtes JSON-RPC ← STDOUT
// ============================================================

var builder = Host.CreateApplicationBuilder(args);

// Rediriger les logs vers STDERR pour ne pas polluer STDOUT (utilisé par MCP)
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "simple";
}).SetMinimumLevel(LogLevel.Warning);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()        // Transport via STDIN/STDOUT (standard MCP)
    .WithToolsFromAssembly();           // Découverte auto des [McpServerToolType] dans l'assembly

await builder.Build().RunAsync();
