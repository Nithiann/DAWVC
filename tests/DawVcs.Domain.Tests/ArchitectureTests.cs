using FluentAssertions;

using NetArchTest.Rules;

using Xunit;

namespace DawVcs.Domain.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_ShouldNotHaveDependencyOn_Infrastructure()
    {
        var result = Types.InAssembly(typeof(DawVcs.Domain.Common.IDomainEntity).Assembly)
            .ShouldNot()
            .HaveDependencyOn("DawVcs.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Domain_ShouldNotHaveDependencyOn_Cli()
    {
        var result = Types.InAssembly(typeof(DawVcs.Domain.Common.IDomainEntity).Assembly)
            .ShouldNot()
            .HaveDependencyOn("DawVcs.Cli")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Domain_ShouldNotHaveDependencyOn_FLStudioAdapter()
    {
        var result = Types.InAssembly(typeof(DawVcs.Domain.Common.IDomainEntity).Assembly)
            .ShouldNot()
            .HaveDependencyOn("DawVcs.Adapters.FLStudio")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void FLStudioAdapter_ShouldNotHaveDependencyOn_Cli()
    {
        var result = Types.InAssembly(typeof(DawVcs.Adapters.FLStudio.FLStudioAdapter).Assembly)
            .ShouldNot()
            .HaveDependencyOn("DawVcs.Cli")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_ShouldNotHaveDependencyOn_Infrastructure()
    {
        var result = Types.InAssembly(typeof(DawVcs.Application.Common.IUseCase<,>).Assembly)
            .ShouldNot()
            .HaveDependencyOn("DawVcs.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
