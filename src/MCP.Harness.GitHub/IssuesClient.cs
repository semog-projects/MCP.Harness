using System.Net.Http.Json;
using System.Text.Json;

namespace MCP.Harness.GitHub;

/// <summary>Operações de Issue via API REST do GitHub.</summary>
public sealed class IssuesClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <param name="type">
    /// Nome do issue type (ex.: <c>Task</c>, <c>Bug</c>, <c>Feature</c>), quando
    /// habilitado no repositório/organização.
    /// </param>
    public async Task<IssueRef> CreateAsync(
        RepoRef repo, string title, string body, string? type = null,
        IReadOnlyList<string>? labels = null, IReadOnlyList<string>? assignees = null,
        CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["body"] = body,
        };
        if (!string.IsNullOrWhiteSpace(type))
        {
            payload["type"] = type;
        }
        if (labels is { Count: > 0 })
        {
            payload["labels"] = labels;
        }
        if (assignees is { Count: > 0 })
        {
            payload["assignees"] = assignees;
        }

        using var response = await http.PostAsJsonAsync(
            $"repos/{repo.Owner}/{repo.Repo}/issues", payload, JsonOptions, ct);
        await GitHubResponse.EnsureSuccessAsync(response, $"criar Issue em {repo}", ct);

        return await ReadIssueAsync(response, ct);
    }

    /// <summary>
    /// Adiciona assignees a uma Issue. Idempotente — quem já está assinado é
    /// ignorado pelo GitHub. Logins inválidos são silenciosamente descartados.
    /// </summary>
    public async Task<IssueRef> AddAssigneesAsync(
        RepoRef repo, int number, IReadOnlyList<string> assignees, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(
            $"repos/{repo.Owner}/{repo.Repo}/issues/{number}/assignees",
            new { assignees }, JsonOptions, ct);
        await GitHubResponse.EnsureSuccessAsync(response, $"assinar a Issue #{number} em {repo}", ct);

        return await ReadIssueAsync(response, ct);
    }

    /// <summary>Nº máximo de páginas (de 100) varridas por <see cref="FindOpenByExactTitleAsync"/>.</summary>
    private const int MaxTitleScanPages = 5;

    /// <summary>
    /// Procura uma Issue <b>aberta</b> cujo título seja exatamente
    /// <paramref name="title"/> (ignorando caixa e espaços nas pontas). Serve
    /// de guarda contra duplicatas ao criar tasks.
    /// <para>
    /// Usa a listagem REST (dados ao vivo), não a busca — o índice de busca do
    /// GitHub é eventualmente consistente e não enxergaria uma Issue criada há
    /// segundos, furando a dedup em chamadas seguidas.
    /// </para>
    /// </summary>
    public async Task<IssueRef?> FindOpenByExactTitleAsync(
        RepoRef repo, string title, CancellationToken ct = default)
    {
        var wanted = title.Trim();

        for (var page = 1; page <= MaxTitleScanPages; page++)
        {
            using var response = await http.GetAsync(
                $"repos/{repo.Owner}/{repo.Repo}/issues?state=open&per_page=100&page={page}", ct);
            await GitHubResponse.EnsureSuccessAsync(response, $"listar Issues de {repo}", ct);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var items = doc.RootElement;
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            {
                return null;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("pull_request", out _))
                {
                    continue;
                }

                if (string.Equals(item.GetProperty("title").GetString()?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return ReadIssue(item);
                }
            }

            if (items.GetArrayLength() < 100)
            {
                return null;
            }
        }

        return null;
    }

    public async Task<IssueRef> GetAsync(RepoRef repo, int number, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(
            $"repos/{repo.Owner}/{repo.Repo}/issues/{number}", ct);
        await GitHubResponse.EnsureSuccessAsync(response, $"ler Issue #{number} em {repo}", ct);

        return await ReadIssueAsync(response, ct);
    }

    public async Task AddCommentAsync(RepoRef repo, int number, string body, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(
            $"repos/{repo.Owner}/{repo.Repo}/issues/{number}/comments", new { body }, JsonOptions, ct);
        await GitHubResponse.EnsureSuccessAsync(response, $"comentar na Issue #{number} em {repo}", ct);
    }

    /// <param name="stateReason"><c>completed</c>, <c>not_planned</c> ou <c>duplicate</c>.</param>
    public async Task<IssueRef> CloseAsync(
        RepoRef repo, int number, string stateReason = "completed", CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch, $"repos/{repo.Owner}/{repo.Repo}/issues/{number}")
        {
            Content = JsonContent.Create(new { state = "closed", state_reason = stateReason }, options: JsonOptions),
        };

        using var response = await http.SendAsync(request, ct);
        await GitHubResponse.EnsureSuccessAsync(response, $"fechar Issue #{number} em {repo}", ct);

        return await ReadIssueAsync(response, ct);
    }

    private static async Task<IssueRef> ReadIssueAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ReadIssue(doc.RootElement);
    }

    private static IssueRef ReadIssue(JsonElement issue) => new(
        Number: issue.GetProperty("number").GetInt32(),
        NodeId: issue.GetProperty("node_id").GetString() ?? throw MissingField("node_id"),
        Url: issue.GetProperty("html_url").GetString() ?? throw MissingField("html_url"),
        State: issue.GetProperty("state").GetString() ?? "unknown",
        Title: issue.GetProperty("title").GetString() ?? string.Empty,
        Assignees: issue.TryGetProperty("assignees", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray()
                .Select(x => x.TryGetProperty("login", out var l) ? l.GetString() ?? string.Empty : string.Empty)
                .Where(login => login.Length > 0).ToList()
            : []);

    private static GitHubException MissingField(string name) =>
        new($"Resposta do GitHub sem o campo esperado '{name}'.");
}
