using TaskTracker.IntegrationTests.Infrastructure;
using Xunit;

namespace TaskTracker.IntegrationTests;

public abstract class BaseIntegrationTest : IAsyncLifetime
{
    protected CustomWebApplicationFactory Factory { get; }
    protected ContainersFixture Containers { get; }

    protected BaseIntegrationTest(ContainersFixture containers)
    {
        Containers = containers;
        Factory = new CustomWebApplicationFactory(containers);
    }

    public virtual async ValueTask InitializeAsync()
    {
        await Factory.InitializeDatabaseAsync();
        await Containers.ResetDatabaseAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
