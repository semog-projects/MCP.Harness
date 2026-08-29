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
}
