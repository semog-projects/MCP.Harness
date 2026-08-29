namespace MCP.Harness.GitHub;

/// <summary>
/// Configuração do harness em si (independente da autenticação). Ligada à
/// seção <c>Harness</c> e às variáveis <c>HARNESS_*</c> pelo host.
/// </summary>
public sealed class HarnessOptions
{
    public const string SectionName = "Harness";

    /// <summary>Dono do Project v2 usado como template no bootstrap.</summary>
    public string TemplateOwner { get; set; } = "semog-projects";

    /// <summary>Número do Project v2 usado como template no bootstrap.</summary>
    public int TemplateNumber { get; set; } = 7;

    /// <summary>Nomes dos campos obrigatórios que o board precisa ter.</summary>
    public IReadOnlyList<string> RequiredFields { get; } = ["Status", "Sprint", "Story Points"];

    /// <summary>
    /// Repo padrão para o resource <c>harness://board/current</c> (que não
    /// carrega owner/repo). Formato <c>owner/repo</c>. Vazio = o resource
    /// pede para usar o template <c>harness://board/{owner}/{repo}</c>.
    /// </summary>
    public string? DefaultRepo { get; set; }

    /// <summary>(owner, repo) a partir de <see cref="DefaultRepo"/>, ou <c>null</c>.</summary>
    public (string Owner, string Repo)? DefaultRepoRef
    {
        get
        {
            var parts = DefaultRepo?.Split('/', 2, StringSplitOptions.TrimEntries);
            return parts is [{ Length: > 0 } owner, { Length: > 0 } repo] ? (owner, repo) : null;
        }
    }
}
