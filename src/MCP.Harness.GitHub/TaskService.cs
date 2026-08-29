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

/// <summary>Resultado de <see cref="TaskService.MoveTaskAsync"/>.</summary>
public sealed record MoveTaskOutcome(
    HarnessProject Project, ProjectItemRef Item, int IssueNumber, string FromStatus, string ToStatus);

/// <summary>
/// Passos 2 e 3 do ciclo de vida do harness: criar a task e mover o
/// <c>Status</c> conforme o trabalho progride.
/// </summary>
public sealed class TaskService(GitHubClient github, ProjectsV2Client projects, IssuesClient issues)
{
    /// <summary>
    /// Move o <c>Status</c> da task da Issue <paramref name="issueNumber"/>.
    /// Não fecha a Issue quando o alvo é <c>Done</c> — isso é
    /// <c>harness_complete_task</c>.
    /// </summary>
    public async Task<MoveTaskOutcome> MoveTaskAsync(
        RepoRef repo, int issueNumber, string status, int? projectNumber = null, CancellationToken ct = default)
    {
        var project = await projects.ResolveProjectAsync(repo, projectNumber, ct);
        var statusField = project.Status;

        var option = statusField.FindOption(status?.Trim() ?? string.Empty)
            ?? throw new GitHubException(
                $"Status '{status}' inválido. Opções: {string.Join(", ", statusField.Options.Select(o => o.Name))}.");

        var item = await projects.FindItemByIssueAsync(project, repo, issueNumber, ct)
            ?? throw new GitHubException(
                $"Issue #{issueNumber} não está no board (Project #{project.Number}). Rode harness_create_task primeiro.");

        var from = await projects.GetSingleSelectValueAsync(item, statusField.Name, ct) ?? "—";
        await projects.SetSingleSelectAsync(project, item, statusField, option.Id, ct);

        return new MoveTaskOutcome(project, item, issueNumber, from, option.Name);
    }

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
