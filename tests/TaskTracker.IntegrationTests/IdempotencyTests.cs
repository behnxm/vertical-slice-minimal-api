using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TaskTracker.Api.Common.Idempotency;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Features.Projects;
using TaskTracker.IntegrationTests.Infrastructure;
using Xunit;

namespace TaskTracker.IntegrationTests;

public class IdempotencyTests(ContainersFixture containers) : BaseIntegrationTest(containers)
{
    [Fact]
    public async Task Idempotent_Request_Replay_Returns_Identical_Response_Without_Duplication()
    {
        var client = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        // 1. Initial Request
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects")
        {
            Content = JsonContent.Create(new CreateProject.Request("Idempotent Architecture", "Verified Valkey Replay"))
        };
        req1.Headers.Add("Idempotency-Key", idempotencyKey);

        var res1 = await client.SendAsync(req1);
        res1.StatusCode.ShouldBe(HttpStatusCode.Created);
        res1.Headers.Contains("Idempotency-Replay").ShouldBeFalse();

        var body1 = await res1.Content.ReadStrictJsonAsync<CreateProject.Response>();
        body1.ShouldNotBeNull();

        // 2. Exact Replay with identical payload and key
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects")
        {
            Content = JsonContent.Create(new CreateProject.Request("Idempotent Architecture", "Verified Valkey Replay"))
        };
        req2.Headers.Add("Idempotency-Key", idempotencyKey);

        var res2 = await client.SendAsync(req2);
        res2.StatusCode.ShouldBe(HttpStatusCode.Created);
        res2.Headers.Contains("Idempotency-Replay").ShouldBeTrue();

        var body2 = await res2.Content.ReadStrictJsonAsync<CreateProject.Response>();
        body2.ShouldNotBeNull();
        body2.Id.ShouldBe(body1.Id);
        body2.Name.ShouldBe(body1.Name);

        // 3. Database verification: exactly one project was created
        var listRes = await client.GetAsync("/api/v1/projects");
        var list = await listRes.Content.ReadStrictJsonAsync<Api.Common.Pagination.PagedResult<ListProjects.ProjectItem>>();
        list.ShouldNotBeNull();
        list.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Payload_Mismatch_Returns_422_Unprocessable_Content()
    {
        var client = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        // 1. Initial Request
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects")
        {
            Content = JsonContent.Create(new CreateProject.Request("Payload A", "Original"))
        };
        req1.Headers.Add("Idempotency-Key", idempotencyKey);
        var res1 = await client.SendAsync(req1);
        res1.StatusCode.ShouldBe(HttpStatusCode.Created);

        // 2. Request with same key but altered payload -> 422 Unprocessable Content
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects")
        {
            Content = JsonContent.Create(new CreateProject.Request("Payload B", "Tampered"))
        };
        req2.Headers.Add("Idempotency-Key", idempotencyKey);
        var res2 = await client.SendAsync(req2);
        res2.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Concurrent_InFlight_Request_Returns_409_Conflict()
    {
        var client = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        // Simulate an in-flight request by manually acquiring the key in the store
        var store = Factory.Services.GetRequiredService<IIdempotencyStore>();
        await store.TryAcquireAsync(idempotencyKey, "simulated-in-progress-hash", TimeSpan.FromMinutes(1));

        // When a concurrent request arrives with the same key, it is blocked with 409 Conflict
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects")
        {
            Content = JsonContent.Create(new CreateProject.Request("Concurrent Project"))
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);

        var res = await client.SendAsync(req);
        res.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
