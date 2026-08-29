using System.Net;

namespace MCP.Harness.GitHub;

/// <summary>Base de todas as falhas ao falar com o GitHub.</summary>
public class GitHubException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Nenhum token utilizável foi encontrado.</summary>
public sealed class GitHubAuthenticationException(string message) : GitHubException(message);

/// <summary>
/// A API respondeu com erro. <see cref="Actionable"/> traz uma mensagem já
/// pronta para ser mostrada ao usuário (token sem escopo, rate limit, etc.).
/// </summary>
public sealed class GitHubApiException(
    HttpStatusCode statusCode,
    string actionable,
    string? rawBody = null,
    Exception? inner = null)
    : GitHubException(actionable, inner)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string Actionable { get; } = actionable;

    public string? RawBody { get; } = rawBody;

    public bool IsRateLimit { get; init; }

    public bool IsPermission { get; init; }
}

/// <summary>Erro reportado pela camada GraphQL (HTTP 200 com <c>errors</c>).</summary>
public sealed class GitHubGraphQlException(string message, IReadOnlyList<string> errors)
    : GitHubException(message)
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
