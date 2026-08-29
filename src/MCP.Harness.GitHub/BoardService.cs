namespace MCP.Harness.GitHub;

/// <summary>
/// Passo 1 do skill: ler o board antes de criar qualquer coisa. Monta o
/// snapshot de uma sprint agrupado por <c>Status</c>.
/// </summary>
public sealed class BoardService(ProjectsV2Client projects)
{
    /// <summary>Rótulo usado quando o snapshot não filtra por sprint.</summary>
    public const string AllSprints = "(todas)";

    /// <param name="sprint">
    /// Título da sprint a filtrar. <c>null</c>/vazio = sprint corrente; se não
    /// houver corrente, cai para todos os itens.
    /// </param>
    public async Task<BoardSnapshot> GetBoardAsync(
        RepoRef repo, string? sprint = null, int? projectNumber = null, CancellationToken ct = default)
    {
        var project = await projects.ResolveProjectAsync(repo, projectNumber, ct);

        var target = string.IsNullOrWhiteSpace(sprint)
            ? project.Field("Sprint")?.CurrentIteration(DateOnly.FromDateTime(DateTime.UtcNow))?.Title
            : sprint.Trim();

        var all = await projects.ListItemsAsync(project, ct);
        var scoped = target is null
            ? all
            : all.Where(i => string.Equals(i.Sprint, target, StringComparison.OrdinalIgnoreCase)).ToList();

        // Colunas na ordem do campo Status; extras (valores fora do padrão) no fim.
        var order = project.Status.Options.Select(o => o.Name).ToList();
        var byStatus = scoped
            .GroupBy(i => i.Status ?? "(sem Status)")
            .OrderBy(g => order.IndexOf(g.Key) is var idx && idx >= 0 ? idx : int.MaxValue)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var columns = new List<BoardColumn>();
        foreach (var name in order)
        {
            var items = scoped.Where(i => i.Status == name)
                .OrderBy(i => i.Number).ToList();
            columns.Add(new BoardColumn(name, items));
        }

        foreach (var group in byStatus.Where(g => !order.Contains(g.Key)))
        {
            columns.Add(new BoardColumn(group.Key, group.OrderBy(i => i.Number).ToList()));
        }

        return new BoardSnapshot(project.Number, project.Url, target ?? AllSprints, columns);
    }
}
