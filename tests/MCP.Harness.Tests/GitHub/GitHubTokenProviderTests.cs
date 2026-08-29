using MCP.Harness.GitHub;
using Microsoft.Extensions.Options;

namespace MCP.Harness.Tests.GitHub;

/// <summary>
/// Testes que mexem em variáveis de ambiente do processo. Ficam na mesma
/// collection dos testes de integração para nunca rodarem em paralelo com
/// quem lê <c>GITHUB_TOKEN</c>.
/// </summary>
[CollectionDefinition("environment-mutation", DisableParallelization = true)]
public sealed class EnvironmentMutationCollection;

[Collection("environment-mutation")]
public class GitHubTokenProviderTests
{
    [Fact]
    public void Prefers_explicit_option_token()
    {
        var provider = new GitHubTokenProvider(Options.Create(new GitHubOptions { Token = "  pat-explicit  " }));

        Assert.Equal("pat-explicit", provider.GetToken());
    }

    [Fact]
    public void Falls_back_to_environment_variable()
    {
        using var _ = new EnvScope("GITHUB_TOKEN", "pat-from-env");
        var provider = new GitHubTokenProvider(Options.Create(new GitHubOptions { AllowGhCliTokenFallback = false }));

        Assert.Equal("pat-from-env", provider.GetToken());
    }

    [Fact]
    public void Throws_actionable_error_when_no_token_anywhere()
    {
        using var _ = new EnvScope("GITHUB_TOKEN", null);
        using var __ = new EnvScope("GH_TOKEN", null);
        var provider = new GitHubTokenProvider(Options.Create(new GitHubOptions { AllowGhCliTokenFallback = false }));

        var ex = Assert.Throws<GitHubAuthenticationException>(() => provider.GetToken());
        Assert.Contains("GITHUB_TOKEN", ex.Message);
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
