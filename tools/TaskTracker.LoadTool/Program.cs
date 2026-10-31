using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskTracker.LoadTool;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine(" TaskTracker Load Testing Tool - Vertical Slice Architecture Benchmark");
        Console.WriteLine("================================================================================");

        var baseUrl = GetArg(args, "--url", "http://localhost:8080");
        var concurrency = int.Parse(GetArg(args, "--concurrency", "5"), System.Globalization.CultureInfo.InvariantCulture);
        var durationSeconds = int.Parse(GetArg(args, "--duration", "10"), System.Globalization.CultureInfo.InvariantCulture);
        var scenario = GetArg(args, "--scenario", "mixed").ToLowerInvariant();
        var paceMs = int.Parse(GetArg(args, "--pace-ms", "0"), System.Globalization.CultureInfo.InvariantCulture);

        Console.WriteLine($"Target URL:    {baseUrl}");
        Console.WriteLine($"Concurrency:   {concurrency} workers");
        Console.WriteLine($"Duration:      {durationSeconds} seconds");
        Console.WriteLine($"Scenario:      {scenario}");
        Console.WriteLine($"Pace Delay:    {paceMs} ms");
        Console.WriteLine("--------------------------------------------------------------------------------");

        using var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = concurrency * 4
        };

        using var adminClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Obtain bearer token for benchmark admin
        Console.WriteLine("[Setup] Requesting admin token from development issuer...");
        var adminToken = await GetTokenAsync(adminClient, "perf-admin");
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Create benchmark test project
        Console.WriteLine("[Setup] Initializing benchmark workspace project...");
        var projRes = await adminClient.PostAsJsonAsync("/api/v1/projects", new
        {
            name = $"Perf Project {Guid.NewGuid():N}",
            description = "High-throughput vertical slice benchmarking workspace"
        }, JsonOptions);

        if (!projRes.IsSuccessStatusCode)
        {
            Console.WriteLine($"[Error] Failed to create project: {projRes.StatusCode}");
            return 1;
        }

        var projDoc = await projRes.Content.ReadFromJsonAsync<JsonDocument>();
        var projectId = projDoc!.RootElement.GetProperty("id").GetString()!;

        // Add each worker user to project to avoid 403 Forbidden and distribute rate limit partitions
        Console.WriteLine($"[Setup] Provisioning {concurrency} worker identities and project memberships...");
        var workerClients = new List<(HttpClient Client, string UserId)>();
        for (var i = 0; i < concurrency; i++)
        {
            var workerUserId = $"perf-worker-{i + 1}";
            var addMemberRes = await adminClient.PostAsJsonAsync($"/api/v1/projects/{projectId}/members", new
            {
                userId = workerUserId,
                role = "Editor"
            }, JsonOptions);
            addMemberRes.EnsureSuccessStatusCode();

            var workerClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            var workerToken = await GetTokenAsync(workerClient, workerUserId);
            workerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", workerToken);
            workerClients.Add((workerClient, workerUserId));
        }

        // Seed initial tasks for read/search scenarios
        Console.WriteLine("[Setup] Seeding baseline tasks for query scenarios...");
        var taskIds = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            var seedRes = await adminClient.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new
            {
                title = $"Benchmark task {i + 1} for high-performance retrieval",
                description = "Task seeded for keyset pagination and full-text search benchmarking",
                priority = i % 2 == 0 ? "High" : "Medium",
                labels = new[] { "benchmark", i % 2 == 0 ? "urgent" : "routine" }
            }, JsonOptions);

            if (seedRes.IsSuccessStatusCode)
            {
                var taskDoc = await seedRes.Content.ReadFromJsonAsync<JsonDocument>();
                taskIds.Add(taskDoc!.RootElement.GetProperty("id").GetString()!);
            }
        }

        Console.WriteLine($"[Setup] Ready. Executing scenario '{scenario}' for {durationSeconds}s with {concurrency} workers...");
        Console.WriteLine("--------------------------------------------------------------------------------");

        var latencies = new ConcurrentBag<double>();
        var statusCodes = new ConcurrentDictionary<int, int>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
        var sw = Stopwatch.StartNew();

        var workers = Enumerable.Range(0, concurrency).Select(async workerIndex =>
        {
            var (client, _) = workerClients[workerIndex];
            var random = new Random(workerIndex * 7919);

            while (!cts.Token.IsCancellationRequested)
            {
                var reqSw = Stopwatch.StartNew();
                try
                {
                    HttpResponseMessage resp;
                    switch (scenario)
                    {
                        case "read":
                            if (random.Next(2) == 0 && taskIds.Count > 0)
                            {
                                var tid = taskIds[random.Next(taskIds.Count)];
                                resp = await client.GetAsync($"/api/v1/tasks/{tid}", cts.Token);
                            }
                            else
                            {
                                resp = await client.GetAsync($"/api/v1/projects/{projectId}", cts.Token);
                            }
                            break;

                        case "search":
                            resp = await client.GetAsync($"/api/v1/projects/{projectId}/tasks?search=benchmark", cts.Token);
                            break;

                        case "write":
                            resp = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new
                            {
                                title = $"Worker {workerIndex} Task {Guid.NewGuid():N}",
                                priority = "High",
                                labels = new[] { "benchmark" }
                            }, JsonOptions, cts.Token);
                            break;

                        case "mixed":
                        default:
                            var dice = random.Next(10);
                            if (dice < 6)
                            {
                                // 60% Read (Task or Project details)
                                var tid = taskIds[random.Next(taskIds.Count)];
                                resp = await client.GetAsync($"/api/v1/tasks/{tid}", cts.Token);
                            }
                            else if (dice < 8)
                            {
                                // 20% Search / Filter
                                resp = await client.GetAsync($"/api/v1/projects/{projectId}/tasks?label=benchmark&limit=10", cts.Token);
                            }
                            else
                            {
                                // 20% Write
                                resp = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new
                                {
                                    title = $"Load task {Guid.NewGuid():N}",
                                    priority = "Medium",
                                    labels = new[] { "load-test" }
                                }, JsonOptions, cts.Token);
                            }
                            break;
                    }

                    reqSw.Stop();
                    var ms = reqSw.Elapsed.TotalMilliseconds;
                    latencies.Add(ms);
                    statusCodes.AddOrUpdate((int)resp.StatusCode, 1, (_, count) => count + 1);

                    if (paceMs > 0)
                    {
                        await Task.Delay(paceMs, cts.Token);
                    }
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    statusCodes.AddOrUpdate(500, 1, (_, count) => count + 1);
                }
            }
        });

        await Task.WhenAll(workers);
        sw.Stop();

        var totalRequests = latencies.Count;
        var elapsedSeconds = sw.Elapsed.TotalSeconds;
        var rps = totalRequests / elapsedSeconds;

        var sorted = latencies.OrderBy(x => x).ToList();
        var p50 = sorted.Count > 0 ? sorted[(int)(sorted.Count * 0.50)] : 0;
        var p95 = sorted.Count > 0 ? sorted[(int)(sorted.Count * 0.95)] : 0;
        var p99 = sorted.Count > 0 ? sorted[(int)(sorted.Count * 0.99)] : 0;
        var max = sorted.Count > 0 ? sorted[^1] : 0;
        var min = sorted.Count > 0 ? sorted[0] : 0;

        Console.WriteLine("================================================================================");
        Console.WriteLine(" BENCHMARK RESULTS SUMMARY");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"Total Completed Requests:  {totalRequests:N0}");
        Console.WriteLine($"Total Duration:            {elapsedSeconds:F2}s");
        Console.WriteLine($"Throughput:                {rps:F1} req/sec");
        Console.WriteLine();
        Console.WriteLine("Latency Percentiles:");
        Console.WriteLine($"  Min:                     {min:F2} ms");
        Console.WriteLine($"  p50 (Median):            {p50:F2} ms");
        Console.WriteLine($"  p95:                     {p95:F2} ms");
        Console.WriteLine($"  p99:                     {p99:F2} ms");
        Console.WriteLine($"  Max:                     {max:F2} ms");
        Console.WriteLine();
        Console.WriteLine("HTTP Status Breakdown:");
        foreach (var kvp in statusCodes.OrderBy(k => k.Key))
        {
            var pct = totalRequests > 0 ? (kvp.Value * 100.0 / totalRequests) : 0;
            Console.WriteLine($"  HTTP {kvp.Key}: {kvp.Value:N0} ({pct:F1}%)");
        }
        Console.WriteLine("================================================================================");

        return 0;
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string userId)
    {
        var tokenRes = await client.PostAsJsonAsync("/dev/token", new
        {
            userId,
            scopes = new[] { "tasks.read", "tasks.write", "tasks.admin" }
        }, JsonOptions);

        tokenRes.EnsureSuccessStatusCode();
        var tokenDoc = await tokenRes.Content.ReadFromJsonAsync<JsonDocument>();
        return tokenDoc!.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static string GetArg(string[] args, string flag, string defaultValue)
    {
        var idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : defaultValue;
    }
}
