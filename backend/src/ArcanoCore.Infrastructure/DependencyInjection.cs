using System;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
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
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IStorageService, SeaweedStorageService>();
        services.AddScoped<IMultipartStorage, SeaweedStorageService>();

        // Identity Configuration
        services.AddIdentityCore<IdentityUser<Guid>>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;
            options.User.RequireUniqueEmail = true;
            options.User.AllowedUserNameCharacters = ""; // UserName is the email; empty turns the character check off
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
            options.TokenValidationParameters = Validation(jwtSettings["Audience"] ?? "ArcanoCoreUsers");
            options.Events = new JwtBearerEvents { OnTokenValidated = RejectRevokedAsync };
        })
        // Download tickets: a different audience, so neither token type is accepted by the other scheme.
        .AddJwtBearer(DownloadTicket.Scheme, options =>
        {
            options.TokenValidationParameters = Validation(DownloadTicket.Audience);
            options.Events = new JwtBearerEvents
            {
                // Only from the query; NoResult stops the handler falling back to the Authorization header.
                OnMessageReceived = context =>
                {
                    var ticket = context.Request.Query[DownloadTicket.QueryKey].ToString();
                    if (string.IsNullOrEmpty(ticket)) context.NoResult();
                    else context.Token = ticket;
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    if (!DownloadTicket.IsValidFor(context.Principal!, context.Request))
                    {
                        context.Fail("Ticket is not valid for this request.");
                        return Task.CompletedTask;
                    }
                    return RejectRevokedAsync(context);
                }
            };
        });

        TokenValidationParameters Validation(string audience) => new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"] ?? "ArcanoCore",
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ClockSkew = TimeSpan.Zero
        };

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }

    // Reject tokens whose security stamp no longer matches (logout / password change) or whose user is gone or disabled (a brute-force lockout does not count).
    // Tokens without the claim (issued before this check existed) are invalid.
    private static async Task RejectRevokedAsync(TokenValidatedContext context)
    {
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var stamp = context.Principal?.FindFirstValue(AuthClaims.SecurityStamp);
        var user = await userManager.FindByIdAsync(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
        if (!UserAccountState.IsTokenValid(user, stamp))
        {
            context.Fail("Token is no longer valid.");
        }
    }
}
