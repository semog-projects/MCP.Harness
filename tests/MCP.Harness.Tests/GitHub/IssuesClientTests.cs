using System.Net;
using MCP.Harness.GitHub;
using MCP.Harness.Tests.Fakes;

namespace MCP.Harness.Tests.GitHub;

public class IssuesClientTests
{
    private static IssuesClient ClientFor(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") });

    [Fact]
    public async Task CreateAsync_posts_payload_and_maps_response()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            HttpStatusCode.Created,
            """
            { "number": 42, "node_id": "I_kwABC", "html_url": "https://github.com/o/r/issues/42",
              "state": "open", "title": "Nova task" }
            """));

        var issue = await ClientFor(handler).CreateAsync(
            new RepoRef("o", "r"), "Nova task", "corpo", type: "Task");

        Assert.Equal(42, issue.Number);
        Assert.Equal("I_kwABC", issue.NodeId);
        Assert.Equal("https://github.com/o/r/issues/42", issue.Url);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/repos/o/r/issues", request.RequestUri!.AbsolutePath);
        Assert.Contains("\"type\":\"Task\"", request.Body);
    }

    [Fact]
    public async Task CloseAsync_sends_state_reason()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{ "number": 7, "node_id": "I_x", "html_url": "u", "state": "closed", "title": "t" }"""));

        var issue = await ClientFor(handler).CloseAsync(new RepoRef("o", "r"), 7);

        Assert.Equal("closed", issue.State);
        Assert.Contains("\"state_reason\":\"completed\"", handler.Requests.Single().Body);
    }

    [Fact]
    public async Task Rate_limit_response_becomes_actionable_exception()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            HttpStatusCode.Forbidden,
            """{ "message": "API rate limit exceeded" }""",
            ("x-ratelimit-remaining", "0"),
            ("x-ratelimit-reset", "4102444800")));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => ClientFor(handler).GetAsync(new RepoRef("o", "r"), 1));

        Assert.True(ex.IsRateLimit);
        Assert.Contains("rate limit", ex.Actionable, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Forbidden_without_rate_limit_reports_missing_scopes()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            HttpStatusCode.Forbidden,
            """{ "message": "Resource not accessible by personal access token" }"""));

        var ex = await Assert.ThrowsAsync<GitHubApiException>(
            () => ClientFor(handler).GetAsync(new RepoRef("o", "r"), 1));

        Assert.True(ex.IsPermission);
        Assert.Contains("escopos", ex.Actionable);
    }
}
