using DawVcs.Application.Common;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Staging;

public sealed record AddRequest(
    string RepositoryDirectory,
    IReadOnlyList<string>? Paths = null,
    bool All = false);

public sealed record AddResult(
    IReadOnlyList<ArtifactPath> StagedPaths);

/// <summary>
/// Orchestrates explicit staging of new or modified assets (FR-STG-004, FR-STG-005, AC-004).
/// </summary>
public sealed class AddUseCase : IUseCase<AddRequest, AddResult>
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dawvc",
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj"
    };

    private readonly Func<string, IRepositoryContext> _contextFactory;

    public AddUseCase(Func<string, IRepositoryContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public async Task<AddResult> ExecuteAsync(AddRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryDirectory);

        var context = _contextFactory(request.RepositoryDirectory);
        var repoRoot = Path.GetFullPath(context.RootPath);
        var config = context.LoadConfig();

        var candidateFiles = new List<string>();

        if (request.All)
        {
            var files = Directory.GetFiles(repoRoot, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relPath = Path.GetRelativePath(repoRoot, file);
                var segments = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(s => IgnoredDirectoryNames.Contains(s) || s.StartsWith('.')))
                {
                    continue;
                }

                candidateFiles.Add(file);
            }
        }
        else if (request.Paths is not null && request.Paths.Count > 0)
        {
            foreach (var path in request.Paths)
            {
                var fullPath = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(repoRoot, path));

                if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Path '{path}' is outside the repository root.");
                }

                if (Directory.Exists(fullPath))
                {
                    var files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        var rel = Path.GetRelativePath(repoRoot, f);
                        var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (!segments.Any(s => IgnoredDirectoryNames.Contains(s) || s.StartsWith('.')))
                        {
                            candidateFiles.Add(f);
                        }
                    }
                }
                else if (File.Exists(fullPath))
                {
                    candidateFiles.Add(fullPath);
                }
                else
                {
                    throw new FileNotFoundException($"File '{path}' does not exist in repository.", fullPath);
                }
            }
        }
        else
        {
            throw new ArgumentException("No path specified to add. Use 'dawvc add <path>' or 'dawvc add --all'.", nameof(request));
        }

        var stagedPaths = new List<ArtifactPath>();

        foreach (var filePath in candidateFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var relPath = Path.GetRelativePath(repoRoot, filePath);
            if (!ArtifactPath.TryCreate(relPath, out var artifactPath, out var error))
            {
                throw new InvalidOperationException($"Cannot stage invalid path '{relPath}': {error}");
            }

            if (relPath.StartsWith(".dawvc", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileInfo = new FileInfo(filePath);
            var fileLength = fileInfo.Length;
            var lastModifiedUtc = fileInfo.LastWriteTimeUtc;

            // 1. Check cache for quick BLAKE3 hash
            var cachedHash = await context.StagingIndex.TryGetCachedHashAsync(artifactPath, fileLength, lastModifiedUtc, cancellationToken).ConfigureAwait(false);
            ContentHash blobHash;

            if (cachedHash.HasValue && context.ObjectStore.Exists(cachedHash.Value))
            {
                blobHash = cachedHash.Value;
            }
            else
            {
                // Stream into object store
                await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous))
                {
                    blobHash = await context.ObjectStore.WriteBlobAsync(stream, cancellationToken).ConfigureAwait(false);
                }

                await context.StagingIndex.SetCachedHashAsync(artifactPath, fileLength, lastModifiedUtc, blobHash, cancellationToken).ConfigureAwait(false);
            }

            // 2. Determine artifact role
            var role = (artifactPath == config.PrimaryArtifact)
                ? ArtifactRole.PrimaryProjectFile
                : ArtifactRole.ProjectAsset;

            var entry = ArtifactEntry.Create(artifactPath, blobHash, fileLength, role);

            // 3. Stage entry
            await context.StagingIndex.StageEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            stagedPaths.Add(artifactPath);
        }

        return new AddResult(stagedPaths);
    }
}
