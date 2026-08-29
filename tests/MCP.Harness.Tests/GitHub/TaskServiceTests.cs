using System.Net;
using MCP.Harness.GitHub;
using MCP.Harness.Tests.Fakes;

namespace MCP.Harness.Tests.GitHub;

public class TaskServiceTests
{
    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private const string BoardNode = """
        {
          "id": "PVT_9", "number": 9, "title": "MCP.Harness Sprints", "url": "https://github.com/orgs/o/projects/9",
          "fields": { "nodes": [
            { "__typename": "ProjectV2SingleSelectField", "id": "F_status", "name": "Status", "dataType": "SINGLE_SELECT",
              "options": [ { "id": "opt_backlog", "name": "Backlog" } ] },
            { "__typename": "ProjectV2IterationField", "id": "F_sprint", "name": "Sprint", "dataType": "ITERATION",
              "configuration": { "iterations": [ { "id": "it_1", "title": "Sprint 1", "startDate": "TODAY", "duration": 14 } ],
                "completedIterations": [] } },
            { "__typename": "ProjectV2Field", "id": "F_sp", "name": "Story Points", "dataType": "NUMBER" }
          ] }
        }
        """;

    private static string Board => BoardNode.Replace("TODAY", Today);

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

    [Fact]
    public async Task Creates_issue_and_places_it_on_the_board_in_Backlog()
    {
        var service = Build(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = req.Body ?? "";

            if (body.Contains("viewer { login }"))
            {
                return Ok("""{ "viewer": { "login": "semog-dev" } }""");
            }
            if (path == "/repos/o/r/issues" && req.Method == HttpMethod.Get)
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
            }
            if (path == "/repos/o/r/issues" && req.Method == HttpMethod.Post)
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, """
                    { "number": 20, "node_id": "I_new", "html_url": "https://github.com/o/r/issues/20",
                      "state": "open", "title": "Nova task", "assignees": [ { "login": "semog-dev" } ] }
                    """);
            }
            if (body.Contains("projectsV2(first: 20)"))
            {
                return Ok($$"""{ "repository": { "projectsV2": { "nodes": [ {{Board}} ] } } }""");
            }
            if (body.Contains("addProjectV2ItemById"))
            {
                return Ok("""{ "addProjectV2ItemById": { "item": { "id": "PVTI_new" } } }""");
            }
            if (body.Contains("updateProjectV2ItemFieldValue"))
            {
                return Ok("""{ "updateProjectV2ItemFieldValue": { "projectV2Item": { "id": "PVTI_new" } } }""");
            }

            throw new InvalidOperationException($"{req.Method} {path} :: {body}");
        }, out var handler);

        var outcome = await service.CreateTaskAsync(
            new RepoRef("o", "r"), "Nova task", "corpo", type: "Task", storyPoints: 3);

        Assert.True(outcome.Created);
        Assert.Equal(20, outcome.Issue.Number);
        Assert.Equal("PVTI_new", outcome.Item.Id);
        Assert.Equal("Sprint 1", outcome.Sprint!.Title);

        Assert.Equal(new[] { "semog-dev" }, outcome.Assignees);

        var post = handler.Requests.Single(r => r.RequestUri!.AbsolutePath == "/repos/o/r/issues" && r.Method == HttpMethod.Post);
        Assert.Contains("\"assignees\":[\"semog-dev\"]", post.Body);
        Assert.Contains(handler.Requests, r => r.Body?.Contains("\"singleSelectOptionId\":\"opt_backlog\"") == true);
        Assert.Contains(handler.Requests, r => r.Body?.Contains("\"iterationId\":\"it_1\"") == true);
        Assert.Contains(handler.Requests, r => r.Body?.Contains("\"number\":3") == true);
    }

    [Fact]
    public async Task Explicit_assignees_override_the_default_viewer()
    {
        var service = Build(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = req.Body ?? "";
            if (path == "/repos/o/r/issues" && req.Method == HttpMethod.Get)
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
            if (path == "/repos/o/r/issues" && req.Method == HttpMethod.Post)
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, """
                    { "number": 20, "node_id": "I_new", "html_url": "u", "state": "open", "title": "T",
                      "assignees": [ { "login": "alice" }, { "login": "bob" } ] }
                    """);
            if (body.Contains("projectsV2(first: 20)"))
                return Ok($$"""{ "repository": { "projectsV2": { "nodes": [ {{Board}} ] } } }""");
            if (body.Contains("addProjectV2ItemById"))
                return Ok("""{ "addProjectV2ItemById": { "item": { "id": "PVTI_new" } } }""");
            if (body.Contains("updateProjectV2ItemFieldValue"))
                return Ok("""{ "updateProjectV2ItemFieldValue": { "projectV2Item": { "id": "PVTI_new" } } }""");
            throw new InvalidOperationException($"{req.Method} {path} :: {body}");
        }, out var handler);

        var outcome = await service.CreateTaskAsync(
            new RepoRef("o", "r"), "T", "corpo", assignees: ["@alice", "bob"]);

        Assert.Equal(new[] { "alice", "bob" }, outcome.Assignees);
        Assert.DoesNotContain(handler.Requests, r => r.Body?.Contains("viewer { login }") == true);
        var post = handler.Requests.Single(r => r.RequestUri!.AbsolutePath == "/repos/o/r/issues" && r.Method == HttpMethod.Post);
        Assert.Contains("\"assignees\":[\"alice\",\"bob\"]", post.Body);
    }

    [Fact]
    public async Task Does_not_create_a_second_issue_when_an_open_one_has_the_same_title()
    {
        var service = Build(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = req.Body ?? "";

            if (path == "/repos/o/r/issues" && req.Method == HttpMethod.Get)
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                    [ { "number": 7, "node_id": "I_7", "html_url": "https://github.com/o/r/issues/7",
                        "state": "open", "title": "  Nova Task  ", "assignees": [ { "login": "carol" } ] } ]
                    """);
            }
            if (body.Contains("projectsV2(first: 20)"))
            {
                return Ok($$"""{ "repository": { "projectsV2": { "nodes": [ {{Board}} ] } } }""");
            }
            if (body.Contains("issue(number: $number)"))
            {
                return Ok("""
                    { "repository": { "issue": { "projectItems": { "nodes": [
                      { "id": "PVTI_7", "project": { "id": "PVT_9" } } ] } } } }
                    """);
            }

            throw new InvalidOperationException($"{req.Method} {path} :: {body}");
        }, out var handler);

        var outcome = await service.CreateTaskAsync(new RepoRef("o", "r"), "nova task", "corpo");

        Assert.False(outcome.Created);
        Assert.Equal(7, outcome.Issue.Number);
        Assert.Equal("PVTI_7", outcome.Item.Id);
        Assert.Equal(new[] { "carol" }, outcome.Assignees);
        Assert.DoesNotContain(handler.Requests, r =>
            r.RequestUri!.AbsolutePath == "/repos/o/r/issues" && r.Method == HttpMethod.Post);
        Assert.DoesNotContain(handler.Requests, r => r.Body?.Contains("addProjectV2ItemById") == true);
        // Respeita atribuição manual: não reassina uma Issue que já tem assignee.
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/assignees"));
    }

    [Fact]
    public async Task Adds_existing_off_board_issue_to_Backlog()
    {
        var service = Build(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = req.Body ?? "";

            if (body.Contains("viewer { login }"))
            {
                return Ok("""{ "viewer": { "login": "semog-dev" } }""");
            }
            if (path == "/repos/o/r/issues" && req.Method == HttpMethod.Get)
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                    [ { "number": 7, "node_id": "I_7", "html_url": "u", "state": "open", "title": "Nova task" } ]
                    """);
            }
            if (path == "/repos/o/r/issues/7/assignees" && req.Method == HttpMethod.Post)
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, """
                    { "number": 7, "node_id": "I_7", "html_url": "u", "state": "open", "title": "Nova task",
                      "assignees": [ { "login": "semog-dev" } ] }
                    """);
            }
            if (body.Contains("projectsV2(first: 20)"))
            {
                return Ok($$"""{ "repository": { "projectsV2": { "nodes": [ {{Board}} ] } } }""");
            }
            if (body.Contains("issue(number: $number)"))
            {
                return Ok("""{ "repository": { "issue": { "projectItems": { "nodes": [] } } } }""");
            }
            if (body.Contains("addProjectV2ItemById"))
            {
                return Ok("""{ "addProjectV2ItemById": { "item": { "id": "PVTI_7" } } }""");
            }
            if (body.Contains("updateProjectV2ItemFieldValue"))
            {
                return Ok("""{ "updateProjectV2ItemFieldValue": { "projectV2Item": { "id": "PVTI_7" } } }""");
            }

            throw new InvalidOperationException($"{req.Method} {path} :: {body}");
        }, out var handler);

        var outcome = await service.CreateTaskAsync(new RepoRef("o", "r"), "Nova task", "corpo");

        Assert.False(outcome.Created);
        Assert.Equal("PVTI_7", outcome.Item.Id);
        Assert.Equal(new[] { "semog-dev" }, outcome.Assignees);
        Assert.Contains(handler.Requests, r => r.Body?.Contains("addProjectV2ItemById") == true);
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath == "/repos/o/r/issues/7/assignees");
        Assert.DoesNotContain(handler.Requests, r =>
            r.RequestUri!.AbsolutePath == "/repos/o/r/issues" && r.Method == HttpMethod.Post);
    }
}
