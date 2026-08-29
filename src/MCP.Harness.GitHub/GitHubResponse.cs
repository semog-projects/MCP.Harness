using System.Net;
using System.Text.Json;

namespace MCP.Harness.GitHub;

/// <summary>
/// Traduz respostas HTTP de erro do GitHub em <see cref="GitHubApiException"/>
/// com mensagens acionáveis (escopo de token, rate limit, permissão).
/// </summary>
internal static class GitHubResponse
{
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await SafeReadAsync(response, ct);
        var apiMessage = TryExtractMessage(body);
        var status = response.StatusCode;

        var remaining = FirstHeader(response, "x-ratelimit-remaining");
        var isPrimaryRateLimit = (status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            && remaining == "0";
        var isSecondaryRateLimit = status is HttpStatusCode.Forbidden
            && apiMessage?.Contains("secondary rate limit", StringComparison.OrdinalIgnoreCase) == true;

        if (isPrimaryRateLimit || isSecondaryRateLimit)
        {
            var reset = FirstHeader(response, "x-ratelimit-reset");
            var when = long.TryParse(reset, out var epoch)
                ? $" Tente de novo após {DateTimeOffset.FromUnixTimeSeconds(epoch):HH:mm:ss} UTC."
                : string.Empty;

            throw new GitHubApiException(status,
                $"{operation}: rate limit do GitHub atingido.{when}", body)
            {
                IsRateLimit = true,
            };
        }

        if (status is HttpStatusCode.Unauthorized)
        {
            throw new GitHubApiException(status,
                $"{operation}: token do GitHub inválido ou expirado. Gere um novo PAT.", body);
        }

        if (status is HttpStatusCode.Forbidden)
        {
            throw new GitHubApiException(status,
                $"{operation}: token sem permissão. Confira os escopos 'repo', 'project' e 'read:org'" +
                (apiMessage is null ? "." : $" (GitHub: {apiMessage})."), body)
            {
                IsPermission = true,
            };
        }

        if (status is HttpStatusCode.NotFound)
        {
            throw new GitHubApiException(status,
                $"{operation}: recurso não encontrado. Confira owner/repo e se o token enxerga o repositório.", body);
        }

        throw new GitHubApiException(status,
            $"{operation}: HTTP {(int)status} {status}" + (apiMessage is null ? "." : $" — {apiMessage}"), body);
    }

    public static string? TryExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static async Task<string?> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return null;
        }
    }
}
