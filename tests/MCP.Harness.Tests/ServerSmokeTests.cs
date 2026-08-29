using System.Reflection;
using MCP.Harness.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCP.Harness.Tests;

public class ServerSmokeTests
{
    private static readonly Assembly ServerAssembly = typeof(DiagnosticsTools).Assembly;

    [Fact]
    public void McpServer_registration_builds_without_throwing()
    {
        var services = new ServiceCollection();

        services.AddMcpServer().WithToolsFromAssembly(ServerAssembly);

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<McpServerTool>().ToList();

        Assert.NotEmpty(tools);
        Assert.Contains(tools, t => t.ProtocolTool.Name == "harness_ping");
    }

    [Fact]
    public void Ping_tool_echoes_message()
    {
        var result = DiagnosticsTools.Ping("olá");

        Assert.StartsWith("pong @ ", result);
        Assert.EndsWith(": olá", result);
    }

    [Fact]
    public void Every_tool_type_is_discoverable_by_assembly_scan()
    {
        var toolTypes = ServerAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .ToList();

        Assert.NotEmpty(toolTypes);
        Assert.All(toolTypes, t => Assert.Contains(
            t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance),
            m => m.GetCustomAttribute<McpServerToolAttribute>() is not null));
    }
}
