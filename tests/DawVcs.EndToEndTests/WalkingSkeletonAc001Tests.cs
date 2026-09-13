using DawVcs.Domain.Hashing;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class WalkingSkeletonAc001Tests : IDisposable
{
    private readonly string _testWorkspace;
    private readonly string _fixtureFlpPath;
    public WalkingSkeletonAc001Tests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), "dawvc_ac001_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testWorkspace);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DawVcs.slnx")))
        {
            dir = dir.Parent;
        }

        var root = dir?.FullName ?? throw new InvalidOperationException("Could not find solution root.");
        _fixtureFlpPath = Path.Combine(root, "fixtures", "flstudio", "Nithiann & Mr. Unit - ID", "Nithiann & Mr. Unit - ID.flp");

        if (!File.Exists(_fixtureFlpPath))
        {
            throw new FileNotFoundException($"Fixture not found at expected path: {_fixtureFlpPath}");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testWorkspace))
        {
            try
            {
                Directory.Delete(_testWorkspace, recursive: true);
            }
            catch
            {
                // Best effort cleanup on Windows
            }
        }
    }

    [Fact]
    [Trait("AcceptanceCriterion", "AC-001")]
    [Trait("Requirement", "FR-REP-001")]
    [Trait("Requirement", "FR-COM-001")]
    [Trait("Requirement", "FR-CHK-001")]
    public async Task WalkingSkeleton_FullLifecycle_SucceedsWithByteExactRestorationAndNonMutatingSource()
    {
        // --- 1. Setup workspace with fixture .flp ---
        var repoDir = Path.Combine(_testWorkspace, "MyProject");
        Directory.CreateDirectory(repoDir);

        var originalProjectFile = Path.Combine(repoDir, "Track.flp");
        File.Copy(_fixtureFlpPath, originalProjectFile);

        var preHash = await Blake3ContentHasher.HashFileAsync(originalProjectFile);
        var originalBytes = await File.ReadAllBytesAsync(originalProjectFile);

        // --- 2. dawvc init ---
        var initExitCode = await DawVcs.Cli.Program.Main(["init", "--dir", repoDir]);
        initExitCode.Should().Be(0, "dawvc init should exit with code 0");

        File.Exists(Path.Combine(repoDir, "dawvc.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(repoDir, ".dawvc", "HEAD")).Should().BeTrue();
        Directory.Exists(Path.Combine(repoDir, ".dawvc", "objects")).Should().BeTrue();

        // --- 3. dawvc commit -m "Initial commit" ---
        var commitExitCode = await DawVcs.Cli.Program.Main(["commit", "-m", "Initial commit", "--dir", repoDir]);
        commitExitCode.Should().Be(0, "dawvc commit should exit with code 0");

        var headRefFile = Path.Combine(repoDir, ".dawvc", "refs", "heads", "main");
        File.Exists(headRefFile).Should().BeTrue();
        var commitHash = (await File.ReadAllTextAsync(headRefFile)).Trim();
        commitHash.Should().HaveLength(64);

        // --- 4. dawvc log ---
        var logExitCode = await DawVcs.Cli.Program.Main(["log", "--dir", repoDir]);
        logExitCode.Should().Be(0, "dawvc log should exit with code 0");

        // --- 5. dawvc checkout main --restore-to <restoreDir> ---
        var restoreDir = Path.Combine(_testWorkspace, "RestoredProject");
        var checkoutExitCode = await DawVcs.Cli.Program.Main(["checkout", "main", "--restore-to", restoreDir, "--dir", repoDir]);
        checkoutExitCode.Should().Be(0, "dawvc checkout should exit with code 0");

        var restoredProjectFile = Path.Combine(restoreDir, "Track.flp");
        File.Exists(restoredProjectFile).Should().BeTrue("restored project file must exist at target location");

        // Assert restored bytes are byte-for-byte identical to the original
        var restoredHash = await Blake3ContentHasher.HashFileAsync(restoredProjectFile);
        restoredHash.Should().Be(preHash, "restored .flp hash must match original .flp hash");

        var restoredBytes = await File.ReadAllBytesAsync(restoredProjectFile);
        restoredBytes.Should().Equal(originalBytes, "restored bytes must be 100% byte-exact identical to the original");

        // Assert original source file was NEVER modified by DAWVC
        var postHash = await Blake3ContentHasher.HashFileAsync(originalProjectFile);
        postHash.Should().Be(preHash, "original source file must not be modified by DAWVC operations (FR-FLP-002 / AC-001)");

        // --- 6. No-op commit rejection (FR-COM-003) ---
        var noOpExitCode = await DawVcs.Cli.Program.Main(["commit", "-m", "Unchanged commit", "--dir", repoDir]);
        noOpExitCode.Should().Be(0, "clean working tree commit should gracefully exit 0 with a notice");

        // Head ref should not have moved
        var currentHead = (await File.ReadAllTextAsync(headRefFile)).Trim();
        currentHead.Should().Be(commitHash);

        // --- 7. Checkout by commit prefix (e.g. 8 chars) ---
        var prefixRestoreDir = Path.Combine(_testWorkspace, "PrefixRestored");
        var prefix = commitHash[..8];
        var prefixCheckoutExitCode = await DawVcs.Cli.Program.Main(["checkout", prefix, "--restore-to", prefixRestoreDir, "--dir", repoDir]);
        prefixCheckoutExitCode.Should().Be(0, "checkout using commit prefix should succeed");

        var prefixRestoredFile = Path.Combine(prefixRestoreDir, "Track.flp");
        File.Exists(prefixRestoredFile).Should().BeTrue();
        var prefixRestoredHash = await Blake3ContentHasher.HashFileAsync(prefixRestoredFile);
        prefixRestoredHash.Should().Be(preHash);
    }
}
