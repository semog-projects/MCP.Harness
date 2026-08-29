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
    ProjectIteration? Sprint,
    IReadOnlyList<string> Assignees);

/// <summary>Resultado de <see cref="TaskService.MoveTaskAsync"/>.</summary>
public sealed record MoveTaskOutcome(
    HarnessProject Project, ProjectItemRef Item, int IssueNumber, string FromStatus, string ToStatus);

/// <summary>Resultado de <see cref="TaskService.CompleteTaskAsync"/>.</summary>
/// <param name="AlreadyCompleted">
/// <c>true</c> se a Issue já estava fechada quando a tool rodou (só garantiu
/// o <c>Status = Done</c>).
/// </param>
public sealed record CompleteTaskOutcome(
    HarnessProject Project, ProjectItemRef Item, IssueRef Issue, bool AlreadyCompleted, bool Commented);

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

    /// <summary>
    /// Passo 4: conclui a task — <c>Status = Done</c> e fecha a Issue
    /// (<c>state_reason = completed</c>). Idempotente: se a Issue já estiver
    /// fechada, só garante o <c>Status = Done</c> e não recomenta nem
    /// re-fecha.
    /// </summary>
    public async Task<CompleteTaskOutcome> CompleteTaskAsync(
        RepoRef repo, int issueNumber, string? comment = null, int? projectNumber = null, CancellationToken ct = default)
    {
        var project = await projects.ResolveProjectAsync(repo, projectNumber, ct);
        var doneOption = project.Status.FindOption("Done")
            ?? throw new GitHubException(
                $"O campo 'Status' do Project #{project.Number} não tem a opção 'Done'.");

        var item = await projects.FindItemByIssueAsync(project, repo, issueNumber, ct)
            ?? throw new GitHubException(
                $"Issue #{issueNumber} não está no board (Project #{project.Number}). Rode harness_create_task primeiro.");

        var issue = await issues.GetAsync(repo, issueNumber, ct);
        var alreadyClosed = string.Equals(issue.State, "closed", StringComparison.OrdinalIgnoreCase);

        // Status = Done sempre (barato e idempotente).
        await projects.SetSingleSelectAsync(project, item, project.Status, doneOption.Id, ct);

        if (alreadyClosed)
        {
            return new CompleteTaskOutcome(project, item, issue, AlreadyCompleted: true, Commented: false);
        }

        var commented = false;
        if (!string.IsNullOrWhiteSpace(comment))
        {
            await issues.AddCommentAsync(repo, issueNumber, comment.Trim(), ct);
            commented = true;
        }

        var closed = await issues.CloseAsync(repo, issueNumber, "completed", ct);
        return new CompleteTaskOutcome(project, item, closed, AlreadyCompleted: false, commented);
    }

    /// <param name="assignees">
    /// Logins a assinar na Issue. <c>null</c>/vazio → o usuário do token. Passe
    /// uma lista vazia via <see cref="Array.Empty{T}"/> só se quiser mesmo sem
    /// ninguém — mas o board espera Assignees preenchido.
    /// </param>
    public async Task<CreateTaskOutcome> CreateTaskAsync(
        RepoRef repo,
        string title,
        string body,
        string? type = null,
        double? storyPoints = null,
        int? projectNumber = null,
        IReadOnlyList<string>? assignees = null,
        CancellationToken ct = default)
    {
        title = title.Trim();
        if (string.IsNullOrEmpty(title))
        {
            throw new GitHubException("O título da task não pode ser vazio.");
        }

        var project = await projects.ResolveProjectAsync(repo, projectNumber, ct);
        var sprint = project.Field("Sprint")?.CurrentIteration(DateOnly.FromDateTime(DateTime.UtcNow));

        var explicitAssignees = assignees is { Count: > 0 }
            ? assignees.Select(a => a.Trim().TrimStart('@')).Where(a => a.Length > 0).Distinct().ToList()
            : null;

        // Só resolve o usuário do token se for mesmo preciso assinar.
        async Task<IReadOnlyList<string>> WantedAssignees() =>
            explicitAssignees ?? [await projects.GetViewerLoginAsync(ct)];

        var existing = await issues.FindOpenByExactTitleAsync(repo, title, ct);
        if (existing is not null)
        {
            // Já existe task equivalente. Não duplica; garante só que está no
            // board (sem mexer no Status de uma que já esteja em andamento).
            var item = await projects.FindItemByIssueAsync(project, repo, existing.Number, ct)
                       ?? await github.PlaceOnBoardAsync(project, existing, "Backlog", sprint?.Id, storyPoints, ct);

            // Só assina se ninguém estiver assinado — respeita atribuições manuais.
            var final = existing.Assignees.Count == 0
                ? await issues.AddAssigneesAsync(repo, existing.Number, await WantedAssignees(), ct)
                : existing;

            return new CreateTaskOutcome(Created: false, final, item, project, sprint, final.Assignees);
        }

        var issue = await issues.CreateAsync(
            repo, title, body, type, assignees: await WantedAssignees(), ct: ct);
        var newItem = await github.PlaceOnBoardAsync(project, issue, "Backlog", sprint?.Id, storyPoints, ct);

        return new CreateTaskOutcome(Created: true, issue, newItem, project, sprint, issue.Assignees);
    }
}
