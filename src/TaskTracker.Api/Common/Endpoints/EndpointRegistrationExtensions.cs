using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TaskTracker.Api.Common.Endpoints;

/// <summary>
/// Scans the assembly to register slice handlers and map IEndpoint implementations.
/// </summary>
public static class EndpointRegistrationExtensions
{
    /// <summary>
    /// Scans the assembly for all slice Handler classes and registers them as scoped services.
    /// Handlers within the Dev namespace are only registered when DevIssuer:Enabled is true.
    /// </summary>
    public static IServiceCollection AddSliceHandlers(
        this IServiceCollection services,
        Assembly assembly,
        IConfiguration configuration)
    {
        var devEnabled = configuration.GetValue<bool>("DevIssuer:Enabled");

        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name == "Handler" && t.DeclaringType != null);

        foreach (var handlerType in handlerTypes)
        {
            if (handlerType.Namespace?.Contains(".Dev", StringComparison.Ordinal) == true && !devEnabled)
            {
                continue;
            }

            services.AddScoped(handlerType);
        }

        return services;
    }

    /// <summary>
    /// Scans the assembly for all IEndpoint implementations and maps them into the /api/v1 versioned route group.
    /// Default authorization is applied to the versioned route group.
    /// Dev endpoints are registered outside /api/v1 and only when DevIssuer:Enabled is true.
    /// </summary>
    public static IEndpointRouteBuilder MapSliceEndpoints(
        this IEndpointRouteBuilder app,
        Assembly assembly,
        IConfiguration configuration)
    {
        var devEnabled = configuration.GetValue<bool>("DevIssuer:Enabled");

        var v1Group = app.MapGroup("/api/v1")
            .RequireAuthorization();

        var endpointTypes = assembly.GetTypes()
            .Where(t => typeof(IEndpoint).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract);

        foreach (var endpointType in endpointTypes)
        {
            if (endpointType.FullName?.Contains(".Dev.") == true)
            {
                if (!devEnabled)
                {
                    continue;
                }

                var devInstance = (IEndpoint)Activator.CreateInstance(endpointType)!;
                devInstance.MapEndpoint(app);
                continue;
            }

            var endpointInstance = (IEndpoint)Activator.CreateInstance(endpointType)!;
            endpointInstance.MapEndpoint(v1Group);
        }

        return app;
    }
}
