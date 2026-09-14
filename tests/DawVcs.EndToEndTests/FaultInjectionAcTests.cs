using System.Buffers.Binary;
using System.Text;

using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Commits;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Repositories;
using DawVcs.Application.Staging;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class FaultInjectionAcTests
{
    private static IDawAdapterRegistry Registry => new DawAdapterRegistry([new FLStudioAdapter()]);

    [Fact]
    [Trait("Requirement", "NFR-INT-005")]
    public async Task FaultInjection_BeforeAtomicPublish_PreservesPreviousReachableState()
    {
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Song.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        // 1. Initial commit with normal context
        Func<string, IRepositoryContext> normalFactory = dir => new FileSystemRepositoryContext(dir);
        var initUseCase = new InitRepositoryUseCase(normalFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "FaultInjectionProject", "Song.flp"));

        var normalCommitUseCase = new CommitUseCase(normalFactory, Registry);
        var initialCommit = await normalCommitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Initial commit"));

        // 2. Stage a new sample file
        var sampleBytes = new byte[] { 1, 2, 3, 4, 5 };
        var samplePath = Path.Combine(temp.Path, "Bass.wav");
        await File.WriteAllBytesAsync(samplePath, sampleBytes);

        var addUseCase = new AddUseCase(normalFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["Bass.wav"]));

        // 3. Configure context factory with simulated fault injection right before atomic publish
        bool faultTriggered = false;
        Func<string, IRepositoryContext> faultFactory = dir => new FileSystemRepositoryContext(dir, onBeforeAtomicPublish: _ =>
        {
            faultTriggered = true;
            throw new IOException("Simulated I/O failure or power loss right before atomic rename.");
        });

        var faultyCommitUseCase = new CommitUseCase(faultFactory, Registry);

        // 4. WHEN commit executes during fault injection
        var act = () => faultyCommitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Faulty commit"));

        // THEN commit throws IOException
        await act.Should().ThrowAsync<IOException>();
        faultTriggered.Should().BeTrue();

        // 5. NFR-INT-005: Vorige bereikbare state mag NIET beschadigd zijn
        var verifyContext = normalFactory(temp.Path);
        var currentBranch = verifyContext.GetCurrentBranch();
        var headCommit = verifyContext.GetBranchCommit(currentBranch);

        headCommit.Should().Be(initialCommit.CommitId, "HEAD must strictly remain at the previous valid commit");

        // 6. Fsck verifies zero repository corruption
        var fsckUseCase = new FsckUseCase(normalFactory, Registry);
        var fsckReport = await fsckUseCase.ExecuteAsync(new FsckRequest(temp.Path, CheckArtifacts: false));

        fsckReport.IsHealthy.Should().BeTrue();
        fsckReport.RepositoryIntegrity.IsIntact.Should().BeTrue();
        fsckReport.RepositoryIntegrity.CorruptObjectsCount.Should().Be(0);
        fsckReport.RepositoryIntegrity.MissingObjectsCount.Should().Be(0);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dawvc-fault-test-" + Guid.NewGuid().ToString("N"));

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
