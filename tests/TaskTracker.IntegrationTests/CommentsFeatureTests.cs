using System.Net;
using System.Net.Http.Json;
using Shouldly;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Features.Comments;
using TaskTracker.Api.Features.Members;
using TaskTracker.Api.Features.Projects;
using TaskTracker.Api.Features.Tasks;
using TaskTracker.IntegrationTests.Infrastructure;
using Xunit;

namespace TaskTracker.IntegrationTests;

public class CommentsFeatureTests(ContainersFixture containers) : BaseIntegrationTest(containers)
{
    [Fact]
    public async Task Create_And_List_Comments_Permitted_For_Project_Members_And_Hidden_From_Strangers()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var viewerClient = Factory.CreateAuthorizedClient("viewer-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var strangerClient = Factory.CreateAuthorizedClient("stranger-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Comment Spec Project"));
        var project = await projectRes.Content.ReadStrictJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members",
            new AddMember.Body("viewer-1", ProjectRole.Viewer));

        var taskRes = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Comment Task", null, TaskPriority.Medium, null, null, null));
        var task = await taskRes.Content.ReadStrictJsonAsync<CreateTask.Response>();
        task.ShouldNotBeNull();

        // 1. Stranger attempting to comment -> 404
        var strangerComment = await strangerClient.PostAsStrictJsonAsync(
            $"/api/v1/tasks/{task.Id}/comments",
            new CreateComment.BodyPayload("Stranger comment"));
        strangerComment.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 2. Viewer member comments -> 201 Created
        var viewerCommentRes = await viewerClient.PostAsStrictJsonAsync(
            $"/api/v1/tasks/{task.Id}/comments",
            new CreateComment.BodyPayload("Viewer feedback on task"));
        viewerCommentRes.StatusCode.ShouldBe(HttpStatusCode.Created);

        var viewerComment = await viewerCommentRes.Content.ReadStrictJsonAsync<CreateComment.Response>();
        viewerComment.ShouldNotBeNull();
        viewerComment.AuthorUserId.ShouldBe("viewer-1");
        viewerComment.Body.ShouldBe("Viewer feedback on task");

        // 3. Owner comments -> 201 Created
        var ownerCommentRes = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/tasks/{task.Id}/comments",
            new CreateComment.BodyPayload("Owner response"));
        ownerCommentRes.StatusCode.ShouldBe(HttpStatusCode.Created);

        // 4. List comments returns chronological list
        var listRes = await viewerClient.GetAsync($"/api/v1/tasks/{task.Id}/comments");
        listRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listResponse = await listRes.Content.ReadStrictJsonAsync<ListComments.Response>();
        listResponse.ShouldNotBeNull();
        listResponse.Comments.Count.ShouldBe(2);
        listResponse.Comments[0].AuthorUserId.ShouldBe("viewer-1");
        listResponse.Comments[1].AuthorUserId.ShouldBe("owner-1");
    }

    [Fact]
    public async Task DeleteComment_Permitted_For_Author_And_Owner_Forbidden_For_Other_Members()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var authorClient = Factory.CreateAuthorizedClient("colleague-author", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var bystanderClient = Factory.CreateAuthorizedClient("bystander", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Comment Security Project"));
        var project = await projectRes.Content.ReadStrictJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members",
            new AddMember.Body("colleague-author", ProjectRole.Editor));

        await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members",
            new AddMember.Body("bystander", ProjectRole.Editor));

        var taskRes = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Security Task", null, TaskPriority.Medium, null, null, null));
        var task = await taskRes.Content.ReadStrictJsonAsync<CreateTask.Response>();
        task.ShouldNotBeNull();

        // Author creates comment
        var commentRes = await authorClient.PostAsStrictJsonAsync(
            $"/api/v1/tasks/{task.Id}/comments",
            new CreateComment.BodyPayload("Sensitive comment"));
        var comment = await commentRes.Content.ReadStrictJsonAsync<CreateComment.Response>();
        comment.ShouldNotBeNull();

        // 1. Bystander tries to delete comment -> 403 Forbidden
        var bystanderDelete = await bystanderClient.DeleteAsync($"/api/v1/tasks/{task.Id}/comments/{comment.Id}");
        bystanderDelete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // 2. Project Owner deletes comment -> 204 NoContent
        var ownerDelete = await ownerClient.DeleteAsync($"/api/v1/tasks/{task.Id}/comments/{comment.Id}");
        ownerDelete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify comment is deleted
        var listAfterDelete = await ownerClient.GetAsync($"/api/v1/tasks/{task.Id}/comments");
        var listData = await listAfterDelete.Content.ReadStrictJsonAsync<ListComments.Response>();
        listData.ShouldNotBeNull();
        listData.Comments.Count.ShouldBe(0);
    }
}
