using DawVcs.Application.Repositories;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Repositories;

public sealed class OpenRepositoryUseCaseTests
{
    [Fact]
    public async Task MissingRepositoryAndPreCancellation_NeverConstructContext()
    {
        bool created = false;
        var query = new OpenRepositoryUseCase(_ => { created = true; throw new InvalidOperationException(); });
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var invalid = () => query.ExecuteAsync(new OpenRepositoryRequest(path));
        await invalid.Should().ThrowAsync<ArgumentException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancel = () => query.ExecuteAsync(new OpenRepositoryRequest(path), cancelled.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        created.Should().BeFalse();
        Directory.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_IsDisposedOnSuccessAndConfigurationFailure_WithoutIndexAccess(bool fail)
    {
        var root = Path.Combine(Path.GetTempPath(), "dawvc-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".dawvc"));
        await File.WriteAllTextAsync(Path.Combine(root, "dawvc.yaml"), "placeholder");
        var context = Substitute.For<IRepositoryContext>();
        context.GetCurrentBranch().Returns(BranchName.Main);
        if (fail) context.LoadConfig().Returns(_ => throw new InvalidDataException());
        else context.LoadConfig().Returns(new RepositoryConfig(RepositoryId.New(), "Project", new ArtifactPath("Project.flp")));
        try
        {
            var query = new OpenRepositoryUseCase(_ => context);
            var act = () => query.ExecuteAsync(new OpenRepositoryRequest(root));
            if (fail) await act.Should().ThrowAsync<InvalidDataException>();
            else (await query.ExecuteAsync(new OpenRepositoryRequest(root))).ProjectName.Should().Be("Project");
            context.Received(1).Dispose();
            _ = context.DidNotReceive().StagingIndex;
        }
        finally { Directory.Delete(root, true); }
    }
}
