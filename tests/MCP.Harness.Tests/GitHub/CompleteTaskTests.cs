using System.Net;
using MCP.Harness.GitHub;
using MCP.Harness.Tests.Fakes;

namespace MCP.Harness.Tests.GitHub;

public class CompleteTaskTests
{
    private const string Board = """
        { "id": "PVT_9", "number": 9, "title": "MCP.Harness Sprints", "url": "u",
          "fields": { "nodes": [
            { "__typename": "ProjectV2SingleSelectField", "id": "F_status", "name": "Status", "dataType": "SINGLE_SELECT",
              "options": [ { "id": "opt_doing", "name": "Doing" }, { "id": "opt_done", "name": "Done" } ] } ] } }
        """;

    private static TaskService Build(Func<CapturedRequest, HttpResponseMessage> route, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(route);
        var rest = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var gql = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/graphql") };
        var issues = new IssuesClient(rest);
        var projects = new ProjectsV2Client(new GraphQlClient(gql));
        return new TaskService(new GitHubClient(issues, projects), projects, issues);
    }

    private static HttpResponseMessage Ok(string data) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{ "data": {{data}} }""");

    private static HttpResponseMessage Issue(string state) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK,
            $$"""{ "number": 7, "node_id": "I_7", "html_url": "https://github.com/o/r/issues/7", "state": "{{state}}", "title": "t" }""");

    private static HttpResponseMessage? Graph(string body) => body switch
    {
        _ when body.Contains("projectsV2(first: 20)") =>
            Ok($$"""{ "repository": { "projectsV2": { "nodes": [ {{Board}} ] } } }"""),
        _ when body.Contains("issue(number: $number)") =>
            Ok("""{ "repository": { "issue": { "projectItems": { "nodes": [ { "id": "PVTI_7", "project": { "id": "PVT_9" } } ] } } } }"""),
        _ when body.Contains("updateProjectV2ItemFieldValue") =>
            Ok("""{ "updateProjectV2ItemFieldValue": { "projectV2Item": { "id": "PVTI_7" } } }"""),
        _ => null,
    };

    [Fact]
    public async Task Completes_open_issue_with_comment_then_closes_and_sets_Done()
    {
        var service = Build(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (Graph(req.Body ?? "") is { } g) return g;
            if (path == "/repos/o/r/issues/7" && req.Method == HttpMethod.Get) return Issue("open");
            if (path == "/repos/o/r/issues/7/comments") return StubHttpMessageHandler.Json(HttpStatusCode.Created, "{}");
            if (path == "/repos/o/r/issues/7" && req.Method == HttpMethod.Patch) return Issue("closed");
            throw new InvalidOperationException($"{req.Method} {path} :: {req.Body}");
        }, out var handler);

        var outcome = await service.CompleteTaskAsync(new RepoRef("o", "r"), 7, comment: "feito");

        Assert.False(outcome.AlreadyCompleted);
        Assert.True(outcome.Commented);
        Assert.Equal("closed", outcome.Issue.State);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath == "/repos/o/r/issues/7/comments");
        Assert.Contains(handler.Requests, r => r.Body?.Contains("\"singleSelectOptionId\":\"opt_done\"") == true);
        Assert.Contains(handler.Requests, r => r.Body?.Contains("\"state_reason\":\"completed\"") == true);
    }

    [Fact]
    public async Task Is_idempotent_on_already_closed_issue_no_comment_no_reclose()
    {
        var service = Build(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (Graph(req.Body ?? "") is { } g) return g;
            if (path == "/repos/o/r/issues/7" && req.Method == HttpMethod.Get) return Issue("closed");
            throw new InvalidOperationException($"{req.Method} {path} :: {req.Body}");
        }, out var handler);

        var outcome = await service.CompleteTaskAsync(new RepoRef("o", "r"), 7, comment: "feito");

        Assert.True(outcome.AlreadyCompleted);
        Assert.False(outcome.Commented);
        Assert.Contains(handler.Requests, r => r.Body?.Contains("\"singleSelectOptionId\":\"opt_done\"") == true);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath == "/repos/o/r/issues/7/comments");
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task Closes_without_comment_when_none_given()
    {
        var service = Build(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (Graph(req.Body ?? "") is { } g) return g;
            if (path == "/repos/o/r/issues/7" && req.Method == HttpMethod.Get) return Issue("open");
            if (path == "/repos/o/r/issues/7" && req.Method == HttpMethod.Patch) return Issue("closed");
            throw new InvalidOperationException($"{req.Method} {path} :: {req.Body}");
        }, out var handler);

        var outcome = await service.CompleteTaskAsync(new RepoRef("o", "r"), 7);

        Assert.False(outcome.Commented);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath == "/repos/o/r/issues/7/comments");
    }
}
