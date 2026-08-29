using MCP.Harness.GitHub;

namespace MCP.Harness.Tools;

/// <summary>
/// Executa a operação de uma tool e transforma erros de domínio
/// (<see cref="GitHubException"/>, sempre acionáveis) em texto de resultado —
/// caso contrário o cliente MCP só veria uma mensagem genérica.
/// </summary>
internal static class ToolResult
{
    public static async Task<string> GuardAsync(Func<Task<string>> operation)
    {
        try
        {
            return await operation();
        }
        catch (GitHubException ex)
        {
            return $"❌ {ex.Message}";
        }
    }
}
