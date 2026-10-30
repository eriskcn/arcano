using System;
using System.Text;
using Amazon.S3;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Infrastructure.Data;
using ArcanoCore.Infrastructure.Security;
using ArcanoCore.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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

        // Identity Configuration
        services.AddIdentityCore<IdentityUser<Guid>>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
            options.User.RequireUniqueEmail = true;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        services.AddSingleton<ILookupNormalizer, CustomLookupNormalizer>();

        // JWT Authentication Configuration
        var jwtSettings = configuration.GetSection("Jwt");
        var secret = jwtSettings["Secret"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Jwt:Secret must be configured and contain at least 32 UTF-8 bytes.");
        var key = Encoding.UTF8.GetBytes(secret);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"] ?? "ArcanoCore",
                ValidAudience = jwtSettings["Audience"] ?? "ArcanoCoreUsers",
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
            };
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }
}
