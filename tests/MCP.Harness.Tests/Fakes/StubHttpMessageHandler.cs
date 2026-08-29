using System.Net;

namespace MCP.Harness.Tests.Fakes;

/// <summary>Responde cada requisição com o resultado de um delegate.</summary>
public sealed class StubHttpMessageHandler(Func<CapturedRequest, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    /// <summary>
    /// Requisições recebidas, com o corpo já materializado em string — o
    /// <see cref="HttpRequestMessage.Content"/> é descartado pelo HttpClient
    /// assim que a requisição completa.
    /// </summary>
    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var captured = new CapturedRequest(request.Method, request.RequestUri, body);
        Requests.Add(captured);

        return responder(captured);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}

public sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, string? Body);
