using System.Net;
using System.Net.Http.Json;
using Shouldly;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Features.Projects;
using TaskTracker.IntegrationTests.Infrastructure;
using Xunit;

namespace TaskTracker.IntegrationTests;

public class MultiInstanceCacheTests(ContainersFixture containers) : BaseIntegrationTest(containers)
{
    [Fact]
    public async Task Two_API_Instances_Share_Valkey_L2_And_Observe_Cache_Invalidation()
    {
        // Create a secondary independent WebApplicationFactory instance connected to the same containers
        await using var secondFactory = new CustomWebApplicationFactory(Containers);

        var client1 = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var client2 = secondFactory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        // 1. Instance 1 creates a project
        var createRes = await client1.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Distributed Cache Project"));
        createRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createRes.Content.ReadStrictJsonAsync<CreateProject.Response>();
        created.ShouldNotBeNull();

        // 2. Instance 2 reads the project (populating Valkey L2 cache)
        var get1 = await client2.GetAsync($"/api/v1/projects/{created.Id}");
        get1.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched1 = await get1.Content.ReadStrictJsonAsync<GetProject.Response>();
        fetched1.ShouldNotBeNull();
        fetched1.Name.ShouldBe("Distributed Cache Project");
        fetched1.RowVersion.ShouldBe(1u);

        // 3. Instance 1 updates the project (triggering cache invalidation)
        var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/projects/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateProject.Body("Renamed by Instance 1", "Cache Test"))
        };
        updateReq.Headers.IfMatch.ParseAdd("W/\"1\"");
        var updateRes = await client1.SendAsync(updateReq);
        updateRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 4. Instance 2 reads the project again: must observe the updated state
        var get2 = await client2.GetAsync($"/api/v1/projects/{created.Id}");
        get2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched2 = await get2.Content.ReadStrictJsonAsync<GetProject.Response>();
        fetched2.ShouldNotBeNull();
        fetched2.Name.ShouldBe("Renamed by Instance 1");
        fetched2.RowVersion.ShouldBe(2u);
    }
}
