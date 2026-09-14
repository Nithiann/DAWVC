namespace DawVcs.Application.Common;

/// <summary>
/// Beheert begrensde concurrency voor parallelle I/O- en hashingoperaties (NFR-PERF-006).
/// Houdt het systeem responsief door het aantal gelijktijdige zware taken te maximeren op basis van processorcapaciteit.
/// </summary>
public static class ConcurrencyLimiter
{
    public static int DefaultMaxConcurrency => Math.Clamp(Environment.ProcessorCount, 1, 8);

    public static async Task ForEachAsync<TSource>(
        IEnumerable<TSource> source,
        Func<TSource, CancellationToken, Task> body,
        int? maxConcurrency = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);

        var degree = maxConcurrency ?? DefaultMaxConcurrency;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = degree,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(source, parallelOptions, async (item, ct) =>
        {
            await body(item, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
