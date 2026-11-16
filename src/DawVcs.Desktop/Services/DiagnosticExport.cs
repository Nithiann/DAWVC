using System.Text.Json;

using Avalonia.Platform.Storage;

namespace DawVcs.Desktop.Services;

public enum DiagnosticKind { Scan, Doctor, Fsck }

/// <summary>Export allowlist: counts only, no backend free text, author names, paths, locators or credentials.</summary>
public sealed record DiagnosticExport(DiagnosticKind Kind, bool Healthy, IReadOnlyDictionary<string, long> Counts)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string ToJson()
    {
        string[] keys = Kind switch
        {
            DiagnosticKind.Scan => ["opaqueFallback"],
            DiagnosticKind.Doctor => ["blocking", "warnings", "artifacts", "dependencies"],
            DiagnosticKind.Fsck => ["objects", "valid", "corrupt", "missing", "orphans"],
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        };
        var counts = keys.Where(Counts.ContainsKey).ToDictionary(key => key, key => Counts[key]);
        return JsonSerializer.Serialize(new { schemaVersion = 1, operation = Kind.ToString(), Healthy, Counts = counts }, Options);
    }
}

public interface IDiagnosticExporter
{
    Task<bool> SaveAsync(DiagnosticExport report, string repositoryRoot, CancellationToken cancellationToken = default);
}

public sealed class DiagnosticExporter(Func<Avalonia.Controls.Window?> owner) : IDiagnosticExporter
{
    public async Task<bool> SaveAsync(DiagnosticExport report, string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var window = owner() ?? throw new InvalidOperationException("No desktop window is available.");
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export diagnostic summary",
            SuggestedFileName = "DAWVC-diagnostics.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
            ShowOverwritePrompt = true
        });
        if (file is null) return false;
        using (file)
        {
            if (!file.Path.IsFile) throw new ArgumentException("Choose a local report destination outside the repository.");
            ValidateDestination(repositoryRoot, file.Path.LocalPath);
            await using var stream = await file.OpenWriteAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (stream.CanSeek) stream.SetLength(0);
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(report.ToJson()), cancellationToken);
            return true;
        }
    }

    public static void ValidateDestination(string root, string destination)
    {
        // Refuse linked destinations/ancestors rather than risk writing into a repository through an alias.
        static string Physical(string path)
        {
            var full = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(full);
            if (parent is null || parent == full) return full;
            var canonicalParent = Physical(parent);
            var candidate = Path.Combine(canonicalParent, Path.GetFileName(full));
            FileSystemInfo item = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
            return item.LinkTarget is not null ? item.ResolveLinkTarget(true)!.FullName : candidate;
        }
        var repository = Physical(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var output = Physical(destination);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!output.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || output.StartsWith(repository, comparison))
            throw new ArgumentException("Choose a JSON report destination outside the repository.");
    }
}
