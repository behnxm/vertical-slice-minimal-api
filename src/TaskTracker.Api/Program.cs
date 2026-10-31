using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Api.Common.Authorization;
using TaskTracker.Api.Common.Endpoints;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Infrastructure;
using TaskTracker.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Core framework services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IProjectAccessService, ProjectAccessService>();
builder.Services.AddSingleton(TimeProvider.System);

// Infrastructure composition
builder.Services.ConfigureStrictJson();
builder.Services.AddTaskTrackerDatabase(builder.Configuration);
builder.Services.AddTaskTrackerCaching(builder.Configuration);
builder.Services.AddTaskTrackerAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddTaskTrackerRateLimiting();
builder.Services.AddTaskTrackerTelemetry(builder.Configuration, builder.Environment);
builder.Services.AddTaskTrackerHealthChecks(builder.Configuration);
builder.Services.AddTaskTrackerOpenApi();

// Background maintenance services
builder.Services.AddHostedService<TaskTracker.Api.Infrastructure.Background.SoftDeletePurgeService>();

// Assembly scanning for vertical slice components
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.AddSliceHandlers(typeof(Program).Assembly, builder.Configuration);

var app = builder.Build();

// CLI command execution (handles --migrate, --seed-demo, --seed-perf, --healthcheck)
var cliExitCode = await CliCommandHandler.HandleAsync(args, app);
if (cliExitCode.HasValue)
{
    return cliExitCode.Value;
}

// Development migration hook (never run in Production)
if (app.Configuration.GetValue("Database:MigrateOnStartup", false) && !app.Environment.IsProduction())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Security & cross-cutting middleware
app.UseSecurityHeaders();
app.UseRequestHardening();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Endpoint mappings
app.MapTaskTrackerHealthChecks(app.Configuration);
app.MapTaskTrackerOpenApi();
app.MapSliceEndpoints(typeof(Program).Assembly, app.Configuration);

await app.RunAsync();

return 0;

public partial class Program;
