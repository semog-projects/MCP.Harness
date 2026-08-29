using System.Net;
using MCP.Harness.GitHub;
using MCP.Harness.Tests.Fakes;

namespace MCP.Harness.Tests.GitHub;

public class MoveTaskTests
{
    private const string Board = """
        {
          "id": "PVT_9", "number": 9, "title": "MCP.Harness Sprints", "url": "u",
          "fields": { "nodes": [
            { "__typename": "ProjectV2SingleSelectField", "id": "F_status", "name": "Status", "dataType": "SINGLE_SELECT",
              "options": [ { "id": "opt_backlog", "name": "Backlog" }, { "id": "opt_todo", "name": "Todo" },
                           { "id": "opt_doing", "name": "Doing" }, { "id": "opt_done", "name": "Done" } ] }
          ] }
        }
        """;

    private static TaskService Build(Func<string, HttpResponseMessage> route, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(req => route(req.Body ?? string.Empty));
        var rest = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var gql = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/graphql") };
        var issues = new IssuesClient(rest);
        var projects = new ProjectsV2Client(new GraphQlClient(gql));
        return new TaskService(new GitHubClient(issues, projects), projects, issues);
    }

    private static HttpResponseMessage Ok(string data) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{ "data": {{data}} }""");

    private static HttpResponseMessage Route(string body) => body switch
    {
        _ when body.Contains("projectsV2(first: 20)") =>
            Ok($$"""{ "repository": { "projectsV2": { "nodes": [ {{Board}} ] } } }"""),
        _ when body.Contains("issue(number: $number)") =>
            Ok("""{ "repository": { "issue": { "projectItems": { "nodes": [ { "id": "PVTI_7", "project": { "id": "PVT_9" } } ] } } } }"""),
        _ when body.Contains("fieldValueByName(name: $field)") =>
            Ok("""{ "node": { "fieldValueByName": { "name": "Todo" } } }"""),
        _ when body.Contains("updateProjectV2ItemFieldValue") =>
            Ok("""{ "updateProjectV2ItemFieldValue": { "projectV2Item": { "id": "PVTI_7" } } }"""),
        _ => throw new InvalidOperationException(body),
    };

    [Theory]
    [InlineData("Doing", "opt_doing")]
    [InlineData("done", "opt_done")]
    [InlineData("BACKLOG", "opt_backlog")]
    public async Task Moves_status_to_each_valid_option(string target, string expectedOptionId)
    {
        var service = Build(Route, out var handler);

        var outcome = await service.MoveTaskAsync(new RepoRef("o", "r"), 7, target);

        Assert.Equal("Todo", outcome.FromStatus);
        Assert.Contains(handler.Requests, r =>
            r.Body!.Contains("updateProjectV2ItemFieldValue")
            && r.Body!.Contains($"\"singleSelectOptionId\":\"{expectedOptionId}\""));
    }

    [Fact]
    public async Task Invalid_status_lists_valid_options_and_sends_no_mutation()
    {
        var service = Build(Route, out var handler);

        var ex = await Assert.ThrowsAsync<GitHubException>(
            () => service.MoveTaskAsync(new RepoRef("o", "r"), 7, "InProgress"));

        Assert.Contains("Backlog, Todo, Doing, Done", ex.Message);
        Assert.DoesNotContain(handler.Requests, r => r.Body!.Contains("updateProjectV2ItemFieldValue"));
    }

    [Fact]
    public async Task Fails_when_issue_is_not_on_the_board()
    {
        var service = Build(body => body.Contains("issue(number: $number)")
            ? Ok("""{ "repository": { "issue": { "projectItems": { "nodes": [] } } } }""")
            : Route(body), out _);

        var ex = await Assert.ThrowsAsync<GitHubException>(
            () => service.MoveTaskAsync(new RepoRef("o", "r"), 7, "Doing"));

        Assert.Contains("não está no board", ex.Message);
    }
}
