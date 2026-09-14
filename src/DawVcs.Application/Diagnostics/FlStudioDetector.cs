using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DawVcs.Application.Diagnostics;

/// <summary>
/// Detecteert lokale installaties van FL Studio op Windows (FR-DOC-003, TD §31).
/// </summary>
public static class FlStudioDetector
{
    private static readonly (string Version, string SubDir)[] StandardVersions =
    [
        ("2026", "FL Studio 2026"),
        ("2024", "FL Studio 2024"),
        ("21", "FL Studio 21"),
        ("20", "FL Studio 20"),
        ("Generic", "FL Studio")
    ];

    public static IReadOnlyList<DawInstallation> Detect()
    {
        var installations = new List<DawInstallation>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return installations;
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
                installations.Add(new DawInstallation(
                    DawName: "FL Studio",
                    Version: version,
                    ExecutablePath: foundExe,
                    IsDetected: true));
            }
        }

        // Registry inspection if on Windows
        if (OperatingSystem.IsWindows())
        {
            CheckRegistry(installations);
        }

        return installations;
    }

    [SupportedOSPlatform("windows")]
    private static void CheckRegistry(List<DawInstallation> installations)
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
                    if (File.Exists(exe) && !installations.Any(i => string.Equals(i.ExecutablePath, exe, StringComparison.OrdinalIgnoreCase)))
                    {
                        installations.Add(new DawInstallation(
                            DawName: "FL Studio",
                            Version: "Registry",
                            ExecutablePath: exe,
                            IsDetected: true));
                    }
                }
            }
        }
        catch
        {
            // Best effort registry read
        }
    }
}
