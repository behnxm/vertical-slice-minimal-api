using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TaskTracker.Api.Common.Caching;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Features.Projects;
using TaskTracker.Api.Features.Tasks;
using TaskTracker.Api.Infrastructure.Background;
using TaskTracker.Api.Persistence;
using TaskTracker.IntegrationTests.Infrastructure;
using Xunit;

namespace TaskTracker.IntegrationTests;

public class HealthAndObservabilityTests(ContainersFixture containers) : BaseIntegrationTest(containers)
{
    [Fact]
    public async Task Liveness_And_Readiness_HealthChecks_Return_Healthy_When_Online()
    {
        var client = Factory.CreateAnonymousClient();

        // 1. Liveness Probe -> 200 OK
        var liveRes = await client.GetAsync("/health/live");
        liveRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 2. Readiness Probe -> 200 OK with database healthy
        var readyRes = await client.GetAsync("/health/ready");
        readyRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var readyBody = await readyRes.Content.ReadAsStringAsync();
        readyBody.ShouldContain("Healthy");
        readyBody.ShouldContain("database");
    }

    [Fact]
    public async Task Readiness_Reports_Degraded_Honest_Fallback_When_Cache_Is_Unhealthy()
    {
        var client = Factory.CreateAnonymousClient();
        var tracker = Factory.Services.GetRequiredService<ICacheHealthTracker>();

        try
        {
            tracker.MarkDegraded();

            var readyRes = await client.GetAsync("/health/ready");
            // Under degraded cache, readiness still returns 200 OK because DB fallback is active
            readyRes.StatusCode.ShouldBe(HttpStatusCode.OK);

            var readyBody = await readyRes.Content.ReadAsStringAsync();
            readyBody.ShouldContain("Degraded");
            readyBody.ShouldContain("valkey_cache");
        }
        finally
        {
            tracker.MarkHealthy();
        }
    }

    [Fact]
    public async Task SoftDeletePurgeService_Permanently_Removes_Tasks_Past_Retention_Period()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Purge Project"));
        var project = await projectRes.Content.ReadStrictJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        var taskRes = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Task to be purged", null, TaskTracker.Api.Domain.Enums.TaskPriority.Low, null, null, null));
        var task = await taskRes.Content.ReadStrictJsonAsync<CreateTask.Response>();
        task.ShouldNotBeNull();

        // Soft delete the task
        var deleteRes = await ownerClient.DeleteAsync($"/api/v1/tasks/{task.Id}");
        deleteRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Advance simulated time beyond 30-day retention
        Factory.TimeProvider.Advance(TimeSpan.FromDays(35));

        // Execute purge
        using var scope = Factory.Services.CreateScope();
        var purgeService = new SoftDeletePurgeService(
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),
            Factory.TimeProvider,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SoftDeletePurgeService>>());

        var purgedCount = await purgeService.PurgeExpiredTasksAsync();
        purgedCount.ShouldBe(1);

        // Verify permanent deletion from database
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remaining = await db.Tasks.IgnoreQueryFilters().CountAsync(t => t.Id == task.Id);
        remaining.ShouldBe(0);
    }
}
