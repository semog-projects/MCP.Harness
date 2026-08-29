using System.Net.Http.Json;
using System.Text.Json;

namespace MCP.Harness.GitHub;

/// <summary>Executor fino de GraphQL sobre o endpoint do GitHub.</summary>
public sealed class GraphQlClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public HttpClient Http => http;

    /// <summary>
    /// Executa <paramref name="query"/> e devolve o nó <c>data</c> como
    /// <see cref="JsonElement"/>. Lança <see cref="GitHubGraphQlException"/>
    /// se a resposta trouxer <c>errors</c>.
    /// </summary>
    public async Task<JsonElement> ExecuteAsync(
        string query, object? variables, string operation, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            (Uri?)null, new { query, variables }, JsonOptions, ct);

        await GitHubResponse.EnsureSuccessAsync(response, operation, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var messages = errors.EnumerateArray()
                .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() ?? "erro" : "erro")
                .ToArray();

            throw new GitHubGraphQlException(
                $"{operation}: GraphQL retornou erro — {string.Join("; ", messages)}", messages);
        }

        return root.TryGetProperty("data", out var data)
            ? data.Clone()
            : throw new GitHubGraphQlException($"{operation}: resposta GraphQL sem 'data'.", []);
    }
}
