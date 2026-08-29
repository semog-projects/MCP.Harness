using System.ComponentModel;
using System.Text;
using MCP.Harness.GitHub;
using ModelContextProtocol.Server;

namespace MCP.Harness.Tools;

/// <summary>Tools do ciclo de vida de uma task no board.</summary>
[McpServerToolType]
public sealed class TaskTools
{
    [McpServerTool(Name = "harness_create_task")]
    [Description(
        "Cria uma Issue rastreada no board do repositório: adiciona ao Project, Status = Backlog e, " +
        "se houver sprint corrente, também a Sprint. Antes de criar, procura uma Issue ABERTA com o " +
        "mesmo título — se achar, devolve essa em vez de duplicar.")]
    public static Task<string> CreateTask(
        TaskService tasks,
        [Description("Owner do repositório (organização ou usuário).")] string owner,
        [Description("Nome do repositório.")] string repo,
        [Description("Título da task (curto e descritivo).")] string title,
        [Description("Corpo da Issue: contexto do pedido, critérios de aceite, constraints.")] string body,
        [Description("Issue type: Task, Bug ou Feature. Default: Task.")] string? type = "Task",
        [Description("Estimativa em Story Points. Omita se ainda não há estimativa.")] double? storyPoints = null,
        [Description("Número do Project, se o repo tiver mais de um board vinculado.")] int? projectNumber = null,
        CancellationToken cancellationToken = default)
        => ToolResult.GuardAsync(async () =>
        {
            var outcome = await tasks.CreateTaskAsync(
                new RepoRef(owner, repo), title, body, type, storyPoints, projectNumber, cancellationToken);

            var sb = new StringBuilder();
            sb.AppendLine(outcome.Created
                ? $"✅ Task criada: **#{outcome.Issue.Number}** — {outcome.Issue.Title}"
                : $"ℹ️ Já existia uma Issue aberta com esse título: **#{outcome.Issue.Number}** — {outcome.Issue.Title} (não dupliquei)");
            sb.AppendLine($"URL: {outcome.Issue.Url}");
            sb.AppendLine($"Board: Project #{outcome.Project.Number} · item `{outcome.Item.Id}` · Status = Backlog");
            sb.AppendLine(outcome.Sprint is { } s
                ? $"Sprint: {s.Title}"
                : "Sprint: — (nenhuma iteração cobre hoje; campo não definido)");

            return sb.ToString().TrimEnd();
        });

    [McpServerTool(Name = "harness_move_task")]
    [Description(
        "Move o Status da task de uma Issue no board (Backlog / Todo / Doing / Done). " +
        "Não fecha a Issue quando o alvo é Done — para isso use harness_complete_task.")]
    public static Task<string> MoveTask(
        TaskService tasks,
        [Description("Owner do repositório (organização ou usuário).")] string owner,
        [Description("Nome do repositório.")] string repo,
        [Description("Número da Issue.")] int issueNumber,
        [Description("Status alvo: Backlog, Todo, Doing ou Done (conforme as opções do board).")] string status,
        [Description("Número do Project, se o repo tiver mais de um board vinculado.")] int? projectNumber = null,
        CancellationToken cancellationToken = default)
        => ToolResult.GuardAsync(async () =>
        {
            var outcome = await tasks.MoveTaskAsync(
                new RepoRef(owner, repo), issueNumber, status, projectNumber, cancellationToken);

            return $"✅ #{outcome.IssueNumber}: Status **{outcome.FromStatus} → {outcome.ToStatus}** " +
                   $"(Project #{outcome.Project.Number}, item `{outcome.Item.Id}`)";
        });
}
