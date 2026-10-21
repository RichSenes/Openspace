using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Offices;
using Openspace.Application.Queue;
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
        services.AddScoped<QueueDispatcher>();
        return services;
    }
}
