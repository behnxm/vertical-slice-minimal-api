using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using TaskTracker.IntegrationTests.Infrastructure;
using Xunit;

namespace TaskTracker.IntegrationTests;

public class OpenApiDriftTests(ContainersFixture containers) : BaseIntegrationTest(containers)
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    private static readonly string SnapshotPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../docs/openapi/tasktracker.v1.json"));

    [Fact]
    public async Task OpenApi_Document_Matches_Committed_Snapshot()
    {
        var client = Factory.CreateAnonymousClient();

        var response = await client.GetAsync("/openapi/v1.json");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var actualJsonString = await response.Content.ReadAsStringAsync();
        actualJsonString.ShouldNotBeNullOrWhiteSpace();

        // Normalize JSON with indented formatting
        using var doc = JsonDocument.Parse(actualJsonString);
        var normalizedActual = JsonSerializer.Serialize(doc.RootElement, IndentedJsonOptions);

        if (!File.Exists(SnapshotPath))
        {
            var dir = Path.GetDirectoryName(SnapshotPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(SnapshotPath, normalizedActual);
        }

        var expectedJsonString = await File.ReadAllTextAsync(SnapshotPath);
        using var expectedDoc = JsonDocument.Parse(expectedJsonString);
        var normalizedExpected = JsonSerializer.Serialize(expectedDoc.RootElement, IndentedJsonOptions);

        normalizedActual.ShouldBe(
            normalizedExpected,
            "The generated OpenAPI document has drifted from docs/openapi/tasktracker.v1.json. If API changes were intentional, update the committed snapshot.");
    }
}
