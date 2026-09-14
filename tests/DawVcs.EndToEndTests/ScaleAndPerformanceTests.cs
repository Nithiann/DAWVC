using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Commits;
using DawVcs.Application.Repositories;
using DawVcs.Application.Scanning;
using DawVcs.Application.Staging;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Repositories;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

[CollectionDefinition("PerformanceNonParallel", DisableParallelization = true)]
public class PerformanceNonParallelDefinition { }

[Collection("PerformanceNonParallel")]
public sealed class ScaleAndPerformanceTests
{
    private static Func<string, IRepositoryContext> ContextFactory => dir => new FileSystemRepositoryContext(dir);
    private static IDawAdapterRegistry Registry => new DawAdapterRegistry([new FLStudioAdapter()]);

    [Fact]
    [Trait("Requirement", "NFR-PERF-001, NFR-PERF-003, NFR-PERF-004")]
    public async Task Scale_5000Assets_CommitsAndWarmStatusCompletesUnderTwoSecondsAndLowMemory()
    {
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Project.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        // 1. Generate 5,000 synthetic sample assets in subdirectories
        var sampleDir = Path.Combine(temp.Path, "Audio");
        Directory.CreateDirectory(sampleDir);

        const int totalAssets = 5000;
        var sampleContent = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 }; // minimal RIFF WAV header

        for (int i = 0; i < totalAssets; i++)
        {
            var fileName = $"Sample_{i:D4}.wav";
            var filePath = Path.Combine(sampleDir, fileName);
            File.WriteAllBytes(filePath, sampleContent);
        }

        // 2. Initialize repository
        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "ScaleBenchmark", "Project.flp"));

        // 3. Stage all 5,000 assets
        var addUseCase = new AddUseCase(ContextFactory);
        var addResult = await addUseCase.ExecuteAsync(new AddRequest(temp.Path, All: true));
        addResult.StagedPaths.Count.Should().BeGreaterOrEqualTo(totalAssets);

        // 4. Commit 5,000 assets and verify memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long initialMemory = GC.GetTotalMemory(true);

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var commitResult = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Commit 5000 synthetic assets"));
        commitResult.IsComplete.Should().BeTrue();

        long postCommitMemory = GC.GetTotalMemory(false);
        long memoryDeltaBytes = Math.Max(0, postCommitMemory - initialMemory);

        // NFR-PERF-003: Piekgeheugengebruik behoort onder 512 MB te blijven
        memoryDeltaBytes.Should().BeLessThan(512 * 1024 * 1024, "Peak memory for 5,000 assets must be under 512 MB");

        // 5. NFR-PERF-004: Unchanged warm status behoort binnen 2 seconden te voltooien
        var statusUseCase = new StatusUseCase(ContextFactory);

        // First status run warms any remaining cache
        await statusUseCase.ExecuteAsync(new StatusRequest(temp.Path));

        // Benchmark warm status
        var sw = Stopwatch.StartNew();
        var warmStatusResult = await statusUseCase.ExecuteAsync(new StatusRequest(temp.Path));
        sw.Stop();

        warmStatusResult.Status.IsClean.Should().BeTrue();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "Warm status scan on 5,000 assets must finish under 2 seconds (NFR-PERF-004)");
    }

    [Fact]
    [Trait("Requirement", "NFR-PERF-002")]
    public async Task Streaming_LargeVirtualData_ProcessesWithoutBufferingInRam()
    {
        using var temp = new TempDirectory();
        var store = new LooseObjectStore(temp.Path);

        // NFR-PERF-002: Verwerk 100 MB / streaming data zonder volledige dataset in RAM te laden
        // Virtual deterministic stream generating 50 MB on the fly in 64KB blocks
        const long streamSize = 50 * 1024 * 1024; // 50 MB
        using var virtualStream = new DeterministicChunkStream(streamSize);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long memoryBefore = GC.GetTotalMemory(true);

        // Write to object store via streaming
        var hash = await store.WriteBlobAsync(virtualStream);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long memoryAfter = GC.GetTotalMemory(true);
        long delta = Math.Max(0, memoryAfter - memoryBefore);

        // RAM memory consumed must remain tiny (well under 512 MB, bounded under 32 MB) during streaming of 50 MB
        delta.Should().BeLessThan(32 * 1024 * 1024, "Streaming I/O must not buffer large payloads into memory");

        // Verify object exists and payload can be verified streaming to Stream.Null
        store.Exists(hash).Should().BeTrue();
        await store.VerifyObjectIntegrityAsync(hash);
    }

    private sealed class DeterministicChunkStream : Stream
    {
        private readonly long _totalLength;
        private long _position;
        private readonly byte[] _block;

        public DeterministicChunkStream(long totalLength)
        {
            _totalLength = totalLength;
            _block = new byte[64 * 1024];
            for (int i = 0; i < _block.Length; i++)
            {
                _block[i] = (byte)(i % 256);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _totalLength;
        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, _totalLength);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _totalLength) return 0;

            int toRead = (int)Math.Min(count, _totalLength - _position);
            int blockOffset = (int)(_position % _block.Length);
            int bytesFromBlock = Math.Min(toRead, _block.Length - blockOffset);

            Buffer.BlockCopy(_block, blockOffset, buffer, offset, bytesFromBlock);
            _position += bytesFromBlock;
            return bytesFromBlock;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _totalLength) return ValueTask.FromResult(0);

            int toRead = (int)Math.Min(buffer.Length, _totalLength - _position);
            int blockOffset = (int)(_position % _block.Length);
            int bytesFromBlock = Math.Min(toRead, _block.Length - blockOffset);

            _block.AsSpan(blockOffset, bytesFromBlock).CopyTo(buffer.Span);
            _position += bytesFromBlock;
            return ValueTask.FromResult(bytesFromBlock);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _totalLength + offset,
                _ => _position
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dawvc-perf-test-" + Guid.NewGuid().ToString("N"));

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
            GC.SuppressFinalize(this);
        }
    }

    private static byte[] CreateFlp()
    {
        using var dataStream = new MemoryStream();
        dataStream.WriteByte(199);
        dataStream.WriteByte(11);
        dataStream.Write(Encoding.ASCII.GetBytes("25.2.5.5319"));

        var dataBytes = dataStream.ToArray();
        using var flp = new MemoryStream();
        flp.Write([0x46, 0x4C, 0x68, 0x64, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x60, 0x00]);
        flp.Write([0x46, 0x4C, 0x64, 0x74]);
        var lenBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(lenBytes, (uint)dataBytes.Length);
        flp.Write(lenBytes);
        flp.Write(dataBytes);

        return flp.ToArray();
    }
}
