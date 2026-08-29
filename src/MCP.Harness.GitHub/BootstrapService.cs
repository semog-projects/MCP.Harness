using Microsoft.Extensions.Options;

namespace MCP.Harness.GitHub;

/// <summary>Resultado de um bootstrap de board.</summary>
/// <param name="Created">
/// <c>true</c> se o Project foi criado agora; <c>false</c> se já existia
/// vinculado (operação idempotente).
/// </param>
public sealed record BootstrapResult(
    bool Created,
    HarnessProject Project,
    ProjectIteration? CurrentSprint,
    IReadOnlyList<ProjectIteration> Sprints,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Cria o board de um repositório copiando o Project-template padronizado
/// (campos <c>Status</c> / <c>Sprint</c> / <c>Story Points</c>) e o vincula
/// ao repositório. Equivalente ao <c>scripts/bootstrap.sh</c>, mas pela API
/// do GitHub — sem depender do <c>gh</c> CLI.
/// </summary>
public sealed class BootstrapService(ProjectsV2Client projects, IOptions<HarnessOptions> harnessOptions)
{
    private readonly HarnessOptions _options = harnessOptions.Value;

    public async Task<BootstrapResult> BootstrapAsync(
        RepoRef target, string? title = null, CancellationToken ct = default)
    {
        title = string.IsNullOrWhiteSpace(title) ? $"{target.Repo} Sprints" : title.Trim();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Idempotência: se já existe um board válido, não cria outro.
        var existing = await FindExistingBoardAsync(target, ct);
        if (existing is not null)
        {
            return new BootstrapResult(
                Created: false,
                Project: existing,
                CurrentSprint: existing.Field("Sprint")?.CurrentIteration(today),
                Sprints: SprintsOf(existing),
                Warnings: Validate(existing, today));
        }

        var template = await projects.GetProjectByNumberAsync(
            _options.TemplateOwner, _options.TemplateNumber, ct);
        var targetOwnerId = await projects.ResolveOwnerIdAsync(target.Owner, ct);
        var repositoryId = await projects.ResolveRepositoryIdAsync(target, ct);

        var copied = await projects.CopyProjectAsync(template.Id, targetOwnerId, title, ct);
        await projects.LinkRepositoryAsync(copied.Id, repositoryId, ct);

        var board = await projects.GetProjectByNumberAsync(target.Owner, copied.Number, ct);

        return new BootstrapResult(
            Created: true,
            Project: board,
            CurrentSprint: board.Field("Sprint")?.CurrentIteration(today),
            Sprints: SprintsOf(board),
            Warnings: Validate(board, today));
    }

    private async Task<HarnessProject?> FindExistingBoardAsync(RepoRef target, CancellationToken ct)
    {
        var linked = await projects.ListLinkedProjectsAsync(target, ct);
        return linked.Count switch
        {
            0 => null,
            1 => linked[0],
            _ => linked.FirstOrDefault(p => p.Title.EndsWith("Sprints", StringComparison.OrdinalIgnoreCase))
                 ?? linked[0],
        };
    }

    private IReadOnlyList<string> Validate(HarnessProject board, DateOnly today)
    {
        var warnings = new List<string>();

        foreach (var name in _options.RequiredFields)
        {
            if (board.Field(name) is null)
            {
                warnings.Add($"Campo obrigatório '{name}' não veio no board — o template #{_options.TemplateNumber} " +
                             $"de '{_options.TemplateOwner}' pode estar desconfigurado.");
            }
        }

        var sprint = board.Field("Sprint");
        if (sprint is { Iterations.Count: > 0 })
        {
            var earliest = sprint.Iterations
                .Where(i => i.StartDate is not null)
                .OrderBy(i => i.StartDate)
                .FirstOrDefault();

            if (earliest?.StartDate is { } start && start < today.AddDays(-earliest.DurationDays))
            {
                warnings.Add("O calendário de iterações do campo 'Sprint' parece herdado do template " +
                             "(começa no passado). Ajuste as datas na UI do Project se não fizer sentido para este repo.");
            }
        }
        else if (sprint is not null)
        {
            warnings.Add("O campo 'Sprint' não tem iterações configuradas. Crie a primeira sprint na UI do Project.");
        }

        return warnings;
    }

    private static IReadOnlyList<ProjectIteration> SprintsOf(HarnessProject board) =>
        board.Field("Sprint")?.Iterations.Where(i => !i.Completed).OrderBy(i => i.StartDate).ToList() ?? [];
}
