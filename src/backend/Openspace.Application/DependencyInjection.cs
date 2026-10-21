using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Agents;
using Openspace.Application.Offices;
using Openspace.Application.Queue;
using Openspace.Application.Reports;
using Openspace.Application.Roles;
using Openspace.Application.Work;

namespace Openspace.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OfficeCommands>();
        services.AddScoped<OfficeQueries>();
        services.AddScoped<WorkTaskCommands>();
        services.AddScoped<WorkTaskQueries>();
        services.AddScoped<Supervisor>();
        services.AddScoped<EpicQueries>();
        services.AddScoped<ReportWriter>();
        services.AddScoped<ReportQueries>();
        services.AddScoped<QueueDispatcher>();
        services.AddScoped<RoleCommands>();
        services.AddScoped<RoleQueries>();
        services.AddScoped<AgentSetups>();
        services.AddScoped<TaskRunner>();
        services.AddScoped<AgentSignals>();
        services.AddSingleton<ActivityBoard>();
        return services;
    }
}
