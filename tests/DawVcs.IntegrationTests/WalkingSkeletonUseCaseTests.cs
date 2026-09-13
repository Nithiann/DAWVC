using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Repositories;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Xunit;

namespace DawVcs.IntegrationTests;

public sealed class WalkingSkeletonUseCaseTests : IDisposable
{
    private readonly string _testWorkspace;
    private readonly string _fixtureFlpPath;
    private readonly Func<string, IRepositoryContext> _contextFactory;

    public WalkingSkeletonUseCaseTests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), "dawvc_app_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testWorkspace);

        _contextFactory = dir => new FileSystemRepositoryContext(dir);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DawVcs.slnx")))
        {
            dir = dir.Parent;
        }

        var root = dir?.FullName ?? throw new InvalidOperationException("Could not find solution root.");
        _fixtureFlpPath = Path.Combine(root, "fixtures", "flstudio", "Nithiann & Mr. Unit - ID", "Nithiann & Mr. Unit - ID.flp");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testWorkspace))
        {
            try
            {
                Directory.Delete(_testWorkspace, recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    public async Task DirectUseCases_FullLifecycle_Succeeds()
    {
        var repoDir = Path.Combine(_testWorkspace, "MyProject");
        Directory.CreateDirectory(repoDir);

        var originalFile = Path.Combine(repoDir, "Track.flp");
        File.Copy(_fixtureFlpPath, originalFile);

        var preHash = await Blake3ContentHasher.HashFileAsync(originalFile);

        // 1. Init
        var initUseCase = new InitRepositoryUseCase(_contextFactory);
        var initResult = await initUseCase.ExecuteAsync(new InitRequest(repoDir));
        initResult.PrimaryArtifact.Value.Should().Be("Track.flp");

        // 2. Commit
        var commitUseCase = new CommitUseCase(_contextFactory);
        var commitResult = await commitUseCase.ExecuteAsync(new CommitRequest(repoDir, "First commit"));
        commitResult.Message.Should().Be("First commit");

        // 3. Log
        var logUseCase = new LogUseCase(_contextFactory);
        var logResult = await logUseCase.ExecuteAsync(new LogRequest(repoDir));
        logResult.Entries.Should().HaveCount(1);
        logResult.Entries[0].Id.Should().Be(commitResult.CommitId);

        // 4. Checkout Restore
        var restoreDir = Path.Combine(_testWorkspace, "Restored");
        var checkoutUseCase = new CheckoutRestoreUseCase(_contextFactory);
        var checkoutResult = await checkoutUseCase.ExecuteAsync(new CheckoutRestoreRequest(repoDir, "main", restoreDir));

        checkoutResult.RestoredFiles.Should().Contain("Track.flp");
        var restoredFile = Path.Combine(restoreDir, "Track.flp");
        File.Exists(restoredFile).Should().BeTrue();

        var restoredHash = await Blake3ContentHasher.HashFileAsync(restoredFile);
        restoredHash.Should().Be(preHash);
    }
}
