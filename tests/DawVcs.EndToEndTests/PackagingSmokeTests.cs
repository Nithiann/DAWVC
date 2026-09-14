using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

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
    public async Task SelfContainedPackage_WhenExtracted_ExecutesSuccessfullyOnCleanSystem()
    {
        // Only run on Windows x64 platform
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var repoRoot = FindRepoRoot();
        var artifactsDir = Path.Combine(repoRoot, "artifacts");
        var zipPath = Path.Combine(artifactsDir, "dawvc-v0.1.0-preview.1-win-x64.zip");
        var checksumPath = Path.Combine(artifactsDir, "SHA256SUMS.txt");

        // 1. Verify distribution zip exists
        File.Exists(zipPath).Should().BeTrue($"Packaging artifact must exist at {zipPath}. Run scripts/package.ps1 first.");
        File.Exists(checksumPath).Should().BeTrue($"Checksum file must exist at {checksumPath}.");

        // 2. Verify SHA-256 checksum
        var zipBytes = await File.ReadAllBytesAsync(zipPath);
        var calculatedHash = Convert.ToHexStringLower(SHA256.HashData(zipBytes));
        var checksumContent = await File.ReadAllTextAsync(checksumPath);
        checksumContent.ToLowerInvariant().Should().Contain(calculatedHash);

        // 3. Verify ZIP contents structure
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var entryNames = archive.Entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            entryNames.Should().Contain("dawvc.exe");
            entryNames.Should().Contain("README.md");
            entryNames.Should().Contain("LICENSE");
        }

        // 4. Extract to isolated temp folder and run as external process
        using var tempDir = new TempDirectory();
        ZipFile.ExtractToDirectory(zipPath, tempDir.Path);
        var exePath = Path.Combine(tempDir.Path, "dawvc.exe");
        File.Exists(exePath).Should().BeTrue();

        // 5. Execute dawvc.exe --version
        var versionResult = await RunProcessAsync(exePath, ["--version"], tempDir.Path);
        versionResult.ExitCode.Should().Be(0);
        versionResult.Stdout.Should().Contain("0.1.0-preview.1");

        // 6. Execute dawvc.exe --help
        var helpResult = await RunProcessAsync(exePath, ["--help"], tempDir.Path);
        helpResult.ExitCode.Should().Be(0);
        helpResult.Stdout.Should().Contain("init");
        helpResult.Stdout.Should().Contain("commit");
        helpResult.Stdout.Should().Contain("status");
        helpResult.Stdout.Should().Contain("doctor");
        helpResult.Stdout.Should().Contain("fsck");

        // 7. Execute dawvc.exe init in clean workspace
        using var repoDir = new TempDirectory();
        var testFlp = Path.Combine(repoDir.Path, "Smoke.flp");
        await File.WriteAllBytesAsync(testFlp, Encoding.ASCII.GetBytes("FLhd\x06\x00\x00\x00\x00\x00\x01\x00\x60\x00FLdt\x00\x00\x00\x00"));

        var initResult = await RunProcessAsync(exePath, ["init", "--dir", repoDir.Path, "--primary", "Smoke.flp"], repoDir.Path);
        initResult.ExitCode.Should().Be(0);
        initResult.Stdout.Should().Contain("Initialized empty DAWVC repository");

        // 8. Execute dawvc.exe status
        var statusResult = await RunProcessAsync(exePath, ["status", "--dir", repoDir.Path], repoDir.Path);
        statusResult.ExitCode.Should().Be(0);
        statusResult.Stdout.Should().Contain("Smoke.flp");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(string exePath, string[] args, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dawvc_pkg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch
            {
                // Ignored in cleanup
            }
        }
    }
}
