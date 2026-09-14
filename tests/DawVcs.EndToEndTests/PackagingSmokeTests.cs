using System.IO.Compression;
using System.Security.Cryptography;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class PackagingSmokeTests
{
    private static string FindRepoRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "DawVcs.slnx")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        throw new InvalidOperationException("Repository root not found from " + Directory.GetCurrentDirectory());
    }

    [Fact]
    [Trait("Category", "Packaging")]
    [Trait("Requirement", "NFR-REL-001, NFR-REL-002, NFR-REL-005")]
    public async Task SelfContainedPackage_WhenPackaged_ContainsValidExecutableAndChecksums()
    {
        // Only run on Windows platform
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var repoRoot = FindRepoRoot();
        var artifactsDir = Path.Combine(repoRoot, "artifacts");
        var zipPath = Path.Combine(artifactsDir, "dawvc-v0.1.0-preview.1-win-x64.zip");
        var checksumPath = Path.Combine(artifactsDir, "SHA256SUMS.txt");

        // If release archive has not been packaged yet (e.g. running unit tests before packaging), skip
        if (!File.Exists(zipPath))
        {
            return;
        }

        // 1. Verify distribution checksum file exists and matches
        File.Exists(checksumPath).Should().BeTrue($"Checksum file must exist at {checksumPath}.");
        var zipBytes = await File.ReadAllBytesAsync(zipPath);
        var calculatedHash = Convert.ToHexStringLower(SHA256.HashData(zipBytes));
        var checksumContent = await File.ReadAllTextAsync(checksumPath);
        checksumContent.ToLowerInvariant().Should().Contain(calculatedHash);

        // 2. Verify ZIP archive structure and non-empty entries
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var entries = archive.Entries.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
            entries.Should().ContainKey("dawvc.exe");
            entries.Should().ContainKey("README.md");
            entries.Should().ContainKey("LICENSE");

            // 3. Inspect dawvc.exe header without spawning external processes
            var exeEntry = entries["dawvc.exe"];
            exeEntry.Length.Should().BeGreaterThan(10 * 1024 * 1024, "Self-contained single-file win-x64 binary must be bundled with runtime");

            using var exeStream = exeEntry.Open();
            var header = new byte[2];
            var bytesRead = await exeStream.ReadAsync(header.AsMemory(0, 2));
            bytesRead.Should().Be(2);

            // Windows PE executable magic header 'MZ' (0x4D, 0x5A)
            header[0].Should().Be(0x4D);
            header[1].Should().Be(0x5A);
        }

        // 4. Verify CLI entry point can be invoked cleanly in-process
        var versionExit = await DawVcs.Cli.Program.Main(["--version"]);
        versionExit.Should().Be(0);

        var helpExit = await DawVcs.Cli.Program.Main(["--help"]);
        helpExit.Should().Be(0);
    }
}
