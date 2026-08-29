using System.ComponentModel;
using System.Text.Json;
using MCP.Harness.GitHub;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace MCP.Harness.Resources;

/// <summary>Configuração efetiva do servidor, exposta como resource (sem segredos).</summary>
[McpServerResourceType]
public sealed class ConfigResources
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [McpServerResource(
        UriTemplate = "harness://config",
        Name = "harness-config",
        Title = "Configuração efetiva do MCP.Harness",
        MimeType = "application/json")]
    [Description(
        "Configuração em uso: URLs da API, owner/número do Project-template, repo padrão, campos " +
        "obrigatórios e a FONTE do token do GitHub (nunca o valor).")]
    public static string Config(
        IOptions<GitHubOptions> gitHub,
        IOptions<HarnessOptions> harness,
        GitHubTokenProvider tokenProvider)
    {
        var g = gitHub.Value;
        var h = harness.Value;

        var payload = new
        {
            github = new
            {
                restBaseUrl = g.RestBaseUrl,
                graphQlUrl = g.GraphQlUrl,
                userAgent = g.UserAgent,
                allowGhCliTokenFallback = g.AllowGhCliTokenFallback,
                tokenSource = tokenProvider.DescribeSource(),
            },
            harness = new
            {
                templateOwner = h.TemplateOwner,
                templateNumber = h.TemplateNumber,
                defaultRepo = h.DefaultRepo,
                requiredFields = h.RequiredFields,
            },
        };

        return JsonSerializer.Serialize(payload, Json);
    }
}
