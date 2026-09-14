using System.Buffers.Binary;
using System.Text;

using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Branches;
using DawVcs.Application.Commits;
using DawVcs.Application.Exceptions;
using DawVcs.Application.Repositories;
using DawVcs.Application.Staging;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class BranchSwitchAcTests
{
    private static Func<string, IRepositoryContext> ContextFactory => dir => new FileSystemRepositoryContext(dir);
    private static IDawAdapterRegistry Registry => new DawAdapterRegistry([new FLStudioAdapter()]);

    [Fact]
    [Trait("Requirement", "FR-BRA-001..003")]
    public async Task Branch_CreateListDelete_WorksCorrectly()
    {
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "BranchTest", "Track.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        var initialCommit = await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Initial commit"));

        var branchUseCase = new BranchUseCase(ContextFactory);

        // List initial branches: only main
        var initialBranches = await branchUseCase.ListAsync(temp.Path);
        initialBranches.Should().ContainSingle(b => b.Name.Value == "main" && b.IsCurrent);

        // Create new branch 'feature-drums'
        var created = await branchUseCase.CreateAsync(new BranchCreateRequest(temp.Path, "feature-drums"));
        created.Name.Value.Should().Be("feature-drums");
        created.CommitId.Should().Be(initialCommit.CommitId);

        // List again: should contain main (active) and feature-drums
        var branches = await branchUseCase.ListAsync(temp.Path);
        branches.Should().HaveCount(2);
        branches.First(b => b.Name.Value == "main").IsCurrent.Should().BeTrue();
        branches.First(b => b.Name.Value == "feature-drums").IsCurrent.Should().BeFalse();

        // Delete branch
        var deleted = await branchUseCase.DeleteAsync(new BranchDeleteRequest(temp.Path, "feature-drums"));
        deleted.Should().BeTrue();

        var afterDelete = await branchUseCase.ListAsync(temp.Path);
        afterDelete.Should().ContainSingle(b => b.Name.Value == "main");
    }

    [Fact]
    [Trait("Requirement", "FR-BRA-004..007")]
    public async Task Switch_WithDirtyWorkspace_BlocksWithoutForce_AndSucceedsWithForceRecovery()
    {
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Track.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        var initUseCase = new InitRepositoryUseCase(ContextFactory);
        await initUseCase.ExecuteAsync(new InitRequest(temp.Path, "SwitchTest", "Track.flp"));

        var commitUseCase = new CommitUseCase(ContextFactory, Registry);
        await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Initial commit"));

        // Maak een feature branch aan
        var branchUseCase = new BranchUseCase(ContextFactory);
        await branchUseCase.CreateAsync(new BranchCreateRequest(temp.Path, "experiment"));

        // Maak een tweede commit op main
        var sampleBytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "HiHat.wav"), sampleBytes);
        var addUseCase = new AddUseCase(ContextFactory);
        await addUseCase.ExecuteAsync(new AddRequest(temp.Path, ["HiHat.wav"]));
        await commitUseCase.ExecuteAsync(new CommitRequest(temp.Path, "Add HiHat"));

        // Maak de active workspace dirty
        var dirtyFlp = new byte[] { 0xFE, 0xED, 0xBA, 0xBE };
        await File.WriteAllBytesAsync(flpPath, dirtyFlp);

        var switchUseCase = new SwitchUseCase(ContextFactory);

        // WHEN we proberen te switchen zonder --force
        var act = () => switchUseCase.ExecuteAsync(new SwitchRequest(temp.Path, "experiment", Force: false));

        // THEN dirty workspace beveiliging blokkeert switch met exit code 7 / DirtyWorkspaceException
        await act.Should().ThrowAsync<DirtyWorkspaceException>();

        // Context HEAD is nog steeds main
        var context = ContextFactory(temp.Path);
        context.GetCurrentBranch().Value.Should().Be("main");

        // WHEN we switchen met --force
        var forceResult = await switchUseCase.ExecuteAsync(new SwitchRequest(temp.Path, "experiment", Force: true));

        // THEN switch slaagt en er is een recoverykopie gemaakt
        forceResult.Branch.Value.Should().Be("experiment");
        forceResult.RecoveryDirectory.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(forceResult.RecoveryDirectory).Should().BeTrue();

        context.GetCurrentBranch().Value.Should().Be("experiment");
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dawvc-branch-test-" + Guid.NewGuid().ToString("N"));

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
