using System.Data.Common;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

[assembly: AssemblyFixture(typeof(TaskTracker.IntegrationTests.Infrastructure.ContainersFixture))]

namespace TaskTracker.IntegrationTests.Infrastructure;

public sealed class ContainersFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder("postgres:17.11-alpine")
        .WithDatabase("tasktracker_test")
        .WithUsername("test_user")
        .WithPassword("test_password_12345")
        .Build();

    private readonly RedisContainer _valkeyContainer = new RedisBuilder("valkey/valkey:8.0.12-alpine")
        .Build();

    private Respawner? _respawner;
    private DbConnection? _dbConnection;

    public string PostgreSqlConnectionString => _postgreSqlContainer.GetConnectionString();
    public string ValkeyConnectionString => _valkeyContainer.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(
            _postgreSqlContainer.StartAsync(),
            _valkeyContainer.StartAsync());

        _dbConnection = new NpgsqlConnection(PostgreSqlConnectionString);
        await _dbConnection.OpenAsync();
    }

    public async Task InitializeRespawnerAsync()
    {
        if (_respawner is not null || _dbConnection is null) return;

        _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"]
        });
    }

    public async Task ResetDatabaseAsync()
    {
        if (_respawner is not null && _dbConnection is not null)
        {
            await _respawner.ResetAsync(_dbConnection);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_dbConnection is not null)
        {
            await _dbConnection.DisposeAsync();
        }

        await Task.WhenAll(
            _postgreSqlContainer.DisposeAsync().AsTask(),
            _valkeyContainer.DisposeAsync().AsTask());
    }
}
