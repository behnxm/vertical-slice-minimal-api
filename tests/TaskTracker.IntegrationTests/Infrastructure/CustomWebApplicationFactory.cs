using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using TaskTracker.Api.Persistence;
using TaskTracker.Api.Persistence.Interceptors;

namespace TaskTracker.IntegrationTests.Infrastructure;

public class CustomWebApplicationFactory(ContainersFixture fixture) : WebApplicationFactory<Program>
{
    public TestJwtTokenIssuer TokenIssuer { get; } = new();
    public FakeTimeProvider TimeProvider { get; } = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Database", fixture.PostgreSqlConnectionString);
        builder.UseSetting("ConnectionStrings:Valkey", fixture.ValkeyConnectionString);
        builder.UseSetting("DevIssuer:Enabled", "false");
        builder.UseSetting("Authentication:Audience", TestJwtTokenIssuer.Audience);
        builder.UseSetting("Database:MigrateOnStartup", "false");

        builder.ConfigureTestServices(services =>
        {
            // Replace TimeProvider with FakeTimeProvider
            services.AddSingleton<TimeProvider>(TimeProvider);

            // Reconfigure JwtBearer to trust TestJwtTokenIssuer
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestJwtTokenIssuer.Issuer,
                    ValidateAudience = true,
                    ValidAudience = TestJwtTokenIssuer.Audience,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    IssuerSigningKey = TokenIssuer.SecurityKey,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
                };
            });
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await fixture.InitializeRespawnerAsync();
    }

    public HttpClient CreateAuthorizedClient(string userId, params string[] scopes)
    {
        var client = CreateClient();
        var token = TokenIssuer.CreateToken(userId, userId, scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateAnonymousClient() => CreateClient();

    public int GetDbCommandCount()
    {
        using var scope = Services.CreateScope();
        var counter = scope.ServiceProvider.GetRequiredService<IDbCommandCounter>();
        return counter.Count;
    }
}
