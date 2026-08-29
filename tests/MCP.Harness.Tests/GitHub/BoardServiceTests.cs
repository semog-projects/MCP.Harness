using System.Net;
using MCP.Harness.GitHub;
using MCP.Harness.Tests.Fakes;

namespace MCP.Harness.Tests.GitHub;

public class BoardServiceTests
{
    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private static string Board => $$"""
        { "id": "PVT_9", "number": 9, "title": "MCP.Harness Sprints", "url": "https://github.com/orgs/o/projects/9",
          "fields": { "nodes": [
            { "__typename": "ProjectV2SingleSelectField", "id": "F_status", "name": "Status", "dataType": "SINGLE_SELECT",
              "options": [ { "id": "b", "name": "Backlog" }, { "id": "t", "name": "Todo" },
                           { "id": "d", "name": "Doing" }, { "id": "e", "name": "Done" } ] },
            { "__typename": "ProjectV2IterationField", "id": "F_sprint", "name": "Sprint", "dataType": "ITERATION",
              "configuration": { "iterations": [ { "id": "it1", "title": "Sprint 1", "startDate": "{{Today}}", "duration": 14 } ],
                "completedIterations": [] } },
            { "__typename": "ProjectV2Field", "id": "F_sp", "name": "Story Points", "dataType": "NUMBER" }
          ] } }
        """;

    private static string Item(int n, string status, string? sprint, double? sp) =>
        $$"""
        { "content": { "__typename": "Issue", "number": {{n}}, "title": "Issue {{n}}", "url": "https://x/{{n}}",
            "state": "OPEN", "assignees": { "nodes": [] } },
          "status": { "name": "{{status}}" },
          "sprint": {{(sprint is null ? "null" : $"{{ \"title\": \"{sprint}\" }}")}},
          "points": {{(sp is null ? "null" : $"{{ \"number\": {sp} }}")}} }
        """;

    private static BoardService Build(Func<string, HttpResponseMessage> route)
    {
        var handler = new StubHttpMessageHandler(req => route(req.Body ?? string.Empty));
        var gql = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/graphql") };
        return new BoardService(new ProjectsV2Client(new GraphQlClient(gql)));
    }

    private static HttpResponseMessage Ok(string data) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{ "data": {{data}} }""");

    private static HttpResponseMessage Items(params string[] nodes) => Ok($$"""
        { "node": { "items": { "pageInfo": { "hasNextPage": false, "endCursor": null },
          "nodes": [ {{string.Join(",", nodes)}} ] } } }
        """);

    [Fact]
    public async Task Groups_by_status_in_board_order_and_sums_points_for_current_sprint()
    {
        var service = Build(body => body switch
        {
            _ when body.Contains("projectsV2(first: 20)") =>
                Ok($$"""{ "repository": { "projectsV2": { "nodes": [ {{Board}} ] } } }"""),
            _ when body.Contains("items(first: 100") => Items(
                Item(1, "Done", "Sprint 1", 3),
                Item(2, "Doing", "Sprint 1", 5),
                Item(3, "Backlog", "Sprint 1", 2),
                Item(4, "Done", "Sprint 1", 1),
                Item(9, "Backlog", "Sprint 2", 8)),
            _ => throw new InvalidOperationException(body),
        });

        var snap = await service.GetBoardAsync(new RepoRef("o", "r"));

        Assert.Equal("Sprint 1", snap.Sprint);
        Assert.Equal(new[] { "Backlog", "Todo", "Doing", "Done" }, snap.Columns.Select(c => c.Status));
        Assert.Equal(4, snap.ItemCount);                       // Sprint 2 item excluído
        Assert.Equal(11, snap.StoryPoints);                    // 3+5+2+1
        Assert.Equal(4, snap.Columns.Single(c => c.Status == "Done").StoryPoints);
        Assert.Empty(snap.Columns.Single(c => c.Status == "Todo").Items);
    }

    [Fact]
    public async Task Explicit_sprint_filter_wins_over_current()
    {
        var service = Build(body => body.Contains("projectsV2(first: 20)")
            ? Ok($$"""{ "repository": { "projectsV2": { "nodes": [ {{Board}} ] } } }""")
            : Items(Item(1, "Done", "Sprint 1", 3), Item(9, "Backlog", "Sprint 2", 8)));

        var snap = await service.GetBoardAsync(new RepoRef("o", "r"), sprint: "Sprint 2");

        Assert.Equal("Sprint 2", snap.Sprint);
        Assert.Equal(9, Assert.Single(snap.Columns.SelectMany(c => c.Items)).Number);
    }

    [Fact]
    public async Task Non_standard_status_becomes_an_extra_column_at_the_end()
    {
        var service = Build(body => body.Contains("projectsV2(first: 20)")
            ? Ok($$"""{ "repository": { "projectsV2": { "nodes": [ {{Board}} ] } } }""")
            : Items(Item(1, "Blocked", "Sprint 1", 2)));

        var snap = await service.GetBoardAsync(new RepoRef("o", "r"));

        Assert.Equal("Blocked", snap.Columns[^1].Status);
        Assert.Equal(1, snap.Columns[^1].Items.Single().Number);
    }
}
