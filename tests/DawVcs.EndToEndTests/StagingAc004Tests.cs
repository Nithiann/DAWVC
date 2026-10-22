using System.Buffers.Binary;
using System.Text;

using DawVcs.Domain.Hashing;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class StagingAc004Tests : IDisposable
{
    private readonly string _testWorkspace;

    public StagingAc004Tests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), "dawvc_ac004_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testWorkspace);
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
    [Trait("AcceptanceCriterion", "AC-004")]
    [Trait("Requirement", "FR-STG-003")]
    [Trait("Requirement", "FR-STG-004")]
    public async Task Ac004_NewSampleRequiresExplicitAdd_IsNotSilentlyBundled()
    {
        var repoDir = Path.Combine(_testWorkspace, "Project");
        Directory.CreateDirectory(repoDir);

        var flpPath = Path.Combine(repoDir, "Song.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        // 1. Initialize and commit initial state
        var initCode = await DawVcs.Cli.Program.Main(["init", "--dir", repoDir]);
        initCode.Should().Be(0);

        var commit1Code = await DawVcs.Cli.Program.Main(["commit", "-m", "Initial commit", "--dir", repoDir]);
        commit1Code.Should().Be(0);

        var headRefFile = Path.Combine(repoDir, ".dawvc", "refs", "heads", "main");
        var firstCommitId = (await File.ReadAllTextAsync(headRefFile)).Trim();

        // 2. Discover a new bundlable sample in working directory
        var sampleBytes = new byte[] { 11, 22, 33, 44, 55 };
        var samplePath = Path.Combine(repoDir, "Kick.wav");
        await File.WriteAllBytesAsync(samplePath, sampleBytes);
        var sampleHash = Blake3ContentHasher.Hash(sampleBytes);

        // 3. Status shows Kick.wav as untracked
        var status1Code = await DawVcs.Cli.Program.Main(["status", "--dir", repoDir]);
        status1Code.Should().Be(0);

        // 4. Attempt direct commit without add -> Kick.wav must NOT be silently bundled (AC-004, FR-STG-003)
        var unaddedCommitCode = await DawVcs.Cli.Program.Main(["commit", "-m", "Trying to commit without adding sample", "--dir", repoDir]);
        unaddedCommitCode.Should().Be(0); // Exits 0 with "Nothing to commit, working tree clean."

        var headAfterUnadded = (await File.ReadAllTextAsync(headRefFile)).Trim();
        headAfterUnadded.Should().Be(firstCommitId, "commit without add must not advance HEAD or silently bundle untracked files");

        // 5. User executes 'dawvc add Kick.wav' (FR-STG-004)
        var addCode = await DawVcs.Cli.Program.Main(["add", "Kick.wav", "--dir", repoDir]);
        addCode.Should().Be(0);

        // 6. User commits again -> Kick.wav IS now bundled
        var commit2Code = await DawVcs.Cli.Program.Main(["commit", "-m", "Added kick sample", "--dir", repoDir]);
        commit2Code.Should().Be(0);

        var secondCommitId = (await File.ReadAllTextAsync(headRefFile)).Trim();
        secondCommitId.Should().NotBe(firstCommitId, "HEAD must advance after committing staged asset");

        // 7. Verify checkout restores BOTH the .flp and the bundled Kick.wav byte-exact
        var restoreDir = Path.Combine(_testWorkspace, "Restored");
        var checkoutCode = await DawVcs.Cli.Program.Main(["checkout", secondCommitId, "--restore-to", restoreDir, "--dir", repoDir]);
        checkoutCode.Should().Be(0);

        File.Exists(Path.Combine(restoreDir, "Song.flp")).Should().BeTrue();
        var restoredSamplePath = Path.Combine(restoreDir, "Kick.wav");
        File.Exists(restoredSamplePath).Should().BeTrue("restored snapshot must contain the bundled Kick.wav");

        var restoredSampleHash = await Blake3ContentHasher.HashFileAsync(restoredSamplePath);
        restoredSampleHash.Should().Be(sampleHash);
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
