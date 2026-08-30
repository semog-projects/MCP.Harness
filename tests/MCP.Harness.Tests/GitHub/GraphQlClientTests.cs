using System.Net;
using System.Text.Json;
using MCP.Harness.GitHub;
using MCP.Harness.Tests.Fakes;

namespace MCP.Harness.Tests.GitHub;

public class GraphQlClientTests
{
    private static GraphQlClient ClientReturning(string body)
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));
        return new GraphQlClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/graphql") });
    }

    // Resposta real do GitHub ao sondar organization+user quando o login é uma org.
    private const string OrgUserProbe = """
        { "data": { "organization": { "id": "O_1" }, "user": null },
          "errors": [ { "type": "NOT_FOUND", "path": ["user"],
                        "message": "Could not resolve to a User with the login of 'x'." } ] }
        """;

    [Fact]
    public async Task Partial_NOT_FOUND_errors_are_tolerated_when_data_is_present()
    {
        var data = await ClientReturning(OrgUserProbe).ExecuteAsync(
            "q", null, "probe", default, allowPartialErrors: true);

        Assert.Equal("O_1", data.GetProperty("organization").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("user").ValueKind);
    }

    [Fact]
    public async Task Partial_errors_still_throw_without_the_flag()
    {
        var ex = await Assert.ThrowsAsync<GitHubGraphQlException>(() =>
            ClientReturning(OrgUserProbe).ExecuteAsync("q", null, "probe", default));

        Assert.Contains("Could not resolve to a User", ex.Message);
    }

    [Fact]
    public async Task Non_NOT_FOUND_errors_throw_even_with_the_flag()
    {
        const string body = """
            { "data": { "organization": null, "user": null },
              "errors": [ { "type": "FORBIDDEN", "message": "Resource not accessible by integration." } ] }
            """;

        var ex = await Assert.ThrowsAsync<GitHubGraphQlException>(() =>
            ClientReturning(body).ExecuteAsync("q", null, "probe", default, allowPartialErrors: true));

        Assert.Contains("Resource not accessible", ex.Message);
    }

    [Fact]
    public async Task Missing_data_throws_even_with_the_flag()
    {
        const string body = """
            { "data": null, "errors": [ { "type": "NOT_FOUND", "message": "nope" } ] }
            """;

        await Assert.ThrowsAsync<GitHubGraphQlException>(() =>
            ClientReturning(body).ExecuteAsync("q", null, "probe", default, allowPartialErrors: true));
    }
}
