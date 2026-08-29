namespace MCP.Harness.GitHub;

/// <summary>
/// Configuração de acesso ao GitHub. Ligada à seção <c>GitHub</c> da
/// configuração e/ou às variáveis de ambiente pelo host.
/// </summary>
public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>
    /// Personal Access Token. Se vazio, o <see cref="GitHubTokenProvider"/>
    /// tenta as variáveis de ambiente e, por último, o <c>gh</c> CLI.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>Base da API REST. Troque para GitHub Enterprise.</summary>
    public string RestBaseUrl { get; set; } = "https://api.github.com/";

    /// <summary>Endpoint GraphQL.</summary>
    public string GraphQlUrl { get; set; } = "https://api.github.com/graphql";

    /// <summary>User-Agent enviado em toda requisição (exigido pelo GitHub).</summary>
    public string UserAgent { get; set; } = "MCP.Harness";

    /// <summary>
    /// Se <c>true</c>, permite descobrir o token via <c>gh auth token</c>
    /// quando nenhum outro foi encontrado. Só afeta a descoberta do token —
    /// nenhuma operação usa o binário <c>gh</c>.
    /// </summary>
    public bool AllowGhCliTokenFallback { get; set; } = true;
}
