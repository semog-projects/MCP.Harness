using MCP.Harness.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MCP.Harness.Tests.GitHub;

/// <summary>
/// Testes ponta-a-ponta contra o GitHub real. Só rodam quando
/// <c>HARNESS_IT=1</c> e <c>GITHUB_TOKEN</c> estão no ambiente.
///
/// Alvo (sobrescrevível): <c>HARNESS_IT_OWNER</c> / <c>HARNESS_IT_REPO</c>,
/// default <c>semog-projects/MCP.Harness</c>. Cada Issue criada é apagada no
/// fim (<c>deleteIssue</c>).
/// </summary>
[Trait("Category", "Integration")]
[Collection("environment-mutation")]
public class GitHubIntegrationTests
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("HARNESS_IT") == "1" &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

    private static RepoRef TargetRepo => new(
        Environment.GetEnvironmentVariable("HARNESS_IT_OWNER") ?? "semog-projects",
        Environment.GetEnvironmentVariable("HARNESS_IT_REPO") ?? "MCP.Harness");

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHarnessGitHub();
        return services.BuildServiceProvider();
    }

    [SkippableFact]
    public async Task Create_task_places_on_board_and_dedups_on_second_call()
    {
        Skip.IfNot(Enabled, "defina HARNESS_IT=1 e GITHUB_TOKEN para rodar os testes de integração");

        using var provider = BuildProvider();
        var tasks = provider.GetRequiredService<TaskService>();
        var issues = provider.GetRequiredService<IssuesClient>();
        var graphQl = provider.GetRequiredService<GraphQlClient>();
        var repo = TargetRepo;

        var title = $"[it] harness create_task {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";
        var first = await tasks.CreateTaskAsync(repo, title, "Issue temporária de teste.", type: "Task", storyPoints: 1);

        try
        {
            Assert.True(first.Created);
            Assert.Equal("Backlog", await StatusOf(graphQl, first));

            // A listagem REST do GitHub pode levar alguns segundos para
            // refletir a Issue recém-criada. Espera ela aparecer antes de
            // testar a dedup — assim o segundo CreateTaskAsync não cria uma
            // duplicata de verdade.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                if (await issues.FindOpenByExactTitleAsync(repo, title) is not null)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            var second = await tasks.CreateTaskAsync(repo, title, "segunda chamada", type: "Task");
            Assert.False(second.Created);
            Assert.Equal(first.Issue.Number, second.Issue.Number);
            Assert.Equal(first.Item.Id, second.Item.Id);
        }
        finally
        {
            await DeleteIssue(graphQl, first.Issue.NodeId);
        }
    }

    private static async Task<string?> StatusOf(GraphQlClient graphQl, CreateTaskOutcome outcome)
    {
        const string query = """
            query($id: ID!) {
              node(id: $id) {
                ... on ProjectV2Item {
                  fieldValueByName(name: "Status") { ... on ProjectV2ItemFieldSingleSelectValue { name } }
                }
              }
            }
            """;

        var data = await graphQl.ExecuteAsync(query, new { id = outcome.Item.Id }, "ler Status do item", default);
        var value = data.GetProperty("node").GetProperty("fieldValueByName");
        return value.ValueKind == System.Text.Json.JsonValueKind.Null ? null : value.GetProperty("name").GetString();
    }

    private static async Task DeleteIssue(GraphQlClient graphQl, string issueNodeId)
    {
        const string mutation = "mutation($id: ID!) { deleteIssue(input: { issueId: $id }) { clientMutationId } }";
        await graphQl.ExecuteAsync(mutation, new { id = issueNodeId }, "apagar Issue de teste", default);
    }
}
