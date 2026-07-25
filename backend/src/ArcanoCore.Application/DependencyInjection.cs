using Microsoft.Extensions.DependencyInjection;
using ArcanoCore.Application.Services;

namespace ArcanoCore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IFileService, FileService>();
        return services;
    }
}
