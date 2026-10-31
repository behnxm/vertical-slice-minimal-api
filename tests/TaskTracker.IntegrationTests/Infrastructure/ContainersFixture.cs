using System.Data.Common;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

[assembly: AssemblyFixture(typeof(TaskTracker.IntegrationTests.Infrastructure.ContainersFixture))]
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace TaskTracker.IntegrationTests.Infrastructure;

public sealed class ContainersFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim ResetLock = new(1, 1);

    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder("postgres:17.11-alpine")
        .WithDatabase("tasktracker_test")
        .WithUsername("test_user")
        .WithPassword("test_password_12345")
        .Build();

    private readonly RedisContainer _valkeyContainer = new RedisBuilder("valkey/valkey:8.0.12-alpine")
        .Build();

    private Respawner? _respawner;

    public string PostgreSqlConnectionString => _postgreSqlContainer.GetConnectionString();
    public string ValkeyConnectionString => _valkeyContainer.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(
            _postgreSqlContainer.StartAsync(),
            _valkeyContainer.StartAsync());
    }

    public async Task InitializeRespawnerAsync()
    {
        if (_respawner is not null) return;

        await ResetLock.WaitAsync();
        try
        {
            if (_respawner is not null) return;

            await using var conn = new NpgsqlConnection(PostgreSqlConnectionString);
            await conn.OpenAsync();

            _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore = ["__EFMigrationsHistory"]
            });
        }
        finally
        {
            ResetLock.Release();
        }
    }

    public async Task ResetDatabaseAsync()
    {
        await ResetLock.WaitAsync();
        try
        {
            if (_respawner is not null)
            {
                await using var conn = new NpgsqlConnection(PostgreSqlConnectionString);
                await conn.OpenAsync();
                await _respawner.ResetAsync(conn);
            }
        }
        finally
        {
            ResetLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(
            _postgreSqlContainer.DisposeAsync().AsTask(),
            _valkeyContainer.DisposeAsync().AsTask());
    }
}
