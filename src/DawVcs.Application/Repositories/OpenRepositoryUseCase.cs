using DawVcs.Application.Common;
using DawVcs.Domain.Repositories;

namespace DawVcs.Application.Repositories;

public sealed record OpenRepositoryRequest(string Directory);
public sealed record OpenRepositoryResult(string Root, string ProjectName, string Branch);

/// <summary>Validates an existing repository without status hashing or staged-index recovery.</summary>
public sealed class OpenRepositoryUseCase(Func<string, IRepositoryContext> contextFactory) : IUseCase<OpenRepositoryRequest, OpenRepositoryResult>
{
    public Task<OpenRepositoryResult> ExecuteAsync(OpenRepositoryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(request.Directory);
        if (!File.Exists(Path.Combine(root, "dawvc.yaml")) || !System.IO.Directory.Exists(Path.Combine(root, ".dawvc")))
            throw new ArgumentException("Choose an initialized DAWVC repository.", nameof(request));
        using var context = contextFactory(root);
        var config = context.LoadConfig();
        var branch = context.GetCurrentBranch();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OpenRepositoryResult(root, config.ProjectName, branch.Value));
    }
}
