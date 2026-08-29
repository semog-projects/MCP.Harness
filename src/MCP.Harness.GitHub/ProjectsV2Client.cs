using System.Text.Json;

namespace MCP.Harness.GitHub;

/// <summary>
/// Operações de GitHub Projects (v2) via GraphQL: resolver o board de um
/// repositório, adicionar itens e atualizar valores de campo.
/// </summary>
public sealed class ProjectsV2Client(GraphQlClient graphQl)
{
    private const string ProjectFragment = """
        id
        number
        title
        url
        fields(first: 50) {
          nodes {
            __typename
            ... on ProjectV2FieldCommon { id name dataType }
            ... on ProjectV2SingleSelectField { options { id name } }
            ... on ProjectV2IterationField {
              configuration {
                iterations { id title startDate duration }
                completedIterations { id title startDate duration }
              }
            }
          }
        }
        """;

    /// <summary>
    /// Resolve o Project v2 vinculado a <paramref name="repo"/>. Se houver mais
    /// de um e <paramref name="projectNumber"/> for <c>null</c>, prefere o de
    /// título terminando em "Sprints"; se ainda assim houver ambiguidade, falha
    /// pedindo o número explícito.
    /// </summary>
    public async Task<HarnessProject> ResolveProjectAsync(
        RepoRef repo, int? projectNumber = null, CancellationToken ct = default)
    {
        const string query = $$"""
            query($owner: String!, $repo: String!) {
              repository(owner: $owner, name: $repo) {
                projectsV2(first: 20) { nodes { {{ProjectFragment}} } }
              }
            }
            """;

        var data = await graphQl.ExecuteAsync(
            query, new { owner = repo.Owner, repo = repo.Repo }, $"resolver Project de {repo}", ct);

        var repository = data.GetProperty("repository");
        if (repository.ValueKind == JsonValueKind.Null)
        {
            throw new GitHubException($"Repositório {repo} não encontrado (ou token sem acesso).");
        }

        var nodes = repository.GetProperty("projectsV2").GetProperty("nodes")
            .EnumerateArray().Select(ParseProject).ToList();

        if (nodes.Count == 0)
        {
            throw new GitHubException(
                $"Nenhum Project v2 vinculado a {repo}. Rode o bootstrap do harness primeiro.");
        }

        if (projectNumber is { } number)
        {
            return nodes.FirstOrDefault(p => p.Number == number)
                ?? throw new GitHubException($"Project #{number} não está vinculado a {repo}.");
        }

        if (nodes.Count == 1)
        {
            return nodes[0];
        }

        var sprintBoards = nodes
            .Where(p => p.Title.EndsWith("Sprints", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sprintBoards.Count == 1)
        {
            return sprintBoards[0];
        }

        var choices = string.Join(", ", nodes.Select(p => $"#{p.Number} \"{p.Title}\""));
        throw new GitHubException(
            $"{repo} tem {nodes.Count} Projects vinculados ({choices}). Informe o número do board.");
    }

    public async Task<ProjectItemRef> AddIssueAsync(
        HarnessProject project, string issueNodeId, CancellationToken ct = default)
    {
        const string mutation = """
            mutation($projectId: ID!, $contentId: ID!) {
              addProjectV2ItemById(input: { projectId: $projectId, contentId: $contentId }) {
                item { id }
              }
            }
            """;

        var data = await graphQl.ExecuteAsync(
            mutation, new { projectId = project.Id, contentId = issueNodeId },
            $"adicionar item ao Project #{project.Number}", ct);

        var id = data.GetProperty("addProjectV2ItemById").GetProperty("item").GetProperty("id").GetString()!;
        return new ProjectItemRef(id);
    }

    public async Task DeleteItemAsync(HarnessProject project, ProjectItemRef item, CancellationToken ct = default)
    {
        const string mutation = """
            mutation($projectId: ID!, $itemId: ID!) {
              deleteProjectV2Item(input: { projectId: $projectId, itemId: $itemId }) { deletedItemId }
            }
            """;

        await graphQl.ExecuteAsync(
            mutation, new { projectId = project.Id, itemId = item.Id },
            $"remover item do Project #{project.Number}", ct);
    }

    public Task SetSingleSelectAsync(
        HarnessProject project, ProjectItemRef item, ProjectField field, string optionId, CancellationToken ct = default)
        => UpdateFieldAsync(project, item, field, new { singleSelectOptionId = optionId }, ct);

    public Task SetIterationAsync(
        HarnessProject project, ProjectItemRef item, ProjectField field, string iterationId, CancellationToken ct = default)
        => UpdateFieldAsync(project, item, field, new { iterationId }, ct);

    public Task SetNumberAsync(
        HarnessProject project, ProjectItemRef item, ProjectField field, double number, CancellationToken ct = default)
        => UpdateFieldAsync(project, item, field, new { number }, ct);

    private async Task UpdateFieldAsync(
        HarnessProject project, ProjectItemRef item, ProjectField field, object value, CancellationToken ct)
    {
        const string mutation = """
            mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $value: ProjectV2FieldValue!) {
              updateProjectV2ItemFieldValue(input: {
                projectId: $projectId, itemId: $itemId, fieldId: $fieldId, value: $value
              }) {
                projectV2Item { id }
              }
            }
            """;

        await graphQl.ExecuteAsync(
            mutation,
            new { projectId = project.Id, itemId = item.Id, fieldId = field.Id, value },
            $"atualizar campo '{field.Name}' no Project #{project.Number}", ct);
    }

    private static HarnessProject ParseProject(JsonElement node)
    {
        var fields = node.GetProperty("fields").GetProperty("nodes")
            .EnumerateArray()
            .Where(f => f.ValueKind == JsonValueKind.Object && f.TryGetProperty("id", out _))
            .Select(ParseField)
            .ToList();

        return new HarnessProject(
            Id: node.GetProperty("id").GetString()!,
            Number: node.GetProperty("number").GetInt32(),
            Title: node.GetProperty("title").GetString() ?? string.Empty,
            Url: node.GetProperty("url").GetString() ?? string.Empty,
            Fields: fields);
    }

    private static ProjectField ParseField(JsonElement node)
    {
        var name = node.GetProperty("name").GetString() ?? string.Empty;
        var typeName = node.GetProperty("__typename").GetString();
        var dataType = node.TryGetProperty("dataType", out var dt) ? dt.GetString() : null;

        var options = node.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array
            ? opts.EnumerateArray()
                .Select(o => new ProjectFieldOption(o.GetProperty("id").GetString()!, o.GetProperty("name").GetString()!))
                .ToList()
            : [];

        var iterations = new List<ProjectIteration>();
        if (node.TryGetProperty("configuration", out var config) && config.ValueKind == JsonValueKind.Object)
        {
            AddIterations(config, "iterations", completed: false, iterations);
            AddIterations(config, "completedIterations", completed: true, iterations);
        }

        var type = typeName switch
        {
            "ProjectV2SingleSelectField" => ProjectFieldType.SingleSelect,
            "ProjectV2IterationField" => ProjectFieldType.Iteration,
            _ => dataType switch
            {
                "NUMBER" => ProjectFieldType.Number,
                "TEXT" => ProjectFieldType.Text,
                "DATE" => ProjectFieldType.Date,
                _ => ProjectFieldType.Other,
            },
        };

        return new ProjectField(node.GetProperty("id").GetString()!, name, type, options, iterations);
    }

    private static void AddIterations(JsonElement config, string property, bool completed, List<ProjectIteration> sink)
    {
        if (!config.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var it in arr.EnumerateArray())
        {
            DateOnly? start = it.TryGetProperty("startDate", out var s) && s.GetString() is { } raw
                && DateOnly.TryParse(raw, out var parsed) ? parsed : null;

            sink.Add(new ProjectIteration(
                Id: it.GetProperty("id").GetString()!,
                Title: it.GetProperty("title").GetString() ?? string.Empty,
                StartDate: start,
                DurationDays: it.TryGetProperty("duration", out var d) ? d.GetInt32() : 14,
                Completed: completed));
        }
    }
}
