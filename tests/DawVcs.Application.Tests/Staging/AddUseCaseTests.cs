using DawVcs.Application.Staging;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Staging;

public sealed class AddUseCaseTests : IDisposable
{
    private readonly string _testDir;
    private readonly IRepositoryContext _context;
    private readonly IObjectStore _objectStore;
    private readonly IStagingIndex _stagingIndex;
    private readonly RepositoryConfig _config;

    public AddUseCaseTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "dawvc_add_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _objectStore = Substitute.For<IObjectStore>();
        _stagingIndex = Substitute.For<IStagingIndex>();
        _context = Substitute.For<IRepositoryContext>();
        _context.RootPath.Returns(_testDir);
        _context.ObjectStore.Returns(_objectStore);
        _context.StagingIndex.Returns(_stagingIndex);

        _config = new RepositoryConfig(RepositoryId.New(), "TestSong", new ArtifactPath("Project.flp"), BranchName.Main);
        _context.LoadConfig().Returns(_config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Add_SpecificFile_StreamsBlobAndStagesEntry()
    {
        var samplePath = Path.Combine(_testDir, "Kick.wav");
        await File.WriteAllBytesAsync(samplePath, [1, 2, 3, 4, 5]);

        var dummyHash = new ContentHash(new byte[32]);
        _objectStore.WriteBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(dummyHash);

        var useCase = new AddUseCase(_ => _context);
        var result = await useCase.ExecuteAsync(new AddRequest(_testDir, Paths: ["Kick.wav"]));

        result.StagedPaths.Should().HaveCount(1);
        result.StagedPaths[0].Value.Should().Be("Kick.wav");

        await _objectStore.Received(1).WriteBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await _stagingIndex.Received(1).StageEntryAsync(Arg.Is<ArtifactEntry>(e => e.Path.Value == "Kick.wav"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_All_DiscoversAllFilesAndExcludesDawvcAndHidden()
    {
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "Project.flp"), [10]);
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "Snare.wav"), [20]);

        var hiddenDir = Path.Combine(_testDir, ".dawvc");
        Directory.CreateDirectory(hiddenDir);
        await File.WriteAllBytesAsync(Path.Combine(hiddenDir, "HEAD"), [30]);

        var dummyHash = new ContentHash(new byte[32]);
        _objectStore.WriteBlobAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(dummyHash);

        var useCase = new AddUseCase(_ => _context);
        var result = await useCase.ExecuteAsync(new AddRequest(_testDir, All: true));

        result.StagedPaths.Should().HaveCount(2);
        result.StagedPaths.Select(p => p.Value).Should().Contain("Project.flp");
        result.StagedPaths.Select(p => p.Value).Should().Contain("Snare.wav");
        result.StagedPaths.Select(p => p.Value).Should().NotContain(p => p.StartsWith(".dawvc"));
    }

    [Fact]
    public async Task Add_WhenPathOutsideRepo_ThrowsInvalidOperationException()
    {
        var outsideFile = Path.Combine(Path.GetTempPath(), "Outside_" + Guid.NewGuid().ToString("N") + ".wav");
        await File.WriteAllBytesAsync(outsideFile, [99]);

        try
        {
            var useCase = new AddUseCase(_ => _context);
            var act = async () => await useCase.ExecuteAsync(new AddRequest(_testDir, Paths: new[] { outsideFile }));

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*outside the repository root*");
        }
        finally
        {
            if (File.Exists(outsideFile)) File.Delete(outsideFile);
        }
    }
}
