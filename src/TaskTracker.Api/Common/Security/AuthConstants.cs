namespace TaskTracker.Api.Common.Security;

/// <summary>
/// Centralized OAuth 2.0 / JWT scopes and authorization policy identifiers.
/// </summary>
public static class AuthConstants
{
    public static class Scopes
    {
        public const string Read = "tasks.read";
        public const string Write = "tasks.write";
        public const string Admin = "tasks.admin";
    }

    public static class Policies
    {
        public const string Read = "tasks.read";
        public const string Write = "tasks.write";
        public const string Admin = "tasks.admin";
    }
}
