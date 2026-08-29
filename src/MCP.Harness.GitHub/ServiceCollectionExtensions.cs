using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MCP.Harness.GitHub;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra o acesso ao GitHub: <see cref="GitHubClient"/>,
    /// <see cref="IssuesClient"/>, <see cref="ProjectsV2Client"/> e o
    /// <see cref="GitHubTokenProvider"/>, com os HttpClients tipados e o
    /// handler de autenticação.
    /// </summary>
    public static IServiceCollection AddHarnessGitHub(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        var optionsBuilder = services.AddOptions<GitHubOptions>();
        if (configuration is not null)
        {
            optionsBuilder.Bind(configuration.GetSection(GitHubOptions.SectionName));
        }

        services.TryAddSingleton<GitHubTokenProvider>();
        services.TryAddTransient<GitHubAuthHandler>();

        services.AddHttpClient<IssuesClient>(ConfigureRest).AddHttpMessageHandler<GitHubAuthHandler>();
        services.AddHttpClient<GraphQlClient>(ConfigureGraphQl).AddHttpMessageHandler<GitHubAuthHandler>();

        // Transient: dependem de HttpClients tipados (transient) — não podem
        // ser capturados por um singleton sob risco de reter handlers.
        services.TryAddTransient<ProjectsV2Client>();
        services.TryAddTransient<GitHubClient>();

        return services;

        static void ConfigureRest(IServiceProvider sp, HttpClient http)
        {
            var options = sp.GetRequiredService<IOptions<GitHubOptions>>().Value;
            http.BaseAddress = new Uri(options.RestBaseUrl);
        }

        static void ConfigureGraphQl(IServiceProvider sp, HttpClient http)
        {
            var options = sp.GetRequiredService<IOptions<GitHubOptions>>().Value;
            http.BaseAddress = new Uri(options.GraphQlUrl);
        }
    }
}
