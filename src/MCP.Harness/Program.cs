using MCP.Harness.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Host do servidor MCP.Harness.
//
// Transporte: stdio (padrão para clientes como o Claude Code). Todo log vai
// para stderr — stdout é reservado ao protocolo MCP (JSON-RPC).
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddHarnessGitHub(builder.Configuration);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly();

await builder.Build().RunAsync();
