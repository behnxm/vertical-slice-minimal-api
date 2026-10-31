using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskTracker.Api.Common.Caching;

namespace TaskTracker.Api.Infrastructure;

public static class CachingExtensions
{
    public static IServiceCollection AddTaskTrackerCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var valkeyConnectionString = configuration.GetConnectionString("Valkey");

        if (!string.IsNullOrWhiteSpace(valkeyConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = valkeyConnectionString;
                options.InstanceName = "tasktracker:";
            });
        }

#pragma warning disable EXTEXP0018 // HybridCache is in preview / experimental package in some builds
        services.AddHybridCache(options =>
        {
            options.MaximumPayloadBytes = 1024 * 1024; // 1 MB max payload
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = CacheConstants.DefaultL2Expiration,
                LocalCacheExpiration = CacheConstants.DefaultL1Expiration
            };
        });
#pragma warning restore EXTEXP0018

        services.AddSingleton<ICacheHealthTracker, CacheHealthTracker>();
        services.AddScoped<CacheService>();
        services.AddScoped<ICacheService>(sp => sp.GetRequiredService<CacheService>());
        services.AddScoped<ICacheInvalidator>(sp => sp.GetRequiredService<CacheService>());

        return services;
    }
}
