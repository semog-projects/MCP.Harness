using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace MCP.Harness.GitHub;

/// <summary>
/// Resolve o token de acesso ao GitHub, em ordem:
/// <list type="number">
///   <item><see cref="GitHubOptions.Token"/> (config explícita);</item>
///   <item>variáveis de ambiente <c>GITHUB_TOKEN</c> / <c>GH_TOKEN</c>;</item>
///   <item><c>gh auth token</c>, se habilitado e o binário existir.</item>
/// </list>
/// O valor é resolvido uma vez e cacheado pelo tempo de vida do provider.
/// </summary>
public sealed class GitHubTokenProvider(IOptions<GitHubOptions> options)
{
    private readonly GitHubOptions _options = options.Value;
    private string? _cached;

    public string GetToken()
    {
        if (_cached is { Length: > 0 })
        {
            return _cached;
        }

        _cached = ResolveToken();
        return _cached;
    }

    private string ResolveToken()
    {
        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            return _options.Token.Trim();
        }

        foreach (var name in (ReadOnlySpan<string>)["GITHUB_TOKEN", "GH_TOKEN"])
        {
            var fromEnv = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return fromEnv.Trim();
            }
        }

        if (_options.AllowGhCliTokenFallback && TryReadGhCliToken(out var fromGh))
        {
            return fromGh;
        }

        throw new GitHubAuthenticationException(
            "Nenhum token do GitHub encontrado. Defina GITHUB_TOKEN (PAT com escopos " +
            "'repo', 'project', 'read:org') ou rode 'gh auth login'.");
    }

    private static bool TryReadGhCliToken(out string token)
    {
        token = string.Empty;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("gh", "auth token")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5_000);

            if (process.ExitCode == 0 && output.Length > 0)
            {
                token = output;
                return true;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // gh não está instalado / não está no PATH — segue sem token.
        }

        return false;
    }
}
