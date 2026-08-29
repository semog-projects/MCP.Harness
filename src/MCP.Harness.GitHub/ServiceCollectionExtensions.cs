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
        var gitHubOptions = services.AddOptions<GitHubOptions>();
        var harnessOptions = services.AddOptions<HarnessOptions>();
        if (configuration is not null)
        {
            gitHubOptions.Bind(configuration.GetSection(GitHubOptions.SectionName));
            harnessOptions.Bind(configuration.GetSection(HarnessOptions.SectionName));
        }

        // Atalhos por variável de ambiente (paridade com o bootstrap.sh).
        harnessOptions.PostConfigure(static options =>
        {
            if (Environment.GetEnvironmentVariable("HARNESS_TEMPLATE_OWNER") is { Length: > 0 } owner)
            {
                options.TemplateOwner = owner;
            }

            if (int.TryParse(Environment.GetEnvironmentVariable("HARNESS_TEMPLATE_NUMBER"), out var number))
            {
                options.TemplateNumber = number;
            }
        });

        services.TryAddSingleton<GitHubTokenProvider>();
        services.TryAddTransient<GitHubAuthHandler>();

        services.AddHttpClient<IssuesClient>(ConfigureRest).AddHttpMessageHandler<GitHubAuthHandler>();
        services.AddHttpClient<GraphQlClient>(ConfigureGraphQl).AddHttpMessageHandler<GitHubAuthHandler>();

        // Transient: dependem de HttpClients tipados (transient) — não podem
        // ser capturados por um singleton sob risco de reter handlers.
        services.TryAddTransient<ProjectsV2Client>();
        services.TryAddTransient<GitHubClient>();
        services.TryAddTransient<BootstrapService>();
        services.TryAddTransient<TaskService>();

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
