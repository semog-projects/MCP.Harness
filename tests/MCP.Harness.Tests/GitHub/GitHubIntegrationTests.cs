using MCP.Harness.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MCP.Harness.Tests.GitHub;

/// <summary>
/// Testes ponta-a-ponta contra o GitHub real. Só rodam quando
/// <c>HARNESS_IT=1</c> e <c>GITHUB_TOKEN</c> estão no ambiente.
///
/// Alvo (sobrescrevível): <c>HARNESS_IT_OWNER</c> / <c>HARNESS_IT_REPO</c>,
/// default <c>semog-projects/MCP.Harness</c>.
///
/// Cobre: criar Issue → adicionar ao board → setar Status. A Issue criada é
/// fechada como <c>not_planned</c> ao final.
/// </summary>
[Trait("Category", "Integration")]
public class GitHubIntegrationTests
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("HARNESS_IT") == "1" &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

    private static RepoRef TargetRepo => new(
        Environment.GetEnvironmentVariable("HARNESS_IT_OWNER") ?? "semog-projects",
        Environment.GetEnvironmentVariable("HARNESS_IT_REPO") ?? "MCP.Harness");

    private static GitHubClient BuildClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHarnessGitHub();
        return services.BuildServiceProvider().GetRequiredService<GitHubClient>();
    }

    [SkippableFact]
    public async Task Create_issue_add_to_board_and_set_status()
    {
        Skip.IfNot(Enabled, "defina HARNESS_IT=1 e GITHUB_TOKEN para rodar os testes de integração");

        var github = BuildClient();
        var repo = TargetRepo;

        var project = await github.Projects.ResolveProjectAsync(repo);
        Assert.NotNull(project.Status.FindOption("Backlog"));

        var issue = await github.Issues.CreateAsync(
            repo,
            title: $"[it] smoke {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}",
            body: "Issue temporária criada por GitHubIntegrationTests. Pode fechar.",
            type: "Task");

        ProjectItemRef? item = null;
        try
        {
            var iteration = project.Sprint.CurrentIteration(DateOnly.FromDateTime(DateTime.UtcNow));
            item = await github.PlaceOnBoardAsync(
                project, issue, status: "Backlog", iterationId: iteration?.Id, storyPoints: 1);

            Assert.False(string.IsNullOrWhiteSpace(item.Id));
        }
        finally
        {
            if (item is not null)
            {
                await github.Projects.DeleteItemAsync(project, item);
            }

            await github.Issues.CloseAsync(repo, issue.Number, stateReason: "not_planned");
        }
    }
}
