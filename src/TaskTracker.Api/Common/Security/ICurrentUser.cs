using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace TaskTracker.Api.Common.Security;

/// <summary>
/// Abstraction representing the authenticated caller for the current request.
/// Slices and handlers depend solely on this abstraction rather than directly touching HttpContext.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? DisplayName { get; }
    bool IsAuthenticated { get; }
    IReadOnlySet<string> Scopes { get; }
    bool HasScope(string scope);
}

/// <summary>
/// Resolves authenticated user details from HttpContext ClaimsPrincipal.
/// Standardizes 'sub' claim for user id and space-delimited 'scope' / 'scp' claims for scopes.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null || user.Identity?.IsAuthenticated != true)
        {
            UserId = null;
            DisplayName = null;
            IsAuthenticated = false;
            Scopes = new HashSet<string>(StringComparer.Ordinal);
            return;
        }

        IsAuthenticated = true;
        UserId = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        DisplayName = user.FindFirstValue("name") ?? user.FindFirstValue("preferred_username") ?? UserId;

        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in user.FindAll("scope"))
        {
            foreach (var part in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                scopes.Add(part);
            }
        }
        foreach (var claim in user.FindAll("scp"))
        {
            foreach (var part in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                scopes.Add(part);
            }
        }

        Scopes = scopes;
    }

    public string? UserId { get; }
    public string? DisplayName { get; }
    public bool IsAuthenticated { get; }
    public IReadOnlySet<string> Scopes { get; }

    public bool HasScope(string scope) => Scopes.Contains(scope);
}
