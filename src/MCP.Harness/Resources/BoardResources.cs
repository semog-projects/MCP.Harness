using System.ComponentModel;
using MCP.Harness.GitHub;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace MCP.Harness.Resources;

/// <summary>
/// Board como resource MCP — para clientes que leem resources em vez de
/// chamar tools. Payload é o mesmo JSON de <c>harness_board</c>.
/// </summary>
[McpServerResourceType]
public sealed class BoardResources
{
    [McpServerResource(
        UriTemplate = "harness://board/{owner}/{repo}",
        Name = "harness-board",
        Title = "Board da sprint corrente (por repo)",
        MimeType = "application/json")]
    [Description("Snapshot da sprint corrente do board de um repositório, em JSON.")]
    public static async Task<string> BoardFor(
        BoardService board, string owner, string repo, CancellationToken cancellationToken)
        => BoardView.ToJson(await board.GetBoardAsync(new RepoRef(owner, repo), sprint: null, ct: cancellationToken));

    [McpServerResource(
        UriTemplate = "harness://board/current",
        Name = "harness-board-current",
        Title = "Board da sprint corrente (repo padrão)",
        MimeType = "application/json")]
    [Description(
        "Snapshot da sprint corrente do board do repo padrão (Harness:DefaultRepo / HARNESS_DEFAULT_REPO). " +
        "Sem repo padrão configurado, aponta para o template harness://board/{owner}/{repo}.")]
    public static async Task<string> CurrentBoard(
        BoardService board, IOptions<HarnessOptions> options, CancellationToken cancellationToken)
    {
        if (options.Value.DefaultRepoRef is not { } target)
        {
            return """
                { "error": "Nenhum repo padrão configurado. Defina Harness:DefaultRepo (ou HARNESS_DEFAULT_REPO) como \"owner/repo\", ou use o resource harness://board/{owner}/{repo}." }
                """;
        }

        return BoardView.ToJson(await board.GetBoardAsync(
            new RepoRef(target.Owner, target.Repo), sprint: null, ct: cancellationToken));
    }
}
