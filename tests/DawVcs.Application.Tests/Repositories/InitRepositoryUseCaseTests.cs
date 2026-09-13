using DawVcs.Application.Repositories;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Repositories;

public sealed class InitRepositoryUseCaseTests : IDisposable
{
    private readonly string _testDir;
    private readonly IRepositoryContext _context;

    public InitRepositoryUseCaseTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "dawvc_init_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _context = Substitute.For<IRepositoryContext>();
        _context.RootPath.Returns(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Init_WithSingleFlpFile_AutodetectsPrimaryArtifact()
    {
        var flpFile = Path.Combine(_testDir, "Song.flp");
        await File.WriteAllBytesAsync(flpFile, [1, 2, 3]);

        var useCase = new InitRepositoryUseCase(_ => _context);
        var result = await useCase.ExecuteAsync(new InitRequest(_testDir));

        result.Should().NotBeNull();
        result.PrimaryArtifact.Value.Should().Be("Song.flp");
        result.DefaultBranch.Should().Be(BranchName.Main);
        result.WasAlreadyInitialized.Should().BeFalse();

        _context.Received(1).SaveConfig(Arg.Is<RepositoryConfig>(c => c.PrimaryArtifact.Value == "Song.flp"));
        _context.Received(1).SetCurrentBranch(BranchName.Main);
    }

    [Fact]
    public async Task Init_WithMultipleFlpFilesAndNoExplicitPrimary_ThrowsInvalidOperationException()
    {
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "Song1.flp"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(_testDir, "Song2.flp"), [2]);

        var useCase = new InitRepositoryUseCase(_ => _context);
        var act = async () => await useCase.ExecuteAsync(new InitRequest(_testDir));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Multiple project files found*");
    }

    [Fact]
    public async Task Init_WithNoFlpFilesAndNoExplicitPrimary_ThrowsInvalidOperationException()
    {
        var useCase = new InitRepositoryUseCase(_ => _context);
        var act = async () => await useCase.ExecuteAsync(new InitRequest(_testDir));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No .flp project file found*");
    }

    [Fact]
    public async Task Init_WithExplicitPrimary_ValidatesExistence()
    {
        var useCase = new InitRepositoryUseCase(_ => _context);
        var act = async () => await useCase.ExecuteAsync(new InitRequest(_testDir, PrimaryArtifactPath: "NonExistent.flp"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Init_WhenAlreadyInitialized_ReturnsWasAlreadyInitializedTrue()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, ".dawvc"));
        await File.WriteAllTextAsync(Path.Combine(_testDir, "dawvc.yaml"), "repositoryId: test");

        var existingConfig = new RepositoryConfig(RepositoryId.New(), "ExistingProj", new ArtifactPath("Main.flp"), BranchName.Main);
        _context.LoadConfig().Returns(existingConfig);

        var useCase = new InitRepositoryUseCase(_ => _context);
        var result = await useCase.ExecuteAsync(new InitRequest(_testDir));

        result.WasAlreadyInitialized.Should().BeTrue();
        result.ProjectName.Should().Be("ExistingProj");
    }
}
