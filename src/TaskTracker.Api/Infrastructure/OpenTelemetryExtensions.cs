using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TaskTracker.Api.Infrastructure;

public static class OpenTelemetryExtensions
{
    public const string ActivitySourceName = "TaskTracker.Api";
    public const string MeterName = "TaskTracker.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Histogram<double> SliceDurationHistogram =
        Meter.CreateHistogram<double>("tasktracker_slice_duration_seconds", "s", "Execution duration of vertical slice handlers");

    public static IServiceCollection AddTaskTrackerTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: "TaskTracker.Api",
                    serviceVersion: "1.0.0",
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment", environment.EnvironmentName)
                ]))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ActivitySourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = httpContext =>
                            !httpContext.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation()
                    .AddNpgsql();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(MeterName)
                    .AddMeter("TaskTracker.Cache")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    });
                }
            });

        return services;
    }

    public static void RecordSliceDuration(string sliceName, double durationSeconds, string outcome)
    {
        SliceDurationHistogram.Record(
            durationSeconds,
            new KeyValuePair<string, object?>("slice", sliceName),
            new KeyValuePair<string, object?>("outcome", outcome));
    }
}

/// <summary>
/// Endpoint filter creating an OpenTelemetry Activity for each slice invocation and recording duration metrics.
/// </summary>
public sealed class SliceTelemetryFilter(string sliceName) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        using var activity = OpenTelemetryExtensions.ActivitySource.StartActivity($"Slice {sliceName}");
        activity?.SetTag("slice.name", sliceName);

        var stopwatch = Stopwatch.StartNew();
        var outcome = "success";

        try
        {
            var result = await next(context);
            if (context.HttpContext.Response.StatusCode >= 400)
            {
                outcome = "failure";
            }
            return result;
        }
        catch (Exception ex)
        {
            outcome = "error";
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            activity?.SetTag("slice.outcome", outcome);
            OpenTelemetryExtensions.RecordSliceDuration(sliceName, stopwatch.Elapsed.TotalSeconds, outcome);
        }
    }
}
