using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskTracker.Api.Persistence;
using TaskTracker.Api.Persistence.Interceptors;

namespace TaskTracker.Api.Infrastructure;

public static class DatabaseExtensions
{
    public static IServiceCollection AddTaskTrackerDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database") ??
            "Host=localhost;Port=5432;Database=tasktracker;Username=tasktracker_app;Password=tasktracker_dev_secret";

        services.AddScoped<IDbCommandCounter, DbCommandCounter>();
        services.AddScoped<AuditTimestampInterceptor>();
        services.AddScoped<DbCommandCountInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var auditInterceptor = sp.GetRequiredService<AuditTimestampInterceptor>();
            var dbCounterInterceptor = sp.GetRequiredService<DbCommandCountInterceptor>();

            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(auditInterceptor, dbCounterInterceptor);
        });

        return services;
    }
}
