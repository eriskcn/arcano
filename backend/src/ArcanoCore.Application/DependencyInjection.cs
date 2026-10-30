using Microsoft.Extensions.DependencyInjection;
using ArcanoCore.Application.Services;
using ArcanoCore.Application.Common.Interfaces;
using System;

namespace ArcanoCore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IFileService, FileService>();
        return services;
    }
}
