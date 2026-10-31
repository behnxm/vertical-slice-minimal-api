using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskTracker.IntegrationTests.Infrastructure;

public static class HttpClientTestExtensions
{
    public static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static Task<HttpResponseMessage> PostAsStrictJsonAsync<T>(this HttpClient client, string requestUri, T value, CancellationToken ct = default)
        => client.PostAsJsonAsync(requestUri, value, StrictJsonOptions, ct);

    public static Task<HttpResponseMessage> PutAsStrictJsonAsync<T>(this HttpClient client, string requestUri, T value, CancellationToken ct = default)
        => client.PutAsJsonAsync(requestUri, value, StrictJsonOptions, ct);

    public static JsonContent CreateStrictJsonContent<T>(T value)
        => JsonContent.Create(value, options: StrictJsonOptions);

    public static Task<T?> ReadStrictJsonAsync<T>(this HttpContent content, CancellationToken ct = default)
        => HttpContentJsonExtensions.ReadFromJsonAsync<T>(content, StrictJsonOptions, ct);
}
