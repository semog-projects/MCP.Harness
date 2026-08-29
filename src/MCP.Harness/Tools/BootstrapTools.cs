using System.ComponentModel;
using System.Text;
using MCP.Harness.GitHub;
using ModelContextProtocol.Server;

namespace MCP.Harness.Tools;

/// <summary>Tool de bootstrap do board de um repositório.</summary>
[McpServerToolType]
public sealed class BootstrapTools
{
    [McpServerTool(Name = "harness_bootstrap")]
    [Description(
        "Cria o board (GitHub Project v2) de um repositório a partir do template padronizado " +
        "do harness (campos Status/Sprint/Story Points) e o vincula ao repo. Idempotente: se já " +
        "houver board vinculado, apenas reporta o estado atual. Equivale ao scripts/bootstrap.sh.")]
    public static Task<string> Bootstrap(
        BootstrapService bootstrap,
        [Description("Owner do repositório alvo (organização ou usuário).")] string owner,
        [Description("Nome do repositório alvo.")] string repo,
        [Description("Título do Project. Default: \"<repo> Sprints\".")] string? title = null,
        CancellationToken cancellationToken = default)
        => ToolResult.GuardAsync(async () =>
            Format(await bootstrap.BootstrapAsync(new RepoRef(owner, repo), title, cancellationToken)));

    private static string Format(BootstrapResult result)
    {
        var sb = new StringBuilder();
        var project = result.Project;

        sb.AppendLine(result.Created
            ? $"✅ Board criado: **{project.Title}** (Project #{project.Number})"
            : $"ℹ️ Board já vinculado: **{project.Title}** (Project #{project.Number}) — nada a fazer.");
        sb.AppendLine($"URL: {project.Url}");

        var fields = string.Join(", ", project.Fields
            .Where(f => f.Type is not ProjectFieldType.Other)
            .Select(f => f.Name));
        sb.AppendLine($"Campos: {fields}");

        if (result.CurrentSprint is { } current)
        {
            sb.AppendLine($"Sprint atual: **{current.Title}** (início {current.StartDate:yyyy-MM-dd}, {current.DurationDays} dias)");
        }
        else
        {
            sb.AppendLine("Sprint atual: — (nenhuma iteração cobre hoje)");
        }

        if (result.Sprints.Count > 0)
        {
            sb.AppendLine("Sprints:");
            foreach (var s in result.Sprints)
            {
                sb.AppendLine($"  - {s.Title} — {s.StartDate:yyyy-MM-dd} (+{s.DurationDays}d)");
            }
        }

        if (result.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("⚠️ Avisos:");
            foreach (var w in result.Warnings)
            {
                sb.AppendLine($"  - {w}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
