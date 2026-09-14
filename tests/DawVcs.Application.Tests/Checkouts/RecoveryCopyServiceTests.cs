using DawVcs.Application.Checkouts;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Checkouts;

public sealed class RecoveryCopyServiceTests : IDisposable
{
    private readonly string _tempDir;

    public RecoveryCopyServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dawvc-recovery-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
            }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateRecoveryCopyAsync_BacksUpFilesAndGeneratesManifest()
    {
        var flpPath = Path.Combine(_tempDir, "Song.flp");
        await File.WriteAllBytesAsync(flpPath, new byte[] { 1, 2, 3 });

        var samplePath = Path.Combine(_tempDir, "Audio", "Lead.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(samplePath)!);
        await File.WriteAllBytesAsync(samplePath, new byte[] { 4, 5, 6, 7 });

        var filesToBackup = new[] { "Song.flp", "Audio/Lead.wav" };

        var recoveryDir = await RecoveryCopyService.CreateRecoveryCopyAsync(_tempDir, filesToBackup);

        Directory.Exists(recoveryDir).Should().BeTrue();
        File.Exists(Path.Combine(recoveryDir, "Song.flp")).Should().BeTrue();
        File.Exists(Path.Combine(recoveryDir, "Audio", "Lead.wav")).Should().BeTrue();

        var manifestPath = Path.Combine(recoveryDir, "manifest.txt");
        File.Exists(manifestPath).Should().BeTrue();

        var manifestText = await File.ReadAllTextAsync(manifestPath);
        manifestText.Should().Contain("Song.flp");
        manifestText.Should().Contain("Audio/Lead.wav");
    }
}
