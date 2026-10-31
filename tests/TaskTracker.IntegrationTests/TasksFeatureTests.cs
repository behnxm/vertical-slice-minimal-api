using System.Net;
using System.Net.Http.Json;
using Shouldly;
using TaskTracker.Api.Common.Pagination;
using TaskTracker.Api.Common.Security;
using TaskTracker.Api.Domain.Enums;
using TaskTracker.Api.Features.Members;
using TaskTracker.Api.Features.Projects;
using TaskTracker.Api.Features.Tasks;
using TaskTracker.IntegrationTests.Infrastructure;
using Xunit;

namespace TaskTracker.IntegrationTests;

public class TasksFeatureTests(ContainersFixture containers) : BaseIntegrationTest(containers)
{
    [Fact]
    public async Task CreateTask_Validates_Assignee_Membership_And_Enforces_Roles()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var strangerClient = Factory.CreateAuthorizedClient("stranger-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Task Spec Project"));
        var project = await projectRes.Content.ReadStrictJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        // 1. Stranger cannot create tasks -> 404
        var strangerCreate = await strangerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Stranger Task", null, TaskPriority.High, null, null, ["test"]));
        strangerCreate.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 2. Assigning a non-member user -> 400 Bad Request
        var nonMemberAssignee = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Assigned Task", null, TaskPriority.High, null, "non-member", ["test"]));
        nonMemberAssignee.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 3. Add member and assign them -> 201 Created
        await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/members",
            new AddMember.Body("colleague-1", ProjectRole.Editor));

        var validCreate = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Assigned Task", "Description", TaskPriority.High, null, "colleague-1", ["backend", "dotnet"]));
        validCreate.StatusCode.ShouldBe(HttpStatusCode.Created);

        var task = await validCreate.Content.ReadStrictJsonAsync<CreateTask.Response>();
        task.ShouldNotBeNull();
        task.Title.ShouldBe("Assigned Task");
        task.AssigneeUserId.ShouldBe("colleague-1");
        task.Status.ShouldBe(TaskItemStatus.Todo);
        task.Labels.ShouldBe(["backend", "dotnet"]);
        task.RowVersion.ShouldBe(1u);
    }

    [Fact]
    public async Task GetTask_Honours_ETag_And_IfNoneMatch_And_Secures_CrossProject_Access()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var strangerClient = Factory.CreateAuthorizedClient("stranger-1", AuthConstants.Scopes.Read);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Task ETag Project"));
        var project = await projectRes.Content.ReadStrictJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        var taskRes = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("ETag Task", null, TaskPriority.Medium, null, null, null));
        var task = await taskRes.Content.ReadStrictJsonAsync<CreateTask.Response>();
        task.ShouldNotBeNull();

        // 1. Initial GET -> 200 OK with ETag
        var getRes = await ownerClient.GetAsync($"/api/v1/tasks/{task.Id}");
        getRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        getRes.Headers.ETag.ShouldNotBeNull();
        var etag = getRes.Headers.ETag.ToString();
        etag.ShouldBe("W/\"1\"");

        // 2. Conditional GET with If-None-Match -> 304 Not Modified
        var conditionalReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tasks/{task.Id}");
        conditionalReq.Headers.IfNoneMatch.ParseAdd(etag);
        var conditionalRes = await ownerClient.SendAsync(conditionalReq);
        conditionalRes.StatusCode.ShouldBe(HttpStatusCode.NotModified);

        // 3. Stranger GET -> 404 NotFound
        var strangerRes = await strangerClient.GetAsync($"/api/v1/tasks/{task.Id}");
        strangerRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateTask_Optimistic_Concurrency_And_IfMatch()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Task Concurrency Project"));
        var project = await projectRes.Content.ReadStrictJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        var taskRes = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Initial Task", null, TaskPriority.Medium, null, null, null));
        var task = await taskRes.Content.ReadStrictJsonAsync<CreateTask.Response>();
        task.ShouldNotBeNull();

        // 1. Missing If-Match -> 428 Precondition Required
        var noHeaderReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/tasks/{task.Id}")
        {
            Content = HttpClientTestExtensions.CreateStrictJsonContent(new UpdateTask.Body("Updated Task", null, TaskPriority.Urgent, null, null))
        };
        var noHeaderRes = await ownerClient.SendAsync(noHeaderReq);
        noHeaderRes.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        // 2. Mismatched If-Match -> 412 Precondition Failed
        var mismatchReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/tasks/{task.Id}")
        {
            Content = HttpClientTestExtensions.CreateStrictJsonContent(new UpdateTask.Body("Updated Task", null, TaskPriority.Urgent, null, null))
        };
        mismatchReq.Headers.IfMatch.ParseAdd("W/\"99\"");
        var mismatchRes = await ownerClient.SendAsync(mismatchReq);
        mismatchRes.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // 3. Valid If-Match -> 200 OK with incremented RowVersion
        var validReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/tasks/{task.Id}")
        {
            Content = HttpClientTestExtensions.CreateStrictJsonContent(new UpdateTask.Body("Updated Task", "Updated Description", TaskPriority.Urgent, null, ["v2"]))
        };
        validReq.Headers.IfMatch.ParseAdd("W/\"1\"");
        var validRes = await ownerClient.SendAsync(validReq);
        validRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await validRes.Content.ReadStrictJsonAsync<UpdateTask.Response>();
        updated.ShouldNotBeNull();
        updated.Title.ShouldBe("Updated Task");
        updated.Priority.ShouldBe(TaskPriority.Urgent);
        updated.RowVersion.ShouldBe(2u);
    }

    [Fact]
    public async Task Task_Status_Machine_Transitions()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Status Machine Project"));
        var project = await projectRes.Content.ReadStrictJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        var taskRes = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Status Test", null, TaskPriority.Medium, null, null, null));
        var task = await taskRes.Content.ReadStrictJsonAsync<CreateTask.Response>();
        task.ShouldNotBeNull();

        // 1. Todo -> InProgress: OK
        var toInProgress = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/tasks/{task.Id}/status",
            new ChangeTaskStatus.Body(TaskItemStatus.InProgress));
        toInProgress.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 2. InProgress -> Done: OK, completedAt set
        var toDone = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/tasks/{task.Id}/status",
            new ChangeTaskStatus.Body(TaskItemStatus.Done));
        toDone.StatusCode.ShouldBe(HttpStatusCode.OK);
        var doneResponse = await toDone.Content.ReadStrictJsonAsync<ChangeTaskStatus.Response>();
        doneResponse.ShouldNotBeNull();
        doneResponse.CompletedAt.ShouldNotBeNull();

        // 3. Done -> Todo: Invalid transition -> 409 Conflict
        var invalidTransition = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/tasks/{task.Id}/status",
            new ChangeTaskStatus.Body(TaskItemStatus.Todo));
        invalidTransition.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // 4. Done -> InProgress (reopen): OK, completedAt cleared
        var reopen = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/tasks/{task.Id}/status",
            new ChangeTaskStatus.Body(TaskItemStatus.InProgress));
        reopen.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reopenResponse = await reopen.Content.ReadStrictJsonAsync<ChangeTaskStatus.Response>();
        reopenResponse.ShouldNotBeNull();
        reopenResponse.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ListTasks_FullTextSearch_And_LabelFilter_And_KeysetPagination()
    {
        var client = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);

        var projectRes = await client.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Search and Query Project"));
        var project = await projectRes.Content.ReadStrictJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        // Seed 3 tasks
        await client.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("PostgreSQL fulltext indexing", "Speed up text searches", TaskPriority.High, null, null, ["database", "postgres"]));

        await client.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Implement HybridCache with Valkey", "Distributed caching layer", TaskPriority.Medium, null, null, ["caching", "redis"]));

        await client.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Database migrations deployment", "Run schema changes automatically", TaskPriority.Low, null, null, ["database", "ops"]));

        // 1. PostgreSQL Full-Text Search
        var searchRes = await client.GetAsync($"/api/v1/projects/{project.Id}/tasks?search=indexing");
        searchRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var searchResult = await searchRes.Content.ReadStrictJsonAsync<PagedResult<ListTasks.TaskSummary>>();
        searchResult.ShouldNotBeNull();
        searchResult.Items.Count.ShouldBe(1);
        searchResult.Items[0].Title.ShouldBe("PostgreSQL fulltext indexing");

        // 2. Native Array Label Filter (@>)
        var labelRes = await client.GetAsync($"/api/v1/projects/{project.Id}/tasks?label=database");
        labelRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var labelResult = await labelRes.Content.ReadStrictJsonAsync<PagedResult<ListTasks.TaskSummary>>();
        labelResult.ShouldNotBeNull();
        labelResult.Items.Count.ShouldBe(2);

        // 3. Keyset Pagination (limit=2)
        var page1Res = await client.GetAsync($"/api/v1/projects/{project.Id}/tasks?limit=2");
        page1Res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page1 = await page1Res.Content.ReadStrictJsonAsync<PagedResult<ListTasks.TaskSummary>>();
        page1.ShouldNotBeNull();
        page1.Items.Count.ShouldBe(2);
        page1.HasMore.ShouldBeTrue();
        page1.NextCursor.ShouldNotBeNull();

        var page2Res = await client.GetAsync($"/api/v1/projects/{project.Id}/tasks?limit=2&cursor={Uri.EscapeDataString(page1.NextCursor)}");
        page2Res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page2 = await page2Res.Content.ReadStrictJsonAsync<PagedResult<ListTasks.TaskSummary>>();
        page2.ShouldNotBeNull();
        page2.Items.Count.ShouldBe(1);
        page2.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task SoftDelete_And_Admin_Restore()
    {
        var ownerClient = Factory.CreateAuthorizedClient("owner-1", AuthConstants.Scopes.Read, AuthConstants.Scopes.Write);
        var adminClient = Factory.CreateAuthorizedClient("admin-1", AuthConstants.Scopes.Admin, AuthConstants.Scopes.Read);

        var projectRes = await ownerClient.PostAsJsonAsync("/api/v1/projects", new CreateProject.Request("Soft Delete Project"));
        var project = await projectRes.Content.ReadStrictJsonAsync<CreateProject.Response>();
        project.ShouldNotBeNull();

        var taskRes = await ownerClient.PostAsStrictJsonAsync(
            $"/api/v1/projects/{project.Id}/tasks",
            new CreateTask.Body("Task to Delete", null, TaskPriority.Low, null, null, null));
        var task = await taskRes.Content.ReadStrictJsonAsync<CreateTask.Response>();
        task.ShouldNotBeNull();

        // 1. Soft-delete task -> 204 NoContent
        var deleteRes = await ownerClient.DeleteAsync($"/api/v1/tasks/{task.Id}");
        deleteRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // 2. Fetching deleted task returns 404 (named query filter active)
        var getDeleted = await ownerClient.GetAsync($"/api/v1/tasks/{task.Id}");
        getDeleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 3. Admin restores task -> 200 OK
        var restoreRes = await adminClient.PostAsync($"/api/v1/admin/tasks/{task.Id}/restore", null);
        restoreRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 4. Task is active again for owner
        var getRestored = await ownerClient.GetAsync($"/api/v1/tasks/{task.Id}");
        getRestored.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
