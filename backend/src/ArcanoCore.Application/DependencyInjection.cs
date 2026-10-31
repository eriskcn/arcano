using Microsoft.Extensions.DependencyInjection;
using ArcanoCore.Application.Services;
using ArcanoCore.Application.Common.Interfaces;
using System;

namespace ArcanoCore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, long storageQuotaBytes = StorageQuotaService.DefaultLimitBytes)
    {
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IShareService, ShareService>();
        services.AddScoped<IStorageQuotaService>(sp => new StorageQuotaService(sp.GetRequiredService<IApplicationDbContext>(), storageQuotaBytes));
        return services;
    }
}
