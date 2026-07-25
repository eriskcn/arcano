using Amazon.S3;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Infrastructure.Data;
using ArcanoCore.Infrastructure.Data.Repositories;
using ArcanoCore.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArcanoCore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database Configuration
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString,
                b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // SeaweedFS S3 Storage Configuration
        var storageSettings = configuration.GetSection("Storage");
        var serviceUrl = storageSettings["ServiceUrl"];
        var accessKey = storageSettings["AccessKey"];
        var secretKey = storageSettings["SecretKey"];

        var s3Config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true, // Required for SeaweedFS S3 gateway
            SignatureVersion = "4"
        };

        services.AddSingleton<IAmazonS3>(new AmazonS3Client(accessKey, secretKey, s3Config));
        services.AddScoped<IStorageService, SeaweedStorageService>();

        // Authentication & Current User stub
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }
}
