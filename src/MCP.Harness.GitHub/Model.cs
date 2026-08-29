namespace MCP.Harness.GitHub;

/// <summary>Coordenada de um repositório.</summary>
public readonly record struct RepoRef(string Owner, string Repo)
{
    public override string ToString() => $"{Owner}/{Repo}";
}

/// <summary>Issue recém-criada ou atualizada (subconjunto do payload REST).</summary>
public sealed record IssueRef(int Number, string NodeId, string Url, string State, string Title);

/// <summary>Tipo de campo do Project v2 que o harness conhece.</summary>
public enum ProjectFieldType
{
    Other,
    SingleSelect,
    Iteration,
    Number,
    Text,
    Date,
}

public sealed record ProjectFieldOption(string Id, string Name);

public sealed record ProjectIteration(string Id, string Title, DateOnly? StartDate, int DurationDays, bool Completed);

/// <summary>Definição de um campo do board, com opções/iterações quando aplicável.</summary>
public sealed record ProjectField(
    string Id,
    string Name,
    ProjectFieldType Type,
    IReadOnlyList<ProjectFieldOption> Options,
    IReadOnlyList<ProjectIteration> Iterations)
{
    public ProjectFieldOption? FindOption(string name) =>
        Options.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Iteração atual (contém hoje) ou, na falta, a próxima não concluída.</summary>
    public ProjectIteration? CurrentIteration(DateOnly today)
    {
        ProjectIteration? next = null;
        foreach (var it in Iterations)
        {
            if (it.Completed || it.StartDate is not { } start)
            {
                continue;
            }

            var end = start.AddDays(it.DurationDays);
            if (today >= start && today < end)
            {
                return it;
            }

            if (today < start && (next is null || start < next.StartDate))
            {
                next = it;
            }
        }

        return next;
    }
}

/// <summary>Project v2 resolvido, já com seus campos indexados por nome.</summary>
public sealed record HarnessProject(string Id, int Number, string Title, string Url, IReadOnlyList<ProjectField> Fields)
{
    public ProjectField? Field(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    public ProjectField Status => Require("Status");
    public ProjectField Sprint => Require("Sprint");
    public ProjectField StoryPoints => Require("Story Points");

    private ProjectField Require(string name) =>
        Field(name) ?? throw new GitHubException(
            $"O Project #{Number} não tem o campo obrigatório '{name}'. " +
            "Rode o bootstrap do harness a partir do template padronizado.");
}

public sealed record ProjectItemRef(string Id);
