using System.Net;
using System.Net.Http.Json;
using Shouldly;
using TaskTracker.Api.Common.Pagination;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Features.Projects;
using TaskTracker.IntegrationTests.Infrastructure;
using Xunit;

namespace TaskTracker.IntegrationTests;

public class ProjectsFeatureTests(ContainersFixture containers) : BaseIntegrationTest(containers)
{
    [Fact]
    public async Task CreateProject_Requires_Authentication_And_Write_Scope()
    {
        // 1. Unauthenticated -> 401
        var anonymousClient = Factory.CreateAnonymousClient();
        var anonResponse = await anonymousClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Test"));
        anonResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // 2. Authenticated without write scope (read only) -> 403
        var readOnlyClient = Factory.CreateAuthorizedClient("user-1", AuthConstants.Scopes.Read);
        var readOnlyResponse = await readOnlyClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Test"));
        readOnlyResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // 3. Authenticated with write scope -> 201 Created
        var writeClient = Factory.CreateAuthorizedClient("user-1", AuthConstants.Scopes.Write);
        var successResponse = await writeClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Backend Platform", "VSA Showcase"));
        successResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await successResponse.Content.ReadFromJsonAsync<CreateProject.Response>();
        created.ShouldNotBeNull();
        created.Name.ShouldBe("Backend Platform");
        created.CreatedBy.ShouldBe("user-1");
        created.RowVersion.ShouldBe(1u);
    }

    [Fact]
    public async Task GetProject_Returns_Weak_ETag_And_Honours_IfNoneMatch()
    {
        var client = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        // Create project
        var createRes = await client.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("ETag Project"));
        var created = await createRes.Content.ReadFromJsonAsync<CreateProject.Response>();
        created.ShouldNotBeNull();

        // 1. Initial GET -> 200 OK with ETag
        var getRes = await client.GetAsync($"/api/v1/projects/{created.Id}");
        getRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        getRes.Headers.ETag.ShouldNotBeNull();
        var etag = getRes.Headers.ETag.ToString();
        etag.ShouldBe("W/\"1\"");

        // 2. Conditional GET with matching If-None-Match -> 304 Not Modified
        var ifNoneMatchRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/projects/{created.Id}");
        ifNoneMatchRequest.Headers.IfNoneMatch.ParseAdd(etag);
        var notModifiedRes = await client.SendAsync(ifNoneMatchRequest);
        notModifiedRes.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task UpdateProject_Enforces_IfMatch_Optimistic_Concurrency()
    {
        var client = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        var createRes = await client.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Original Title"));
        var created = await createRes.Content.ReadFromJsonAsync<CreateProject.Response>();
        created.ShouldNotBeNull();

        // 1. Update without If-Match header -> 428 Precondition Required
        var noHeaderRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/projects/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateProject.Body("Updated Title"))
        };
        var noHeaderRes = await client.SendAsync(noHeaderRequest);
        noHeaderRes.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        // 2. Update with mismatched If-Match header -> 412 Precondition Failed
        var mismatchRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/projects/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateProject.Body("Updated Title"))
        };
        mismatchRequest.Headers.IfMatch.ParseAdd("W/\"99\"");
        var mismatchRes = await client.SendAsync(mismatchRequest);
        mismatchRes.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // 3. Update with valid matching If-Match header -> 200 OK
        var validRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/projects/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateProject.Body("Updated Title", "Updated Description"))
        };
        validRequest.Headers.IfMatch.ParseAdd("W/\"1\"");
        var validRes = await client.SendAsync(validRequest);
        validRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await validRes.Content.ReadFromJsonAsync<UpdateProject.Response>();
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("Updated Title");
        updated.RowVersion.ShouldBe(2u);
    }

    [Fact]
    public async Task ListProjects_Keyset_Pagination_And_User_Isolation()
    {
        var user1 = Factory.CreateAuthorizedClient("alice", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var user2 = Factory.CreateAuthorizedClient("bob", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        // Alice creates 3 projects
        for (var i = 1; i <= 3; i++)
        {
            await user1.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request($"Alice Project {i}"));
        }

        // Bob creates 1 project
        await user2.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Bob Project 1"));

        // Alice lists her projects with limit 2 (first page)
        var alicePage1Res = await user1.GetAsync("/api/v1/projects?limit=2");
        alicePage1Res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var alicePage1 = await alicePage1Res.Content.ReadFromJsonAsync<PagedResult<ListProjects.ProjectItem>>();
        alicePage1.ShouldNotBeNull();
        alicePage1.Items.Count.ShouldBe(2);
        alicePage1.HasMore.ShouldBeTrue();
        alicePage1.NextCursor.ShouldNotBeNull();

        // Alice fetches page 2 using cursor
        var alicePage2Res = await user1.GetAsync($"/api/v1/projects?cursor={alicePage1.NextCursor}&limit=2");
        alicePage2Res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var alicePage2 = await alicePage2Res.Content.ReadFromJsonAsync<PagedResult<ListProjects.ProjectItem>>();
        alicePage2.ShouldNotBeNull();
        alicePage2.Items.Count.ShouldBe(1);
        alicePage2.HasMore.ShouldBeFalse();

        // BOLA check: Bob only sees his own 1 project, never Alice's
        var bobListRes = await user2.GetAsync("/api/v1/projects");
        var bobList = await bobListRes.Content.ReadFromJsonAsync<PagedResult<ListProjects.ProjectItem>>();
        bobList.ShouldNotBeNull();
        bobList.Items.Count.ShouldBe(1);
        bobList.Items[0].Name.ShouldBe("Bob Project 1");
    }

    [Fact]
    public async Task NonMember_Receives_404_NotFound_Protecting_Private_Projects()
    {
        var alice = Factory.CreateAuthorizedClient("alice", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var eve = Factory.CreateAuthorizedClient("eve", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        // Alice creates a private project
        var createRes = await alice.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Alice Secret Project"));
        var project = await createRes.Content.ReadFromJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        // Eve (non-member) attempts to read Alice's project -> MUST return 404 Not Found, NOT 403 Forbidden
        var eveGetRes = await eve.GetAsync($"/api/v1/projects/{project.Id}");
        eveGetRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Eve attempts to update Alice's project -> MUST return 404 Not Found
        var evePutRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/projects/{project.Id}")
        {
            Content = JsonContent.Create(new UpdateProject.Body("Hacked Name"))
        };
        evePutRequest.Headers.IfMatch.ParseAdd("W/\"1\"");
        var evePutRes = await eve.SendAsync(evePutRequest);
        evePutRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
