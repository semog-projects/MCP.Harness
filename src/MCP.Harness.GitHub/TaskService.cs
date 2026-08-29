namespace MCP.Harness.GitHub;

/// <summary>Resultado de <see cref="TaskService.CreateTaskAsync"/>.</summary>
/// <param name="Created">
/// <c>true</c> se a Issue foi criada agora; <c>false</c> se já existia uma
/// aberta com o mesmo título (dedup).
/// </param>
public sealed record CreateTaskOutcome(
    bool Created,
    IssueRef Issue,
    ProjectItemRef Item,
    HarnessProject Project,
    ProjectIteration? Sprint);

/// <summary>
/// Passo 2 do ciclo de vida do harness: transformar um pedido de trabalho em
/// Issue rastreada no board (<c>Status = Backlog</c>, sprint corrente).
/// </summary>
public sealed class TaskService(GitHubClient github, ProjectsV2Client projects, IssuesClient issues)
{
    public async Task<CreateTaskOutcome> CreateTaskAsync(
        RepoRef repo,
        string title,
        string body,
        string? type = null,
        double? storyPoints = null,
        int? projectNumber = null,
        CancellationToken ct = default)
    {
        title = title.Trim();
        if (string.IsNullOrEmpty(title))
        {
            throw new GitHubException("O título da task não pode ser vazio.");
        }

        var project = await projects.ResolveProjectAsync(repo, projectNumber, ct);
        var sprint = project.Field("Sprint")?.CurrentIteration(DateOnly.FromDateTime(DateTime.UtcNow));

        var existing = await issues.FindOpenByExactTitleAsync(repo, title, ct);
        if (existing is not null)
        {
            // Já existe task equivalente. Não duplica; garante só que está no
            // board (sem mexer no Status de uma que já esteja em andamento).
            var item = await projects.FindItemByIssueAsync(project, repo, existing.Number, ct)
                       ?? await github.PlaceOnBoardAsync(project, existing, "Backlog", sprint?.Id, storyPoints, ct);

            return new CreateTaskOutcome(Created: false, existing, item, project, sprint);
        }

        var issue = await issues.CreateAsync(repo, title, body, type, ct: ct);
        var newItem = await github.PlaceOnBoardAsync(project, issue, "Backlog", sprint?.Id, storyPoints, ct);

        return new CreateTaskOutcome(Created: true, issue, newItem, project, sprint);
    }
}
