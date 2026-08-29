using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace MCP.Harness.GitHub;

/// <summary>
/// Injeta <c>Authorization</c>, <c>User-Agent</c> e <c>Accept</c> em toda
/// requisição feita pelos clients tipados.
/// </summary>
public sealed class GitHubAuthHandler(GitHubTokenProvider tokenProvider, IOptions<GitHubOptions> options)
    : DelegatingHandler
{
    private readonly GitHubOptions _options = options.Value;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization ??= new AuthenticationHeaderValue("Bearer", tokenProvider.GetToken());

        if (request.Headers.UserAgent.Count == 0)
        {
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        }

        if (!request.Headers.Accept.Any(a => a.MediaType == "application/vnd.github+json"))
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
        }

        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        return await base.SendAsync(request, cancellationToken);
    }
}
