using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.LocalModels;
using Openspace.Application.Routines;
using Openspace.Application.Queue;
using Openspace.Infrastructure.ClaudeCode;
using Openspace.Infrastructure.Git;
using Openspace.Infrastructure.Hosting;
using Openspace.Infrastructure.LocalModels;
using Openspace.Infrastructure.OpenCode;
using Openspace.Infrastructure.Persistence;

namespace Openspace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<OpenspaceDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OpenspaceDbContext>());
        services.AddScoped<IOfficeRepository, OfficeRepository>();
        services.AddScoped<IWorkTaskRepository, WorkTaskRepository>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IEpicRepository, EpicRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IRoutineRepository, RoutineRepository>();
        services.TryAddSingleton<IDomainEventPublisher>(NullDomainEventPublisher.Instance);
        services.TryAddSingleton<IActivityPublisher, NullActivityPublisher>();
        services.AddSingleton<IWorkspaceManager, GitWorkspaceManager>();
        services.AddSingleton<ICheckRunner, Checks.ShellCheckRunner>();
        services.AddSingleton<IPullRequests, GitHubPullRequests>();
        services.AddSingleton<IDatabaseBackups>(sp => new SqliteBackups(
            connectionString, sp.GetRequiredService<IOptions<BackupOptions>>(), sp.GetRequiredService<TimeProvider>()));
        services.AddHostedService<BackupService>();
        return services;
    }

    /// <summary>Connects agents: the OpenCode server, its event stream and the background queue.</summary>
    public static IServiceCollection AddAgents(this IServiceCollection services)
    {
        // Planning and drafting wait for the model to answer: allow minutes, not seconds.
        services.AddHttpClient<OpenCodeRuntime>(ConfigureOpenCode)
            .ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<ClaudeCodeRuntime>();
        services.AddSingleton<ClaudeCodeUsage>();
        services.AddScoped<IAgentRuntime, AgentRuntimeRouter>();
        services.AddScoped<IAgentEngines, AgentEngines>();
        services.AddHttpClient(OpenCodeEventListener.HttpClientName, ConfigureOpenCode)
            .ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
        // Plain AddSingleton: AddHostedService would deduplicate two factories of the same return type.
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IOptions<OpenCodeOptions>>().Value.ListenToEvents
            ? ActivatorUtilities.CreateInstance<OpenCodeEventListener>(sp)
            : new IdleService());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IOptions<OpenCodeOptions>>().Value.ListenToEvents
            ? ActivatorUtilities.CreateInstance<OpenCodeStatusSweep>(sp)
            : new IdleService());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IOptions<QueueOptions>>().Value.Enabled
            ? ActivatorUtilities.CreateInstance<QueueRunnerService>(sp)
            : new IdleService());
        services.AddHostedService<ReviewRunnerService>();

        // Routines run in the background like the queue, and like it are off in tests.
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IOptions<QueueOptions>>().Value.Enabled
            ? ActivatorUtilities.CreateInstance<RoutineRunnerService>(sp)
            : new IdleService());

        // The graphics card: local models load when desks need them and give way to the programs listed first.
        services.AddHttpClient(LocalModelServer.HttpClientName).ConfigureHttpClient(http => http.Timeout = TimeSpan.FromSeconds(5));
        services.AddSingleton<IGpuProbe, GpuProbe>();
        services.AddSingleton<ILocalModelServer>(sp => new LocalModelServer(
            sp.GetRequiredService<IHttpClientFactory>(),
            () => sp.GetRequiredService<IOptionsMonitor<LocalModelOptions>>().CurrentValue.ServerUrl,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LocalModelServer>>()));

        // Routines ask the local models through the arbiter, and the decider on the processor directly.
        services.AddHttpClient<ILocalModelApi, LlamaServerApi>().ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(15));
        services.AddHttpClient(DeciderClient.HttpClientName).ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(10));
        services.AddSingleton<DeciderClient>();
        services.AddSingleton<IDecider>(sp => sp.GetRequiredService<DeciderClient>());
        services.AddHostedService<GpuShiftService>();

        // Routines read news feeds and print their pages to PDF with a headless Firefox.
        services.AddHttpClient<IFeedReader, Routines.FeedReader>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Openspace/1.0 (personal news reader)");
        });
        services.AddHttpClient<IWebImages, Routines.WebImages>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Openspace/1.0 (personal news reader)");
        });
        // Job boards are read through the pages they serve to anyone, under the same honest name.
        services.AddHttpClient<IJobBoard, Routines.JapanDevBoard>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Openspace/1.0 (personal news reader)");
        });
        services.AddHttpClient(Routines.FirefoxPdfPrinter.HttpClientName).ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(3));
        services.AddSingleton<IPdfPrinter, Routines.FirefoxPdfPrinter>();

        // The kanji of the day reads open dictionaries kept on this computer.
        services.AddSingleton<IKanjiBook, Routines.KanjiBook>();

        // What routines make reaches the person's own Telegram group.
        services.AddHttpClient<IRoutineOutbox, Telegram.TelegramOutbox>(http =>
        {
            http.BaseAddress = new Uri("https://api.telegram.org/");
            http.Timeout = TimeSpan.FromMinutes(2);
        });

        // Google accounts connected with the person's own OAuth client; their mail for the mail routine.
        services.AddHttpClient(Google.GoogleHttp.Name).ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(1));
        services.AddSingleton(sp => new Google.GoogleAccounts(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(Google.GoogleHttp.Name), sp.GetRequiredService<IOptionsMonitor<Google.GoogleOptions>>(), TimeProvider.System));
        services.AddSingleton<Application.Google.IGoogleAccounts>(sp => sp.GetRequiredService<Google.GoogleAccounts>());
        services.AddHttpClient<IMailbox, Google.GmailMailbox>().ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(2));
        services.AddHttpClient<IAgenda, Google.GoogleAgenda>().ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(1));
        return services;
    }

    private static void ConfigureOpenCode(IServiceProvider services, HttpClient http)
    {
        var baseUrl = services.GetRequiredService<IOptions<OpenCodeOptions>>().Value.BaseUrl.ToString();
        http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
    }

    /// <summary>Creates the database or brings it up to the latest migration.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenspaceDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
