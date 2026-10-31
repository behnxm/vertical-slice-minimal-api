namespace TaskTracker.Api.Common.Caching;

public static class CacheConstants
{
    public static string ProjectKey(Guid projectId) => $"v1:project:{projectId:N}";
    public static string ProjectSummaryKey(Guid projectId) => $"v1:project:{projectId:N}:summary";
    public static string UserProjectsFirstPageKey(string userId) => $"v1:user:{userId}:projects:first";
    public static string TaskKey(Guid taskId) => $"v1:task:{taskId:N}";

    public static string ProjectTag(Guid projectId) => $"project:{projectId:N}";
    public static string UserTag(string userId) => $"user:{userId}";
    public static string TaskTag(Guid taskId) => $"task:{taskId:N}";

    public static readonly TimeSpan DefaultL2Expiration = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DefaultL1Expiration = TimeSpan.FromSeconds(30);
}
