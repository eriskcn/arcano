using ArcanoCore.API.Middleware;
using ArcanoCore.Application;
using ArcanoCore.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Stub Authentication setup
builder.Services.AddAuthentication("StubBearer")
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ArcanoCore.API.Authentication.StubAuthenticationHandler>("StubBearer", null);

// Clean Architecture Layers Dependency Injection
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
