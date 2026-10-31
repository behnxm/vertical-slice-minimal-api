using System.Net;
using System.Net.Http.Json;
using Shouldly;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Features.Members;
using TaskTracker.Api.Features.Projects;
using TaskTracker.IntegrationTests.Infrastructure;
using Xunit;

namespace TaskTracker.IntegrationTests;

public class MembersFeatureTests(ContainersFixture containers) : BaseIntegrationTest(containers)
{
    [Fact]
    public async Task AddMember_Requires_Owner_Role_And_Prohibits_Duplicates()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var otherClient = Factory.CreateAuthorizedClient("other-user", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        // 1. Create project with owner-1
        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Member Test Project"));
        var project = await projectRes.Content.ReadFromJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        // 2. Non-owner trying to add member -> 404 (stranger) or 403 (insufficient role)
        var unauthorizedAdd = await otherClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members",
            new AddMember.Body("new-member", ProjectRole.Viewer));
        unauthorizedAdd.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 3. Owner adds new member -> 201 Created
        var addRes = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members",
            new AddMember.Body("colleague-1", ProjectRole.Editor));
        addRes.StatusCode.ShouldBe(HttpStatusCode.Created);

        var member = await addRes.Content.ReadStrictJsonAsync<AddMember.Response>();
        member.ShouldNotBeNull();
        member.UserId.ShouldBe("colleague-1");
        member.Role.ShouldBe(ProjectRole.Editor);

        // 4. Adding existing member again -> 409 Conflict
        var duplicateAdd = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members",
            new AddMember.Body("colleague-1", ProjectRole.Viewer));
        duplicateAdd.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ListMembers_Permitted_For_Members_And_Hidden_From_Strangers()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var memberClient = Factory.CreateAuthorizedClient("colleague-1", AuthConstants.Scopes.Read);
        var strangerClient = Factory.CreateAuthorizedClient("stranger", AuthConstants.Scopes.Read);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("List Members Project"));
        var project = await projectRes.Content.ReadFromJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members",
            new AddMember.Body("colleague-1", ProjectRole.Viewer));

        // Stranger -> 404 NotFound
        var strangerRes = await strangerClient.GetAsync($"/api/v1/projects/{project.Id}/members");
        strangerRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Member -> 200 OK with list of members
        var memberRes = await memberClient.GetAsync($"/api/v1/projects/{project.Id}/members");
        memberRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var memberListResponse = await memberRes.Content.ReadStrictJsonAsync<ListMembers.Response>();
        memberListResponse.ShouldNotBeNull();
        memberListResponse.Members.Count.ShouldBe(2);
        memberListResponse.Members.ShouldContain(m => m.UserId == "owner-1" && m.Role == ProjectRole.Owner);
        memberListResponse.Members.ShouldContain(m => m.UserId == "colleague-1" && m.Role == ProjectRole.Viewer);
    }

    [Fact]
    public async Task UpdateMemberRole_And_LastOwnerDemotionProtection()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Role Demotion Project"));
        var project = await projectRes.Content.ReadFromJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members",
            new AddMember.Body("colleague-1", ProjectRole.Editor));

        // Attempting to demote the sole owner -> 409 Conflict
        var demoteSoleOwner = await ownerClient.PutAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members/owner-1",
            new UpdateMemberRole.Body(ProjectRole.Editor));
        demoteSoleOwner.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Promote colleague-1 to Owner -> 200 OK
        var promoteRes = await ownerClient.PutAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members/colleague-1",
            new UpdateMemberRole.Body(ProjectRole.Owner));
        promoteRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Now that two owners exist, owner-1 can be demoted to Editor -> 200 OK
        var demoteRes = await ownerClient.PutAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members/owner-1",
            new UpdateMemberRole.Body(ProjectRole.Editor));
        demoteRes.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemoveMember_Enforces_LastOwnerProtection_And_Allows_SelfRemoval()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var colleagueClient = Factory.CreateAuthorizedClient("colleague-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Remove Member Project"));
        var project = await projectRes.Content.ReadFromJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members",
            new AddMember.Body("colleague-1", ProjectRole.Editor));

        // Sole owner cannot leave the project -> 409 Conflict
        var removeSoleOwner = await ownerClient.DeleteAsync($"/api/v1/projects/{project.Id}/members/owner-1");
        removeSoleOwner.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Colleague can remove themselves (self-removal) -> 204 NoContent
        var selfRemoval = await colleagueClient.DeleteAsync($"/api/v1/projects/{project.Id}/members/colleague-1");
        selfRemoval.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Colleague is no longer a member -> 404 on project access
        var accessAfterRemoval = await colleagueClient.GetAsync($"/api/v1/projects/{project.Id}");
        accessAfterRemoval.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
