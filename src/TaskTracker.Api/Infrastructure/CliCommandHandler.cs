using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskTracker.Api.Domain.Entities;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Persistence;

namespace TaskTracker.Api.Infrastructure;

/// <summary>
/// Handles one-shot CLI commands before the web host starts (e.g. database migrations, seeding, container healthcheck).
/// </summary>
public static class CliCommandHandler
{
    public static async Task<int?> HandleAsync(string[] args, IHost host)
    {
        if (args.Length == 0)
        {
            return null; // No CLI switch; proceed with web application startup
        }

        var command = args[0];

        switch (command)
        {
            case "--migrate":
                return await RunMigrationAsync(host);

            case "--seed-demo":
                return await RunSeedDemoAsync(host);

            case "--seed-perf":
                var taskCount = 10_000;
                var taskIndex = Array.IndexOf(args, "--tasks");
                if (taskIndex >= 0 && taskIndex + 1 < args.Length && int.TryParse(args[taskIndex + 1], out var parsedCount))
                {
                    taskCount = parsedCount;
                }
                return await RunSeedPerfAsync(host, taskCount);

            case "--healthcheck":
                return await RunHealthCheckProbeAsync();

            default:
                return null;
        }
    }

    private static async Task<int> RunMigrationAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Console.WriteLine("[CLI] Applying database migrations...");
        try
        {
            await db.Database.MigrateAsync();
            Console.WriteLine("[CLI] Database migrations applied successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CLI ERROR] Database migration failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunSeedDemoAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (env.IsProduction())
        {
            Console.Error.WriteLine("[CLI ERROR] Seeding demo data is strictly prohibited in Production.");
            return 1;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        Console.WriteLine("[CLI] Seeding demo dataset...");

        // Ensure database schema is ready
        await db.Database.MigrateAsync();

        // Check if demo data already exists
        if (await db.Projects.IgnoreQueryFilters().AnyAsync())
        {
            Console.WriteLine("[CLI] Database already contains records; skipping demo seed.");
            return 0;
        }

        var projectResult = Project.Create("Core Backend Platform", "Main platform backend services and APIs", "dev-owner-user", timeProvider);
        if (projectResult.IsFailure) return 1;
        var project = projectResult.Value;

        var ownerMember = ProjectMember.Create(project.Id, "dev-owner-user", ProjectRole.Owner, "dev-owner-user", timeProvider).Value;
        var editorMember = ProjectMember.Create(project.Id, "dev-editor-user", ProjectRole.Editor, "dev-owner-user", timeProvider).Value;
        var viewerMember = ProjectMember.Create(project.Id, "dev-viewer-user", ProjectRole.Viewer, "dev-owner-user", timeProvider).Value;

        var task1 = TaskItem.Create(
            project.Id,
            "Implement Keyset Pagination",
            "Replace offset pagination with opaque URL-safe base64 cursors.",
            TaskPriority.High,
            timeProvider.GetUtcNow().AddDays(3),
            "dev-editor-user",
            ["api", "performance"],
            "dev-owner-user",
            timeProvider).Value;

        var task2 = TaskItem.Create(
            project.Id,
            "Configure HybridCache L2 Valkey",
            "Integrate multi-tier caching with explicit key invalidations.",
            TaskPriority.Urgent,
            timeProvider.GetUtcNow().AddDays(1),
            "dev-owner-user",
            ["cache", "valkey"],
            "dev-owner-user",
            timeProvider).Value;

        var task3 = TaskItem.Create(
            project.Id,
            "Initial System Architecture Review",
            "Review vertical slice boundaries and named query filters.",
            TaskPriority.Medium,
            timeProvider.GetUtcNow().AddDays(-2),
            "dev-editor-user",
            ["architecture", "review"],
            "dev-owner-user",
            timeProvider).Value;

        task3.TransitionStatus(TaskItemStatus.InProgress, timeProvider);
        task3.TransitionStatus(TaskItemStatus.Done, timeProvider);

        var comment = TaskComment.Create(
            task1.Id,
            "dev-editor-user",
            "Started work on the Base64Url cursor encoding and tie-breaker sorting.",
            timeProvider).Value;

        db.Projects.Add(project);
        db.ProjectMembers.AddRange(ownerMember, editorMember, viewerMember);
        db.Tasks.AddRange(task1, task2, task3);
        db.Comments.Add(comment);

        await db.SaveChangesAsync();
        Console.WriteLine("[CLI] Demo dataset seeded successfully.");
        return 0;
    }

    private static async Task<int> RunSeedPerfAsync(IHost host, int taskCount)
    {
        using var scope = host.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (env.IsProduction())
        {
            Console.Error.WriteLine("[CLI ERROR] Performance dataset seeding is strictly prohibited in Production.");
            return 1;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        Console.WriteLine($"[CLI] Seeding performance dataset with {taskCount} tasks...");
        await db.Database.MigrateAsync();

        var project = Project.Create("Performance Benchmark Workspace", "Large scale dataset for query-plan verification", "perf-tester", timeProvider).Value;
        var member = ProjectMember.Create(project.Id, "perf-tester", ProjectRole.Owner, "perf-tester", timeProvider).Value;

        db.Projects.Add(project);
        db.ProjectMembers.Add(member);
        await db.SaveChangesAsync();

        var batchSize = 1000;
        var priorities = new[] { TaskPriority.Low, TaskPriority.Medium, TaskPriority.High, TaskPriority.Urgent };
        var labelPool = new[] { "backend", "api", "database", "security", "infra", "auth", "frontend", "cache" };

        for (var i = 0; i < taskCount; i += batchSize)
        {
            var tasks = new List<TaskItem>();
            var currentBatch = Math.Min(batchSize, taskCount - i);

            for (var j = 0; j < currentBatch; j++)
            {
                var index = i + j;
                var priority = priorities[index % priorities.Length];
                var labels = new[] { labelPool[index % labelPool.Length], labelPool[(index + 2) % labelPool.Length] };

                var task = TaskItem.Create(
                    project.Id,
                    $"Task #{index:D6}: Optimization and telemetry enhancement for service component",
                    $"Detailed performance stress testing specification for batch item #{index:D6}. Generated tsvector search vector verification.",
                    priority,
                    timeProvider.GetUtcNow().AddDays((index % 30) - 10),
                    "perf-tester",
                    labels,
                    "perf-tester",
                    timeProvider).Value;

                if (index % 3 == 0)
                {
                    task.TransitionStatus(TaskItemStatus.InProgress, timeProvider);
                }
                else if (index % 5 == 0)
                {
                    task.TransitionStatus(TaskItemStatus.InProgress, timeProvider);
                    task.TransitionStatus(TaskItemStatus.Done, timeProvider);
                }

                tasks.Add(task);
            }

            db.Tasks.AddRange(tasks);
            await db.SaveChangesAsync();
            Console.WriteLine($"[CLI] Inserted {i + currentBatch}/{taskCount} tasks...");
        }

        Console.WriteLine("[CLI] Performance dataset seeded successfully.");
        return 0;
    }

    private static async Task<int> RunHealthCheckProbeAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync("http://127.0.0.1:8080/health/live");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }
}
