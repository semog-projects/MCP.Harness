using System.Text;
using System.Text.Json;
using MCP.Harness.GitHub;

namespace MCP.Harness;

/// <summary>Formatação compartilhada do snapshot do board.</summary>
internal static class BoardView
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string ToJson(BoardSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json);

    public static string ToMarkdown(BoardSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Board — Project #{snapshot.ProjectNumber} · sprint {snapshot.Sprint}");
        sb.AppendLine($"{snapshot.ItemCount} itens · {Points(snapshot.StoryPoints)} SP · {snapshot.ProjectUrl}");

        foreach (var column in snapshot.Columns)
        {
            sb.AppendLine();
            sb.AppendLine($"## {column.Status} — {column.Items.Count} · {Points(column.StoryPoints)} SP");

            if (column.Items.Count == 0)
            {
                sb.AppendLine("_(vazio)_");
                continue;
            }

            foreach (var item in column.Items)
            {
                var meta = new List<string>();
                if (item.StoryPoints is { } sp)
                {
                    meta.Add($"{Points(sp)} SP");
                }

                if (item.Assignees.Count > 0)
                {
                    meta.Add("@" + string.Join(", @", item.Assignees));
                }

                if (!string.Equals(item.State, "open", StringComparison.OrdinalIgnoreCase))
                {
                    meta.Add(item.State);
                }

                var suffix = meta.Count > 0 ? $" — {string.Join(" · ", meta)}" : string.Empty;
                sb.AppendLine($"- [#{item.Number}]({item.Url}) {item.Title}{suffix}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string Points(double value) => value == Math.Floor(value)
        ? ((long)value).ToString()
        : value.ToString("0.##");
}
