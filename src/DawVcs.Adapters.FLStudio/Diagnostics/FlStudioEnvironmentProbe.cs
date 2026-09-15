using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using DawVcs.Adapters.Abstractions;

namespace DawVcs.Adapters.FLStudio.Diagnostics;

/// <summary>
/// Detecteert lokale installaties en omgevingsstatus van FL Studio op de hostmachine (FR-DOC-003, TD §31).
/// </summary>
public static class FlStudioEnvironmentProbe
{
    private static readonly (string Version, string SubDir)[] StandardVersions =
    [
        ("2026", "FL Studio 2026"),
        ("2024", "FL Studio 2024"),
        ("21", "FL Studio 21"),
        ("20", "FL Studio 20"),
        ("Generic", "FL Studio")
    ];

    public static Task<IReadOnlyList<DawEnvironmentFinding>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<DawEnvironmentFinding>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            findings.Add(new DawEnvironmentFinding(
                DawName: "FL Studio",
                Version: null,
                InstallationPath: null,
                IsInstalled: false,
                StatusMessage: "FL Studio discovery is only supported on Windows host environments."));
            return Task.FromResult<IReadOnlyList<DawEnvironmentFinding>>(findings);
        }

        var candidateRoots = new List<string>();

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf))
        {
            candidateRoots.Add(Path.Combine(pf, "Image-Line"));
        }

        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pfx86))
        {
            candidateRoots.Add(Path.Combine(pfx86, "Image-Line"));
        }

        // Scan file system roots
        foreach (var (version, subDir) in StandardVersions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? foundExe = null;

            foreach (var root in candidateRoots)
            {
                var dir = Path.Combine(root, subDir);
                if (!Directory.Exists(dir)) continue;

                var fl64 = Path.Combine(dir, "FL64.exe");
                if (File.Exists(fl64))
                {
                    foundExe = fl64;
                    break;
                }

                var fl = Path.Combine(dir, "FL.exe");
                if (File.Exists(fl))
                {
                    foundExe = fl;
                    break;
                }
            }

            if (foundExe != null)
            {
                findings.Add(new DawEnvironmentFinding(
                    DawName: "FL Studio",
                    Version: version,
                    InstallationPath: foundExe,
                    IsInstalled: true,
                    StatusMessage: $"Detected {version} at '{foundExe}'"));
            }
        }

        // Registry inspection if on Windows
        if (OperatingSystem.IsWindows())
        {
            CheckRegistry(findings);
        }

        if (findings.Count == 0)
        {
            findings.Add(new DawEnvironmentFinding(
                DawName: "FL Studio",
                Version: null,
                InstallationPath: null,
                IsInstalled: false,
                StatusMessage: "No standard FL Studio installation detected in default paths or registry."));
        }

        return Task.FromResult<IReadOnlyList<DawEnvironmentFinding>>(findings);
    }

    [SupportedOSPlatform("windows")]
    private static void CheckRegistry(List<DawEnvironmentFinding> findings)
    {
        try
        {
            using var baseKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Image-Line\FL Studio\Paths");
            if (baseKey != null)
            {
                var appPath = baseKey.GetValue("AppPath") as string;
                if (!string.IsNullOrEmpty(appPath))
                {
                    var exe = Path.Combine(appPath, "FL64.exe");
                    if (File.Exists(exe) && !findings.Any(f => string.Equals(f.InstallationPath, exe, StringComparison.OrdinalIgnoreCase)))
                    {
                        findings.Add(new DawEnvironmentFinding(
                            DawName: "FL Studio",
                            Version: "Registry",
                            InstallationPath: exe,
                            IsInstalled: true,
                            StatusMessage: $"Detected registry installation at '{exe}'"));
                    }
                }
            }
        }
        catch
        {
            // Best-effort registry read
        }
    }
}
