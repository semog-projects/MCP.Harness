using MCP.Harness.GitHub;
using Microsoft.Extensions.Options;

namespace MCP.Harness.Tests.GitHub;

[Collection("environment-mutation")]
public class TokenSourceTests
{
    [Fact]
    public void DescribeSource_reports_config_without_leaking_the_value()
    {
        var provider = new GitHubTokenProvider(Options.Create(new GitHubOptions { Token = "super-secret" }));

        var source = provider.DescribeSource();

        Assert.Equal("config (GitHub:Token)", source);
        Assert.DoesNotContain("super-secret", source);
    }

    [Fact]
    public void DescribeSource_reports_env_var_name()
    {
        var previous = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "t");
        try
        {
            var provider = new GitHubTokenProvider(
                Options.Create(new GitHubOptions { AllowGhCliTokenFallback = false }));

            Assert.Equal("env GITHUB_TOKEN", provider.DescribeSource());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previous);
        }
    }

    [Fact]
    public void DescribeSource_is_nenhuma_when_no_token_anywhere()
    {
        var github = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var gh = Environment.GetEnvironmentVariable("GH_TOKEN");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        Environment.SetEnvironmentVariable("GH_TOKEN", null);
        try
        {
            var provider = new GitHubTokenProvider(
                Options.Create(new GitHubOptions { AllowGhCliTokenFallback = false }));

            Assert.Equal("nenhuma", provider.DescribeSource());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", github);
            Environment.SetEnvironmentVariable("GH_TOKEN", gh);
        }
    }
}
