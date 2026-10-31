using Microsoft.AspNetCore.Routing;

namespace TaskTracker.Api.Common.Endpoints;

/// <summary>
/// Contract implemented by every vertical slice endpoint class.
/// </summary>
public interface IEndpoint
{
    /// <summary>
    /// Maps the slice endpoint onto the route group.
    /// </summary>
    void MapEndpoint(IEndpointRouteBuilder app);
}
