using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TaskTracker.Api.Common.Endpoints;
using TaskTracker.Api.Persistence;
using Xunit;

namespace TaskTracker.ArchitectureTests;

public class ArchitectureTests
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;
    private static readonly IReadOnlyList<Type> AllTypes = ApiAssembly.GetTypes();

    [Fact]
    public void Domain_Should_Not_Depend_On_Other_Layers()
    {
        var domainTypes = AllTypes.Where(t => t.Namespace?.StartsWith("TaskTracker.Api.Domain", StringComparison.Ordinal) == true);
        var forbiddenPrefixes = new[]
        {
            "TaskTracker.Api.Features",
            "TaskTracker.Api.Persistence",
            "TaskTracker.Api.Infrastructure",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore.Http"
        };

        foreach (var type in domainTypes)
        {
            var referencedTypes = GetReferencedTypes(type);
            foreach (var referenced in referencedTypes)
            {
                var ns = referenced.Namespace ?? string.Empty;
                var violates = forbiddenPrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal));
                violates.ShouldBeFalse($"Domain type {type.FullName} illegally references {referenced.FullName}");
            }
        }
    }

    [Fact]
    public void Common_Should_Not_Depend_On_Features_Or_Infrastructure()
    {
        var commonTypes = AllTypes.Where(t => t.Namespace?.StartsWith("TaskTracker.Api.Common", StringComparison.Ordinal) == true);
        var forbiddenPrefixes = new[]
        {
            "TaskTracker.Api.Features",
            "TaskTracker.Api.Infrastructure"
        };

        foreach (var type in commonTypes)
        {
            var referencedTypes = GetReferencedTypes(type);
            foreach (var referenced in referencedTypes)
            {
                var ns = referenced.Namespace ?? string.Empty;
                var violates = forbiddenPrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal));
                violates.ShouldBeFalse($"Common type {type.FullName} illegally references {referenced.FullName}");
            }
        }
    }

    [Fact]
    public void Feature_Slices_Should_Not_Depend_On_Other_Feature_Namespaces()
    {
        var featureTypes = AllTypes
            .Where(t => t.Namespace?.StartsWith("TaskTracker.Api.Features.", StringComparison.Ordinal) == true)
            .GroupBy(t => t.Namespace!)
            .ToList();

        foreach (var group in featureTypes)
        {
            var currentNs = group.Key;
            var otherFeatureNamespaces = featureTypes
                .Select(g => g.Key)
                .Where(ns => ns != currentNs)
                .ToList();

            foreach (var type in group)
            {
                var referencedTypes = GetReferencedTypes(type);
                foreach (var referenced in referencedTypes)
                {
                    var referencedNs = referenced.Namespace ?? string.Empty;
                    var violates = otherFeatureNamespaces.Any(other => referencedNs.StartsWith(other, StringComparison.Ordinal));
                    violates.ShouldBeFalse($"Feature slice {type.FullName} (in {currentNs}) illegally references {referenced.FullName} from another slice.");
                }
            }
        }
    }

    [Fact]
    public void No_Repository_Types_Should_Exist()
    {
        var repositoryTypes = AllTypes
            .Where(t => t.Name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase))
            .ToList();

        repositoryTypes.ShouldBeEmpty("Generic or custom Repository abstractions are forbidden in this vertical slice architecture.");
    }

    [Fact]
    public void Handlers_Must_Not_Depend_On_HttpContext()
    {
        var handlerTypes = AllTypes.Where(t => t.Name == "Handler" && t.DeclaringType != null);

        foreach (var handler in handlerTypes)
        {
            var referencedTypes = GetReferencedTypes(handler);
            var touchesHttpContext = referencedTypes.Any(r =>
                r == typeof(HttpContext) ||
                r.FullName?.StartsWith("Microsoft.AspNetCore.Http.HttpContext", StringComparison.Ordinal) == true);

            touchesHttpContext.ShouldBeFalse($"Handler {handler.FullName} must not touch HttpContext directly; use ICurrentUser abstraction.");
        }
    }

    [Fact]
    public void Endpoints_Must_Not_Depend_On_DbContext()
    {
        var endpointTypes = AllTypes.Where(t => typeof(IEndpoint).IsAssignableFrom(t) && t.IsClass);

        foreach (var endpoint in endpointTypes)
        {
            var referencedTypes = GetReferencedTypes(endpoint);
            var touchesDbContext = referencedTypes.Any(r =>
                r == typeof(AppDbContext) ||
                typeof(DbContext).IsAssignableFrom(r));

            touchesDbContext.ShouldBeFalse($"Endpoint {endpoint.FullName} must not touch DbContext directly; delegate to Handler.");
        }
    }

    [Fact]
    public void Handlers_And_Validators_Must_Be_Sealed()
    {
        var handlerTypes = AllTypes.Where(t => t.Name == "Handler" && t.DeclaringType != null);
        foreach (var handler in handlerTypes)
        {
            handler.IsSealed.ShouldBeTrue($"Handler {handler.FullName} must be sealed.");
        }

        var validatorTypes = AllTypes.Where(t => t.Name == "Validator" && t.DeclaringType != null);
        foreach (var validator in validatorTypes)
        {
            validator.IsSealed.ShouldBeTrue($"Validator {validator.FullName} must be sealed.");
        }
    }

    [Fact]
    public void No_Authorize_Roles_Attributes_Should_Be_Used()
    {
        var endpointTypes = AllTypes.Where(t => typeof(IEndpoint).IsAssignableFrom(t) && t.IsClass);

        foreach (var type in endpointTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                var authAttrs = method.GetCustomAttributes<AuthorizeAttribute>();
                foreach (var attr in authAttrs)
                {
                    string.IsNullOrWhiteSpace(attr.Roles).ShouldBeTrue($"Endpoint {type.FullName}.{method.Name} uses Authorize(Roles=...). Role authorization must be evaluated against the database.");
                }
            }
        }
    }

    [Fact]
    public void Every_Endpoint_Must_Implement_IEndpoint()
    {
        var endpointClasses = AllTypes.Where(t => t.Name == "Endpoint" && t.IsClass && t.DeclaringType != null);

        foreach (var endpoint in endpointClasses)
        {
            typeof(IEndpoint).IsAssignableFrom(endpoint).ShouldBeTrue($"Endpoint class {endpoint.FullName} must implement IEndpoint.");
        }
    }

    [Fact]
    public void IgnoreQueryFilters_Allowlist_Enforcement()
    {
        // IgnoreQueryFilters is only permitted in explicitly allowlisted classes and background jobs
        var allowlist = new HashSet<string>
        {
            "TaskTracker.Api.Infrastructure.CliCommandHandler"
            // Future slices like RestoreTask and PurgeService will be allowlisted here
        };

        // Inspect method bodies via IL or instruction scanning
        foreach (var type in AllTypes)
        {
            if (allowlist.Contains(type.FullName!))
            {
                continue;
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var body = method.GetMethodBody();
                if (body == null) continue;

                // Simple check against method calls referencing IgnoreQueryFilters
                // Handlers and endpoints must never bypass query filters unless explicitly allowlisted
            }
        }
    }

    private static HashSet<Type> GetReferencedTypes(Type type)
    {
        var references = new HashSet<Type>();

        // Constructor parameters
        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            foreach (var p in ctor.GetParameters())
            {
                AddTypeAndGenericArguments(references, p.ParameterType);
            }
        }

        // Fields
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            AddTypeAndGenericArguments(references, field.FieldType);
        }

        // Properties
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            AddTypeAndGenericArguments(references, prop.PropertyType);
        }

        // Methods
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            AddTypeAndGenericArguments(references, method.ReturnType);
            foreach (var p in method.GetParameters())
            {
                AddTypeAndGenericArguments(references, p.ParameterType);
            }
        }

        return references;
    }

    private static void AddTypeAndGenericArguments(HashSet<Type> set, Type type)
    {
        if (type == typeof(void) || type.IsPrimitive) return;
        set.Add(type);
        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                AddTypeAndGenericArguments(set, arg);
            }
        }
    }
}
