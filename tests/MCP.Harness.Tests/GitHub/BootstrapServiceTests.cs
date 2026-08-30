using System.Net;
using MCP.Harness.GitHub;
using MCP.Harness.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace MCP.Harness.Tests.GitHub;

public class BootstrapServiceTests
{
    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static string ProjectNode(string id, int number, string title, string sprintStart) => $$"""
        {
          "id": "{{id}}", "number": {{number}}, "title": "{{title}}", "url": "https://github.com/orgs/o/projects/{{number}}",
          "fields": { "nodes": [
            { "__typename": "ProjectV2SingleSelectField", "id": "F_status", "name": "Status", "dataType": "SINGLE_SELECT",
              "options": [ { "id": "opt_backlog", "name": "Backlog" }, { "id": "opt_done", "name": "Done" } ] },
            { "__typename": "ProjectV2IterationField", "id": "F_sprint", "name": "Sprint", "dataType": "ITERATION",
              "configuration": {
                "iterations": [ { "id": "it_1", "title": "Sprint 1", "startDate": "{{sprintStart}}", "duration": 14 } ],
                "completedIterations": [] } },
            { "__typename": "ProjectV2Field", "id": "F_sp", "name": "Story Points", "dataType": "NUMBER" }
          ] }
        }
        """;

    private static BootstrapService BuildService(
        Func<string, HttpResponseMessage> route, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(req => route(req.Body ?? string.Empty));
        var graphQl = new GraphQlClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/graphql") });
        return new BootstrapService(
            new ProjectsV2Client(graphQl),
            Options.Create(new HarnessOptions { TemplateOwner = "semog-projects", TemplateNumber = 7 }));
    }

    private static HttpResponseMessage Ok(string data) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{ "data": {{data}} }""");

    // Sondagem organization+user quando o login é uma org: data válido + erro parcial NOT_FOUND.
    private static HttpResponseMessage OkOrgProbe(string data) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""
            { "data": {{data}},
              "errors": [ { "type": "NOT_FOUND", "path": ["user"], "message": "Could not resolve to a User." } ] }
            """);

    private sealed record Scenario
    {
        public IReadOnlyList<string> LinkedNodes { get; init; } = [];
        public IReadOnlyList<string> OwnerNodes { get; init; } = [];
        public bool LinkForbidden { get; init; }
    }

    private static HttpResponseMessage Dispatch(string body, Scenario s)
    {
        // ListLinkedProjectsAsync (passo 1)
        if (body.Contains("projectsV2(first: 20)"))
        {
            return Ok($$"""{ "repository": { "projectsV2": { "nodes": [ {{string.Join(",", s.LinkedNodes)}} ] } } }""");
        }

        // ListOwnerProjectsAsync (passo 2)
        if (body.Contains("projectsV2(first: 50)"))
        {
            return OkOrgProbe($$"""
                { "organization": { "projectsV2": { "nodes": [ {{string.Join(",", s.OwnerNodes)}} ] } }, "user": null }
                """);
        }

        // GetProjectByNumberAsync (template e re-resolve)
        if (body.Contains("$number: Int!"))
        {
            var node = body.Contains("\"login\":\"semog-projects\"")
                ? ProjectNode("PVT_template", 7, "[TEMPLATE] Sprint Harness", Today)
                : ProjectNode("PVT_new", 42, "MCP.Harness Sprints", Today);
            return OkOrgProbe($$"""{ "organization": { "projectV2": {{node}} }, "user": null }""");
        }

        if (body.Contains("organization(login: $login)"))
        {
            return OkOrgProbe("""{ "organization": { "id": "OWNER_o" }, "user": null }""");
        }

        if (body.Contains("repository(owner: $owner, name: $repo)"))
        {
            return Ok("""{ "repository": { "id": "REPO_id" } }""");
        }

        if (body.Contains("copyProjectV2"))
        {
            return Ok("""
                { "copyProjectV2": { "projectV2": {
                  "id": "PVT_new", "number": 42, "title": "MCP.Harness Sprints",
                  "url": "https://github.com/orgs/o/projects/42" } } }
                """);
        }

        if (body.Contains("linkProjectV2ToRepository"))
        {
            return s.LinkForbidden
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """
                    { "data": null, "errors": [ { "type": "FORBIDDEN",
                      "message": "your token has not been granted the required scopes" } ] }
                    """)
                : Ok("""{ "linkProjectV2ToRepository": { "repository": { "id": "REPO_id" } } }""");
        }

        throw new InvalidOperationException($"query não esperada: {body}");
    }

    [Fact]
    public async Task Creates_and_links_a_board_when_none_exists()
    {
        var service = BuildService(b => Dispatch(b, new Scenario()), out var handler);

        var result = await service.BootstrapAsync(new RepoRef("o", "r"));

        Assert.True(result.Created);
        Assert.True(result.Linked);
        Assert.Equal(42, result.Project.Number);
        Assert.Equal("it_1", result.CurrentSprint!.Id);
        Assert.Empty(result.Warnings);
        Assert.Contains(handler.Requests, r => r.Body!.Contains("copyProjectV2"));
        Assert.Contains(handler.Requests, r => r.Body!.Contains("linkProjectV2ToRepository"));
    }

    [Fact]
    public async Task Is_idempotent_when_a_board_is_already_linked()
    {
        var linked = ProjectNode("PVT_9", 9, "MCP.Harness Sprints", Today);
        var service = BuildService(b => Dispatch(b, new Scenario { LinkedNodes = [linked] }), out var handler);

        var result = await service.BootstrapAsync(new RepoRef("o", "r"));

        Assert.False(result.Created);
        Assert.True(result.Linked);
        Assert.Equal(9, result.Project.Number);
        Assert.DoesNotContain(handler.Requests, r => r.Body!.Contains("copyProjectV2"));
    }

    [Fact]
    public async Task Warns_when_sprint_calendar_is_inherited_from_template()
    {
        var longAgo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-120).ToString("yyyy-MM-dd");
        var linked = ProjectNode("PVT_9", 9, "MCP.Harness Sprints", longAgo);
        var service = BuildService(b => Dispatch(b, new Scenario { LinkedNodes = [linked] }), out _);

        var result = await service.BootstrapAsync(new RepoRef("o", "r"));

        Assert.Contains(result.Warnings, w => w.Contains("herdado do template"));
    }

    [Fact]
    public async Task Link_failure_does_not_abort_and_warns_with_manual_step()
    {
        var service = BuildService(b => Dispatch(b, new Scenario { LinkForbidden = true }), out _);

        var result = await service.BootstrapAsync(new RepoRef("o", "r"));

        Assert.True(result.Created);
        Assert.False(result.Linked);
        Assert.Equal(42, result.Project.Number);
        Assert.Contains(result.Warnings, w => w.Contains("gh project link 42 --owner o --repo r"));
        Assert.Contains(result.Warnings, w => w.Contains("CLÁSSICO"));
    }

    [Fact]
    public async Task Reuses_an_orphan_board_instead_of_creating_a_second_one()
    {
        var orphan = ProjectNode("PVT_orphan", 42, "r Sprints", Today);
        var service = BuildService(b => Dispatch(b, new Scenario { OwnerNodes = [orphan] }), out var handler);

        var result = await service.BootstrapAsync(new RepoRef("o", "r"));

        Assert.False(result.Created);
        Assert.True(result.Linked);
        Assert.Equal(42, result.Project.Number);
        Assert.DoesNotContain(handler.Requests, r => r.Body!.Contains("copyProjectV2"));
        Assert.Contains(handler.Requests, r => r.Body!.Contains("linkProjectV2ToRepository"));
    }
}
