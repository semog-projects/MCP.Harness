using Microsoft.Extensions.Options;

namespace MCP.Harness.GitHub;

/// <summary>Resultado de um bootstrap de board.</summary>
/// <param name="Created">
/// <c>true</c> se o Project foi criado agora; <c>false</c> se já existia
/// (vinculado ou órfão).
/// </param>
/// <param name="Linked">
/// <c>true</c> se o Project está vinculado ao repositório. Pode ser
/// <c>false</c> se o token não tem permissão de <c>linkProjectV2ToRepository</c>
/// (PAT fine-grained) — nesse caso há um aviso com o passo manual.
/// </param>
public sealed record BootstrapResult(
    bool Created,
    bool Linked,
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

        // 1. Já vinculado ao repo? Idempotente, nada a fazer.
        if (await FindLinkedBoardAsync(target, ct) is { } linked)
        {
            return Result(created: false, isLinked: true, linked, today, extra: []);
        }

        var repositoryId = await projects.ResolveRepositoryIdAsync(target, ct);

        // 2. Existe no owner mas não vinculado (link falhou antes, ou linkado
        //    fora do harness). Não cria outro: tenta (re)vincular e devolve.
        if (await FindOrphanBoardAsync(target.Owner, title, ct) is { } orphan)
        {
            var warnings = new List<string>();
            var relinked = await TryLinkAsync(
                new ProjectRef(orphan.Id, orphan.Number), repositoryId, target, warnings, ct);
            var board = await projects.GetProjectByNumberAsync(target.Owner, orphan.Number, ct);
            return Result(created: false, isLinked: relinked, board, today, warnings);
        }

        // 3. Criar do template.
        var template = await projects.GetProjectByNumberAsync(
            _options.TemplateOwner, _options.TemplateNumber, ct);
        var targetOwnerId = await projects.ResolveOwnerIdAsync(target.Owner, ct);

        var copied = await projects.CopyProjectAsync(template.Id, targetOwnerId, title, ct);

        var createWarnings = new List<string>();
        var freshlyLinked = await TryLinkAsync(
            new ProjectRef(copied.Id, copied.Number), repositoryId, target, createWarnings, ct);
        var newBoard = await projects.GetProjectByNumberAsync(target.Owner, copied.Number, ct);

        return Result(created: true, isLinked: freshlyLinked, newBoard, today, createWarnings);
    }

    private readonly record struct ProjectRef(string Id, int Number);

    private BootstrapResult Result(
        bool created, bool isLinked, HarnessProject board, DateOnly today, IReadOnlyList<string> extra)
    {
        var warnings = new List<string>(Validate(board, today));
        warnings.AddRange(extra);
        return new BootstrapResult(
            created, isLinked, board,
            board.Field("Sprint")?.CurrentIteration(today),
            SprintsOf(board),
            warnings);
    }

    private async Task<bool> TryLinkAsync(
        ProjectRef project, string repositoryId, RepoRef target, List<string> warnings, CancellationToken ct)
    {
        try
        {
            await projects.LinkRepositoryAsync(project.Id, repositoryId, ct);
            return true;
        }
        catch (GitHubException ex)
        {
            warnings.Add(
                $"Project #{project.Number} criado, mas NÃO foi vinculado ao repositório ({ex.Message}). " +
                $"linkProjectV2ToRepository costuma exigir um token CLÁSSICO com scope 'project' " +
                $"(PAT fine-grained não serve). Vincule com: " +
                $"gh project link {project.Number} --owner {target.Owner} --repo {target.Repo} " +
                $"— ou pela UI do repo em Projects → Link a project. Rode harness_bootstrap de novo depois para confirmar.");
            return false;
        }
    }

    private async Task<HarnessProject?> FindLinkedBoardAsync(RepoRef target, CancellationToken ct)
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

    private async Task<HarnessProject?> FindOrphanBoardAsync(string owner, string title, CancellationToken ct)
    {
        IReadOnlyList<HarnessProject> ownerProjects;
        try
        {
            ownerProjects = await projects.ListOwnerProjectsAsync(owner, ct);
        }
        catch (GitHubException)
        {
            return null; // sem permissão pra listar os projects do owner — segue pro passo 3
        }

        // Só por título EXATO — "termina em Sprints" pegaria o board de outro repo.
        return ownerProjects.FirstOrDefault(p =>
            string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase));
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
