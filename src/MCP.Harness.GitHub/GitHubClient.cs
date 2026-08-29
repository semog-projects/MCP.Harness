namespace MCP.Harness.GitHub;

/// <summary>
/// Fachada fina sobre as APIs do GitHub usadas pelo harness. Agrupa os
/// clients REST (<see cref="Issues"/>) e GraphQL/Projects v2
/// (<see cref="Projects"/>) e oferece atalhos do ciclo de vida de task.
/// </summary>
public sealed class GitHubClient(IssuesClient issues, ProjectsV2Client projects)
{
    public IssuesClient Issues => issues;

    public ProjectsV2Client Projects => projects;

    /// <summary>
    /// Adiciona uma Issue ao board e define <c>Status</c> (e opcionalmente
    /// <c>Sprint</c> e <c>Story Points</c>) em uma tacada só.
    /// </summary>
    public async Task<ProjectItemRef> PlaceOnBoardAsync(
        HarnessProject project,
        IssueRef issue,
        string status,
        string? iterationId = null,
        double? storyPoints = null,
        CancellationToken ct = default)
    {
        var item = await projects.AddIssueAsync(project, issue.NodeId, ct);

        var statusField = project.Status;
        var option = statusField.FindOption(status)
            ?? throw new GitHubException(
                $"Status '{status}' inválido. Opções: {string.Join(", ", statusField.Options.Select(o => o.Name))}.");
        await projects.SetSingleSelectAsync(project, item, statusField, option.Id, ct);

        if (iterationId is not null)
        {
            await projects.SetIterationAsync(project, item, project.Sprint, iterationId, ct);
        }

        if (storyPoints is { } points)
        {
            await projects.SetNumberAsync(project, item, project.StoryPoints, points, ct);
        }

        return item;
    }
}
