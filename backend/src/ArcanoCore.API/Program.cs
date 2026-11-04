using ArcanoCore.API.Authorization;
using ArcanoCore.API.Middleware;
using ArcanoCore.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using ArcanoCore.Application;
using ArcanoCore.Application.Services;
using ArcanoCore.Infrastructure;
using ArcanoCore.Infrastructure.Data;
using ArcanoCore.Infrastructure.Jobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        var defaultFactory = options.InvalidModelStateResponseFactory;
        // shortcut: matches the framework's message text; there is no typed signal for an oversized form body.
        options.InvalidModelStateResponseFactory = context =>
            context.ModelState.Values.SelectMany(v => v.Errors).Any(e => e.ErrorMessage.Contains("body too large") || e.ErrorMessage.Contains("body length limit"))
                ? new ObjectResult(new { code = "File.TooLarge", description = "File is too large." }) { StatusCode = StatusCodes.Status413PayloadTooLarge }
                : defaultFactory(context);
    });
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:4200" })
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ArcanoCore API",
        Version = "v1",
        Description = "ArcanoCore Cloud Storage REST API"
    });

    // Configure JWT Bearer in Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT Bearer token"
    });

    options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

// Clean Architecture Layers Dependency Injection
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("public-info", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("public-download", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    // Per signed-in user (IP when anonymous). Needs UseAuthentication before UseRateLimiter.
    // shortcut: fixed per-process counters, not shared across instances; use a distributed limiter if scaled out.
    options.AddPolicy("user-api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
    // Tighter: POST UserShares reveals whether an email is registered.
    options.AddPolicy("user-share-create", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

// Upload size limit: one value drives Kestrel and the multipart form reader (both default to lower caps).
var maxUploadBytes = builder.Configuration.GetValue<long?>("Storage:MaxUploadBytes") ?? 104857600;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxUploadBytes + 1048576); // + multipart overhead
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUploadBytes);
builder.Services.AddApplication(builder.Configuration.GetValue<long?>("Storage:QuotaBytes") ?? StorageQuotaService.DefaultLimitBytes);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminPolicy.Name, policy => policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));
builder.Services.AddScoped<IAuthorizationHandler, AdminHandler>();
builder.Services.AddSingleton<LoginThrottle>();

// Persist DataProtection keys in containers (volume mounted at /app/keys); outside containers use the framework default.
var keysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ? "/app/keys" : null);
if (keysPath != null)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddCleanupJob(builder.Configuration);

var app = builder.Build();

// shortcut: runs on every instance start, so with several API instances a deploy could race; use a one-off migrate step if that is ever needed.
if (builder.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
            break;
        }
        catch (Exception ex)
        {
            if (attempt >= 5)
            {
                throw;
            }
            app.Logger.LogWarning(ex, "Database migration attempt {Attempt} failed; retrying in 2 seconds.", attempt);
            Thread.Sleep(TimeSpan.FromSeconds(2));
        }
    }
}

using (var scope = app.Services.CreateScope())
{
    await AdminBootstrap.RunAsync(
        scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
        scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>(),
        builder.Configuration["Auth:BootstrapAdminEmail"],
        app.Logger);
}

// Configure the HTTP request pipeline.
// Forwarded headers are trusted only from proxies the operator lists; otherwise clients could spoof X-Forwarded-For past the rate limiter.
var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>();
if (knownProxies.Length > 0)
{
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    foreach (var proxy in knownProxies)
    {
        forwardedOptions.KnownProxies.Add(IPAddress.Parse(proxy));
    }
    app.UseForwardedHeaders(forwardedOptions);
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ArcanoCore API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();
