using DawVcs.Application.Remotes;
using DawVcs.Domain.Common;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace DawVcs.Application.Tests.Remotes;

public sealed class RemoteUseCaseTests
{
    private readonly IRepositoryContext _context = Substitute.For<IRepositoryContext>();
    private readonly RemoteUseCase _useCase;

    public RemoteUseCaseTests()
    {
        _useCase = new RemoteUseCase(_ => _context);
    }

    [Fact]
    public void AddRemote_ValidNameAndUrl_SavesAndReturnsConfig()
    {
        _context.GetRemote("origin").Returns((RemoteConfig?)null);

        var request = new RemoteAddRequest("C:\\repo", "origin", "https://example.com/repo.git");
        var result = _useCase.AddRemote(request);

        result.Name.Value.Should().Be("origin");
        result.Url.Should().Be("https://example.com/repo.git");
        _context.Received(1).SaveRemote(Arg.Is<RemoteConfig>(r => r.Name.Value == "origin" && r.Url == "https://example.com/repo.git"));
    }

    [Fact]
    public void AddRemote_DuplicateName_ThrowsInvalidOperationException()
    {
        _context.GetRemote("origin")
            .Returns(new RemoteConfig(RemoteName.Origin, "https://old.url"));

        var request = new RemoteAddRequest("C:\\repo", "origin", "https://new.url");
        var act = () => _useCase.AddRemote(request);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid name with spaces")]
    [InlineData("-starts-with-hyphen")]
    public void AddRemote_InvalidName_ThrowsArgumentException(string invalidName)
    {
        var request = new RemoteAddRequest("C:\\repo", invalidName, "https://example.com");
        var act = () => _useCase.AddRemote(request);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddRemote_EmptyUrl_ThrowsArgumentException(string emptyUrl)
    {
        var request = new RemoteAddRequest("C:\\repo", "origin", emptyUrl);
        var act = () => _useCase.AddRemote(request);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*URL*");
    }

    [Fact]
    public void RemoveRemote_Existing_CallsContextAndReturnsTrue()
    {
        _context.RemoveRemote("origin").Returns(true);

        var result = _useCase.RemoveRemote(new RemoteRemoveRequest("C:\\repo", "origin"));

        result.Should().BeTrue();
        _context.Received(1).RemoveRemote("origin");
    }

    [Fact]
    public void RemoveRemote_NonExisting_ReturnsFalse()
    {
        _context.RemoveRemote("upstream").Returns(false);

        var result = _useCase.RemoveRemote(new RemoteRemoveRequest("C:\\repo", "upstream"));

        result.Should().BeFalse();
    }

    [Fact]
    public void ListRemotes_ReturnsAllConfiguredRemotes()
    {
        var remotes = new List<RemoteConfig>
        {
            new(RemoteName.Origin, @"\\NAS\Share"),
            new(RemoteName.Parse("backup"), @"D:\Backup")
        };
        _context.GetRemotes().Returns(remotes);

        var result = _useCase.ListRemotes(new RemoteListRequest("C:\\repo"));

        result.Remotes.Should().HaveCount(2);
        result.Remotes[0].Name.Value.Should().Be("origin");
        result.Remotes[1].Name.Value.Should().Be("backup");
    }
}
