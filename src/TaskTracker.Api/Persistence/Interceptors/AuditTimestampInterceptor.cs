using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskTracker.Api.Domain.Entities;

namespace TaskTracker.Api.Persistence.Interceptors;

/// <summary>
/// Interceptor ensuring audit timestamps and application-managed row versions
/// are updated consistently on save operations using TimeProvider.
/// </summary>
public sealed class AuditTimestampInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var utcNow = timeProvider.GetUtcNow();

        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is Project)
                {
                    entry.Property(nameof(Project.UpdatedAt)).CurrentValue = utcNow;
                    var currentVersion = (uint)(entry.Property(nameof(Project.RowVersion)).CurrentValue ?? 1u);
                    entry.Property(nameof(Project.RowVersion)).CurrentValue = currentVersion + 1;
                }
                else if (entry.Entity is TaskItem)
                {
                    entry.Property(nameof(TaskItem.UpdatedAt)).CurrentValue = utcNow;
                    var currentVersion = (uint)(entry.Property(nameof(TaskItem.RowVersion)).CurrentValue ?? 1u);
                    entry.Property(nameof(TaskItem.RowVersion)).CurrentValue = currentVersion + 1;
                }
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
