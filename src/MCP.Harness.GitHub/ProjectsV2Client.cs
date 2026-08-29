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
        var nodes = await ListLinkedProjectsAsync(repo, ct);

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

    /// <summary>Lista os Projects v2 vinculados a um repositório (com campos).</summary>
    public async Task<IReadOnlyList<HarnessProject>> ListLinkedProjectsAsync(
        RepoRef repo, CancellationToken ct = default)
    {
        const string query = $$"""
            query($owner: String!, $repo: String!) {
              repository(owner: $owner, name: $repo) {
                projectsV2(first: 20) { nodes { {{ProjectFragment}} } }
              }
            }
            """;

        var data = await graphQl.ExecuteAsync(
            query, new { owner = repo.Owner, repo = repo.Repo }, $"listar Projects de {repo}", ct);

        var repository = data.GetProperty("repository");
        if (repository.ValueKind == JsonValueKind.Null)
        {
            throw new GitHubException($"Repositório {repo} não encontrado (ou token sem acesso).");
        }

        return repository.GetProperty("projectsV2").GetProperty("nodes")
            .EnumerateArray().Select(ParseProject).ToList();
    }

    /// <summary>Resolve o node id de um owner (organização ou usuário).</summary>
    public async Task<string> ResolveOwnerIdAsync(string login, CancellationToken ct = default)
    {
        const string query = """
            query($login: String!) {
              organization(login: $login) { id }
              user(login: $login) { id }
            }
            """;

        var data = await graphQl.ExecuteAsync(query, new { login }, $"resolver owner '{login}'", ct);
        return NonNullId(data, "organization", "user")
            ?? throw new GitHubException($"Owner '{login}' não encontrado (organização ou usuário).");
    }

    /// <summary>Node id de um repositório.</summary>
    public async Task<string> ResolveRepositoryIdAsync(RepoRef repo, CancellationToken ct = default)
    {
        const string query = """
            query($owner: String!, $repo: String!) {
              repository(owner: $owner, name: $repo) { id }
            }
            """;

        var data = await graphQl.ExecuteAsync(
            query, new { owner = repo.Owner, repo = repo.Repo }, $"resolver repositório {repo}", ct);

        var repository = data.GetProperty("repository");
        return repository.ValueKind != JsonValueKind.Null
            ? repository.GetProperty("id").GetString()!
            : throw new GitHubException($"Repositório {repo} não encontrado (ou token sem acesso).");
    }

    /// <summary>Resolve um Project v2 pelo owner + número, já com os campos.</summary>
    public async Task<HarnessProject> GetProjectByNumberAsync(
        string login, int number, CancellationToken ct = default)
    {
        const string query = $$"""
            query($login: String!, $number: Int!) {
              organization(login: $login) { projectV2(number: $number) { {{ProjectFragment}} } }
              user(login: $login) { projectV2(number: $number) { {{ProjectFragment}} } }
            }
            """;

        var data = await graphQl.ExecuteAsync(
            query, new { login, number }, $"resolver Project #{number} de '{login}'", ct);

        foreach (var prop in (ReadOnlySpan<string>)["organization", "user"])
        {
            if (data.TryGetProperty(prop, out var owner) && owner.ValueKind == JsonValueKind.Object
                && owner.TryGetProperty("projectV2", out var project) && project.ValueKind == JsonValueKind.Object)
            {
                return ParseProject(project);
            }
        }

        throw new GitHubException($"Project #{number} não encontrado em '{login}'.");
    }

    /// <summary>Copia um Project v2 (template) para outro owner. Mutation <c>copyProjectV2</c>.</summary>
    public async Task<CopiedProject> CopyProjectAsync(
        string sourceProjectId, string targetOwnerId, string title, CancellationToken ct = default)
    {
        const string mutation = """
            mutation($projectId: ID!, $ownerId: ID!, $title: String!) {
              copyProjectV2(input: {
                projectId: $projectId, ownerId: $ownerId, title: $title, includeDraftIssues: false
              }) {
                projectV2 { id number title url }
              }
            }
            """;

        var data = await graphQl.ExecuteAsync(
            mutation, new { projectId = sourceProjectId, ownerId = targetOwnerId, title },
            "copiar o Project template", ct);

        var p = data.GetProperty("copyProjectV2").GetProperty("projectV2");
        return new CopiedProject(
            p.GetProperty("id").GetString()!,
            p.GetProperty("number").GetInt32(),
            p.GetProperty("title").GetString()!,
            p.GetProperty("url").GetString()!);
    }

    /// <summary>Vincula um Project v2 a um repositório. Mutation <c>linkProjectV2ToRepository</c>.</summary>
    public async Task LinkRepositoryAsync(string projectId, string repositoryId, CancellationToken ct = default)
    {
        const string mutation = """
            mutation($projectId: ID!, $repositoryId: ID!) {
              linkProjectV2ToRepository(input: { projectId: $projectId, repositoryId: $repositoryId }) {
                repository { id }
              }
            }
            """;

        await graphQl.ExecuteAsync(
            mutation, new { projectId, repositoryId }, "vincular o Project ao repositório", ct);
    }

    private static string? NonNullId(JsonElement data, params string[] props)
    {
        foreach (var prop in props)
        {
            if (data.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Object
                && el.TryGetProperty("id", out var id))
            {
                return id.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Acha o item que representa a Issue <paramref name="issueNumber"/> no
    /// board <paramref name="project"/>, ou <c>null</c> se ela não estiver lá.
    /// </summary>
    public async Task<ProjectItemRef?> FindItemByIssueAsync(
        HarnessProject project, RepoRef repo, int issueNumber, CancellationToken ct = default)
    {
        const string query = """
            query($owner: String!, $repo: String!, $number: Int!) {
              repository(owner: $owner, name: $repo) {
                issue(number: $number) {
                  projectItems(first: 20, includeArchived: true) {
                    nodes { id project { id } }
                  }
                }
              }
            }
            """;

        var data = await graphQl.ExecuteAsync(
            query, new { owner = repo.Owner, repo = repo.Repo, number = issueNumber },
            $"localizar item da Issue #{issueNumber} no Project #{project.Number}", ct);

        var repository = data.GetProperty("repository");
        if (repository.ValueKind == JsonValueKind.Null
            || repository.GetProperty("issue").ValueKind == JsonValueKind.Null)
        {
            throw new GitHubException($"Issue #{issueNumber} não encontrada em {repo}.");
        }

        foreach (var node in repository.GetProperty("issue").GetProperty("projectItems").GetProperty("nodes").EnumerateArray())
        {
            if (node.GetProperty("project").GetProperty("id").GetString() == project.Id)
            {
                return new ProjectItemRef(node.GetProperty("id").GetString()!);
            }
        }

        return null;
    }

    /// <summary>Lista todos os itens (Issues) do board, com Status / Sprint / Story Points.</summary>
    public async Task<IReadOnlyList<BoardItem>> ListItemsAsync(HarnessProject project, CancellationToken ct = default)
    {
        const string query = """
            query($id: ID!, $after: String) {
              node(id: $id) {
                ... on ProjectV2 {
                  items(first: 100, after: $after) {
                    pageInfo { hasNextPage endCursor }
                    nodes {
                      content {
                        __typename
                        ... on Issue {
                          number title url state
                          assignees(first: 10) { nodes { login } }
                        }
                      }
                      status: fieldValueByName(name: "Status") {
                        ... on ProjectV2ItemFieldSingleSelectValue { name }
                      }
                      sprint: fieldValueByName(name: "Sprint") {
                        ... on ProjectV2ItemFieldIterationValue { title }
                      }
                      points: fieldValueByName(name: "Story Points") {
                        ... on ProjectV2ItemFieldNumberValue { number }
                      }
                    }
                  }
                }
              }
            }
            """;

        var items = new List<BoardItem>();
        string? after = null;

        do
        {
            var data = await graphQl.ExecuteAsync(
                query, new { id = project.Id, after }, $"listar itens do Project #{project.Number}", ct);

            var page = data.GetProperty("node").GetProperty("items");
            foreach (var node in page.GetProperty("nodes").EnumerateArray())
            {
                var content = node.GetProperty("content");
                if (content.ValueKind != JsonValueKind.Object
                    || content.GetProperty("__typename").GetString() != "Issue")
                {
                    continue;
                }

                items.Add(new BoardItem(
                    Number: content.GetProperty("number").GetInt32(),
                    Title: content.GetProperty("title").GetString() ?? string.Empty,
                    Url: content.GetProperty("url").GetString() ?? string.Empty,
                    State: content.GetProperty("state").GetString()?.ToLowerInvariant() ?? "unknown",
                    Status: TextOf(node, "status", "name"),
                    Sprint: TextOf(node, "sprint", "title"),
                    StoryPoints: NumberOf(node, "points"),
                    Assignees: content.GetProperty("assignees").GetProperty("nodes").EnumerateArray()
                        .Select(a => a.GetProperty("login").GetString() ?? string.Empty)
                        .Where(login => login.Length > 0).ToList()));
            }

            var pageInfo = page.GetProperty("pageInfo");
            after = pageInfo.GetProperty("hasNextPage").GetBoolean()
                ? pageInfo.GetProperty("endCursor").GetString()
                : null;
        }
        while (after is not null);

        return items;
    }

    private static string? TextOf(JsonElement node, string property, string field)
        => node.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Object
           && el.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? NumberOf(JsonElement node, string property)
        => node.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Object
           && el.TryGetProperty("number", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

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

    /// <summary>Lê o valor atual de um campo single-select do item, ou <c>null</c> se não definido.</summary>
    public async Task<string?> GetSingleSelectValueAsync(
        ProjectItemRef item, string fieldName, CancellationToken ct = default)
    {
        const string query = """
            query($id: ID!, $field: String!) {
              node(id: $id) {
                ... on ProjectV2Item {
                  fieldValueByName(name: $field) {
                    ... on ProjectV2ItemFieldSingleSelectValue { name }
                  }
                }
              }
            }
            """;

        var data = await graphQl.ExecuteAsync(
            query, new { id = item.Id, field = fieldName }, $"ler campo '{fieldName}' do item", ct);

        var node = data.GetProperty("node");
        if (node.ValueKind == JsonValueKind.Null || !node.TryGetProperty("fieldValueByName", out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.TryGetProperty("name", out var name) ? name.GetString() : null;
    }

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
