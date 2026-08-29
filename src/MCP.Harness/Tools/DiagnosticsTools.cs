using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MCP.Harness.Tools;

/// <summary>
/// Ferramentas de diagnóstico. Servem de "hello world" do servidor e de
/// checagem de conectividade enquanto as tools do harness (bootstrap,
/// create_task, move_task, complete_task, board) não estão implementadas.
/// </summary>
[McpServerToolType]
public static class DiagnosticsTools
{
    [McpServerTool(Name = "harness_ping")]
    [Description("Verifica se o servidor MCP.Harness está no ar. Retorna um eco com a hora UTC.")]
    public static string Ping(
        [Description("Texto opcional a ser ecoado de volta.")] string? message = null)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("O");
        return string.IsNullOrWhiteSpace(message)
            ? $"pong @ {stamp}"
            : $"pong @ {stamp}: {message}";
    }
}
