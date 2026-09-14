using System.Buffers.Binary;
using System.Text;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class CliSmokeTests
{
    [Fact]
    public async Task Cli_WhenInvokedWithVersion_ShouldReturnZero()
    {
        var exitCode = await DawVcs.Cli.Program.Main(["--version"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Cli_WhenInvokedWithHelp_ShouldReturnZero()
    {
        var exitCode = await DawVcs.Cli.Program.Main(["--help"]);
        exitCode.Should().Be(0);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("switch")]
    [InlineData("doctor")]
    [InlineData("fsck")]
    public async Task Cli_Wp08Commands_WhenInvokedWithHelp_ShouldReturnZero(string command)
    {
        var exitCode = await DawVcs.Cli.Program.Main([command, "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "FR-BRA-006, FR-DOC-001, FR-FSC-001")]
    public async Task Cli_Wp08_FullLifecycle_BranchSwitchDoctorFsck()
    {
        using var temp = new TempDirectory();
        var flpPath = Path.Combine(temp.Path, "Song.flp");
        await File.WriteAllBytesAsync(flpPath, CreateFlp());

        // 1. init
        var initCode = await DawVcs.Cli.Program.Main(["init", "--dir", temp.Path, "--primary", "Song.flp"]);
        initCode.Should().Be(0);

        // 2. commit
        var commitCode = await DawVcs.Cli.Program.Main(["commit", "-m", "Initial commit", "--dir", temp.Path]);
        commitCode.Should().Be(0);

        // 3. branch (list)
        var branchListCode = await DawVcs.Cli.Program.Main(["branch", "--dir", temp.Path]);
        branchListCode.Should().Be(0);

        // 4. branch create
        var branchCreateCode = await DawVcs.Cli.Program.Main(["branch", "experiment", "--dir", temp.Path]);
        branchCreateCode.Should().Be(0);

        // 5. switch to experiment
        var switchCode = await DawVcs.Cli.Program.Main(["switch", "experiment", "--dir", temp.Path]);
        switchCode.Should().Be(0);

        // 6. switch back to main
        var switchBackCode = await DawVcs.Cli.Program.Main(["switch", "main", "--dir", temp.Path]);
        switchBackCode.Should().Be(0);

        // 7. make workspace dirty and test exit code 7 protection
        await File.WriteAllBytesAsync(flpPath, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        var dirtySwitchCode = await DawVcs.Cli.Program.Main(["switch", "experiment", "--dir", temp.Path]);
        dirtySwitchCode.Should().Be(7);

        // 8. switch with --force should succeed (exit code 0) and create recovery
        var forceSwitchCode = await DawVcs.Cli.Program.Main(["switch", "experiment", "--force", "--dir", temp.Path]);
        forceSwitchCode.Should().Be(0);

        // 9. doctor --json should succeed with exit code 0
        var doctorCode = await DawVcs.Cli.Program.Main(["doctor", "--json", "--dir", temp.Path]);
        doctorCode.Should().Be(0);

        // 10. fsck --artifacts --json should succeed with exit code 0
        var fsckCode = await DawVcs.Cli.Program.Main(["fsck", "--artifacts", "--json", "--dir", temp.Path]);
        fsckCode.Should().Be(0);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dawvc-cli-test-" + Guid.NewGuid().ToString("N"));

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
