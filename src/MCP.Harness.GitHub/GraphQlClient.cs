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
    /// <param name="allowPartialErrors">
    /// Quando <c>true</c>, uma resposta com <c>data</c> presente cujos
    /// <c>errors</c> são <b>todos</b> <c>NOT_FOUND</c> não lança — o <c>data</c>
    /// é devolvido com os campos não resolvidos em <c>null</c>. Serve para
    /// queries que sondam alternativas (ex.: <c>organization</c> OU <c>user</c>),
    /// onde o GitHub reporta a alternativa inexistente como erro parcial.
    /// </param>
    public async Task<JsonElement> ExecuteAsync(
        string query, object? variables, string operation, CancellationToken ct,
        bool allowPartialErrors = false)
    {
        using var response = await http.PostAsJsonAsync(
            (Uri?)null, new { query, variables }, JsonOptions, ct);

        await GitHubResponse.EnsureSuccessAsync(response, operation, ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var hasData = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object;

        if (root.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var allNotFound = errors.EnumerateArray().All(e =>
                e.TryGetProperty("type", out var t) && t.GetString() == "NOT_FOUND");

            if (!(allowPartialErrors && hasData && allNotFound))
            {
                var messages = errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() ?? "erro" : "erro")
                    .ToArray();

                throw new GitHubGraphQlException(
                    $"{operation}: GraphQL retornou erro — {string.Join("; ", messages)}", messages);
            }
        }

        return hasData
            ? data.Clone()
            : throw new GitHubGraphQlException($"{operation}: resposta GraphQL sem 'data'.", []);
    }
}
