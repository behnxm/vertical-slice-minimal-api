using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TaskTracker.Api.Persistence.Interceptors;

/// <summary>
/// Scoped tracker tracking the number of database commands executed during a request.
/// </summary>
public interface IDbCommandCounter
{
    int Count { get; }
    void Increment();
    void Reset();
}

public sealed class DbCommandCounter : IDbCommandCounter
{
    private int _count;

    public int Count => _count;

    public void Increment() => Interlocked.Increment(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);
}

/// <summary>
/// EF Core command interceptor recording query executions for test verification and telemetry.
/// </summary>
public sealed class DbCommandCountInterceptor(IDbCommandCounter counter) : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        counter.Increment();
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        counter.Increment();
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        counter.Increment();
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }
}
