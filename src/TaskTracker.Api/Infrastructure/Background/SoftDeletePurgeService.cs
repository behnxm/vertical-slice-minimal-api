using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Infrastructure.Background;

/// <summary>
/// Background maintenance service that permanently removes soft-deleted tasks older than the configured retention threshold.
/// Explicitly allowlisted in architecture tests to bypass named query filters via IgnoreQueryFilters().
/// </summary>
public sealed partial class SoftDeletePurgeService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SoftDeletePurgeService> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);

    public TimeSpan Interval { get; set; } = DefaultInterval;
    public TimeSpan RetentionPeriod { get; set; } = DefaultRetention;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalConfig = configuration.GetValue<int?>("Maintenance:PurgeIntervalSeconds");
        if (intervalConfig.HasValue)
        {
            Interval = TimeSpan.FromSeconds(intervalConfig.Value);
        }

        var retentionConfig = configuration.GetValue<int?>("Maintenance:RetentionDays");
        if (retentionConfig.HasValue)
        {
            RetentionPeriod = TimeSpan.FromDays(retentionConfig.Value);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredTasksAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogPurgeFailed(logger, ex);
            }

            try
            {
                await Task.Delay(Interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<int> PurgeExpiredTasksAsync(CancellationToken cancellationToken = default)
    {
        var threshold = timeProvider.GetUtcNow().Subtract(RetentionPeriod);

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Allowlisted: Purge requires accessing soft-deleted records across all projects
        var expiredTasks = await dbContext.Tasks
            .IgnoreQueryFilters()
            .Where(t => t.DeletedAt != null && t.DeletedAt < threshold)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (expiredTasks.Count == 0)
        {
            return 0;
        }

        dbContext.Tasks.RemoveRange(expiredTasks);
        await dbContext.SaveChangesAsync(cancellationToken);

        LogPurgedTasks(logger, expiredTasks.Count);
        return expiredTasks.Count;
    }

    [LoggerMessage(LogLevel.Information, "SoftDeletePurgeService permanently deleted {Count} expired soft-deleted task(s).")]
    private static partial void LogPurgedTasks(ILogger logger, int count);

    [LoggerMessage(LogLevel.Error, "SoftDeletePurgeService encountered an error while purging expired tasks.")]
    private static partial void LogPurgeFailed(ILogger logger, Exception ex);
}
