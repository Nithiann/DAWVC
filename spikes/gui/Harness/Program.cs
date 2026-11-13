using System.Diagnostics;
using System.Text.Json;

using DawVcs.Adapters.FLStudio.Tests.Fixtures;
using DawVcs.Application.Common;
using DawVcs.Application.Repositories;
using DawVcs.Application.Scanning;
using DawVcs.Bootstrap;
using DawVcs.GuiSpike;

using Microsoft.Extensions.DependencyInjection;

if (args is ["--fixture" or "--large-fixture", var path])
{
    var root = Path.GetFullPath(path);
    if (Directory.Exists(root) || File.Exists(root)) throw new ArgumentException("Fixture destination must not exist.");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(root, "Samples"));
    var names = Enumerable.Range(0, 100).Select(i => $"Samples/Asset{i:D5}.wav").ToArray();
    foreach (var name in names)
    {
        using var wave = new BinaryWriter(File.Create(Path.Combine(root, name)));
        wave.Write("RIFF"u8); wave.Write(4096 - 8); wave.Write("WAVEfmt "u8); wave.Write(16);
        wave.Write((short)1); wave.Write((short)1); wave.Write(44100); wave.Write(88200);
        wave.Write((short)2); wave.Write((short)16); wave.Write("data"u8); wave.Write(4096 - 44);
        wave.Write(new byte[4096 - 44]);
    }
    await File.WriteAllBytesAsync(Path.Combine(root, "Project.flp"), FlpFixtureGenerator.CreateValidFlp(samplePaths: names));
    if (args[0] == "--large-fixture")
    {
        await using var large = File.Create(Path.Combine(root, "LargeAsset.bin"));
        var buffer = new byte[1024 * 1024];
        for (int i = 0; i < 256; i++) await large.WriteAsync(buffer);
    }
    using var provider = new ServiceCollection().AddDawVcs().BuildServiceProvider();
    await provider.GetRequiredService<InitRepositoryUseCase>().ExecuteAsync(new InitRequest(root, "GUI spike", "Project.flp"));
    await File.WriteAllTextAsync(Path.Combine(root, ".wp3-fixture"), "DAWVC-WP3-SYNTHETIC-v1");
    Console.WriteLine("Disposable synthetic fixture created.");
}
else if (args is ["--model-benchmark"])
{
    await using var model = new SpikeSession();
    var times = new List<double>();
    for (int i = 0; i < 20; i++)
        foreach (var filter in new[] { "Asset099", "", "no-match" })
        {
            model.Filter = filter;
            times.Add(model.LastFilterMilliseconds);
        }
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "model-filter-only", framework = "shared", samplesMs = times, p95Ms = times.Order().ElementAt((int)Math.Ceiling(times.Count * 0.95) - 1), maxMs = times.Max() }));
}
else if (args is ["--cancel-status", var directory])
{
    var root = Path.GetFullPath(directory);
    var large = Path.Combine(root, "LargeAsset.bin");
    if (!File.Exists(Path.Combine(root, ".wp3-fixture")) ||
        await File.ReadAllTextAsync(Path.Combine(root, ".wp3-fixture")) != "DAWVC-WP3-SYNTHETIC-v1" ||
        !File.Exists(large) || new FileInfo(large).Length != 256L * 1024 * 1024)
        throw new ArgumentException("Requires a disposable --large-fixture directory.");
    for (int i = 0; i < 10; i++)
    {
        // Invalidate only the known synthetic large asset's hash cache, without changing bytes.
        File.SetLastWriteTimeUtc(large, DateTime.UtcNow.AddSeconds(i + 1));
        using var provider = new ServiceCollection().AddDawVcs().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        await using var progress = new CancelDuringHashing(cancellation);
        var outcome = "finished-before-cancellation";
        try
        {
            await Task.Run(() => provider.GetRequiredService<StatusUseCase>()
                .ExecuteAsync(new StatusRequest(root), cancellation.Token, progress));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { outcome = "cancelled"; }
        var end = Stopwatch.GetTimestamp();
        var cancelledAt = progress.CancelledAt;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "backend-cancel-only",
            iteration = i + 1,
            outcome,
            millisecondsAfterRequest = cancelledAt == 0 ? (double?)null : Stopwatch.GetElapsedTime(cancelledAt, end).TotalMilliseconds
        }));
        if (outcome != "cancelled") return 3;
    }
}
else if (args is ["--backend", var root])
{
    await using var session = new SpikeSession();
    if (!session.Open(root)) throw new ArgumentException("Not an initialized repository.");
    foreach (var operation in new[] { "Status", "Scan", "Doctor" })
    {
        var timer = Stopwatch.StartNew();
        await session.RunAsync(operation);
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "backend-only", operation, elapsedMs = timer.Elapsed.TotalMilliseconds, summary = session.Summary, failureCategory = session.FailureCategory?.ToString() }));
        if (session.FailureCategory.HasValue) return 2;
    }
}
else
{
    Console.WriteLine("--fixture <new-directory> | --large-fixture <new-directory> | --model-benchmark | --backend <disposable-repository> | --cancel-status <large-fixture>");
    return 1;
}
return 0;


internal sealed class CancelDuringHashing : IProgress<OperationProgress>, IAsyncDisposable
{
    private readonly Timer _timer;
    private int _scheduled;
    private long _cancelledAt;
    public long CancelledAt => Interlocked.Read(ref _cancelledAt);

    public CancelDuringHashing(CancellationTokenSource source)
    {
        _timer = new Timer(_ =>
        {
            Interlocked.Exchange(ref _cancelledAt, Stopwatch.GetTimestamp());
            source.Cancel();
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Report(OperationProgress value)
    {
        if (value.Stage == OperationStage.HashingFiles && value.Total > 0 && Interlocked.Exchange(ref _scheduled, 1) == 0)
            _timer.Change(TimeSpan.FromMilliseconds(10), Timeout.InfiniteTimeSpan);
    }

    public ValueTask DisposeAsync() => _timer.DisposeAsync();
}
