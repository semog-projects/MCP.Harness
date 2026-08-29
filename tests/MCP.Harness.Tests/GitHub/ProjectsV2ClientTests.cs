using System.Net;
using MCP.Harness.GitHub;
using MCP.Harness.Tests.Fakes;

namespace MCP.Harness.Tests.GitHub;

public class ProjectsV2ClientTests
{
    private const string OneProjectResponse = """
        { "data": { "repository": { "projectsV2": { "nodes": [ {
          "id": "PVT_1", "number": 9, "title": "MCP.Harness Sprints", "url": "https://github.com/orgs/o/projects/9",
          "fields": { "nodes": [
            { "__typename": "ProjectV2SingleSelectField", "id": "F_status", "name": "Status", "dataType": "SINGLE_SELECT",
              "options": [ { "id": "opt_backlog", "name": "Backlog" }, { "id": "opt_doing", "name": "Doing" } ] },
            { "__typename": "ProjectV2IterationField", "id": "F_sprint", "name": "Sprint", "dataType": "ITERATION",
              "configuration": { "iterations": [ { "id": "it_1", "title": "Sprint 1", "startDate": "2026-08-29", "duration": 14 } ],
                "completedIterations": [] } },
            { "__typename": "ProjectV2Field", "id": "F_sp", "name": "Story Points", "dataType": "NUMBER" }
          ] }
        } ] } } } }
        """;

    private static ProjectsV2Client ClientReturning(string body, out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        return new ProjectsV2Client(new GraphQlClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/graphql") }));
    }

    [Fact]
    public async Task ResolveProjectAsync_parses_fields_options_and_iterations()
    {
        var client = ClientReturning(OneProjectResponse, out _);

        var project = await client.ResolveProjectAsync(new RepoRef("o", "r"));

        Assert.Equal("PVT_1", project.Id);
        Assert.Equal(9, project.Number);
        Assert.Equal("opt_backlog", project.Status.FindOption("backlog")!.Id);
        Assert.Equal(ProjectFieldType.Number, project.StoryPoints.Type);

        var current = project.Sprint.CurrentIteration(new DateOnly(2026, 9, 1));
        Assert.Equal("it_1", current!.Id);
    }

    [Fact]
    public async Task ResolveProjectAsync_fails_clearly_when_no_project_linked()
    {
        var client = ClientReturning(
            """{ "data": { "repository": { "projectsV2": { "nodes": [] } } } }""", out _);

        var ex = await Assert.ThrowsAsync<GitHubException>(
            () => client.ResolveProjectAsync(new RepoRef("o", "r")));
        Assert.Contains("bootstrap", ex.Message);
    }

    [Fact]
    public async Task GraphQl_errors_surface_as_exception()
    {
        var client = ClientReturning(
            """{ "errors": [ { "message": "Could not resolve to a Repository" } ] }""", out _);

        var ex = await Assert.ThrowsAsync<GitHubGraphQlException>(
            () => client.ResolveProjectAsync(new RepoRef("o", "r")));
        Assert.Contains("Could not resolve", ex.Message);
    }

    [Fact]
    public async Task SetNumberAsync_sends_updateProjectV2ItemFieldValue_mutation()
    {
        var client = ClientReturning(
            """{ "data": { "updateProjectV2ItemFieldValue": { "projectV2Item": { "id": "PVTI_1" } } } }""",
            out var handler);

        var field = new ProjectField("F_sp", "Story Points", ProjectFieldType.Number, [], []);
        var project = new HarnessProject("PVT_1", 9, "t", "u", [field]);

        await client.SetNumberAsync(project, new ProjectItemRef("PVTI_1"), field, 5);

        var body = handler.Requests.Single().Body;
        Assert.Contains("updateProjectV2ItemFieldValue", body);
        Assert.Contains("\"number\":5", body);
    }
}
