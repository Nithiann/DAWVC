using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Branches;
using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Dependencies;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Remotes;
using DawVcs.Application.Repositories;
using DawVcs.Application.Scanning;
using DawVcs.Application.Staging;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Repositories;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace DawVcs.Bootstrap.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    public static TheoryData<Type> SharedUseCases => new()
    {
        typeof(InitRepositoryUseCase),
        typeof(CommitUseCase),
        typeof(LogUseCase),
        typeof(CheckoutRestoreUseCase),
        typeof(CheckoutUseCase),
        typeof(BindDependencyUseCase),
        typeof(StatusUseCase),
        typeof(AddUseCase),
        typeof(ScanUseCase),
        typeof(BranchUseCase),
        typeof(SwitchUseCase),
        typeof(DoctorUseCase),
        typeof(FsckUseCase),
        typeof(RemoteUseCase),
        typeof(ObjectGraphWalker),
        typeof(FetchUseCase),
        typeof(AncestryService),
        typeof(PullUseCase),
        typeof(CloneUseCase)
    };

    [Fact]
    public void AddDawVcs_ReturnsTheHostCollection()
    {
        var services = new ServiceCollection();

        services.AddDawVcs().Should().BeSameAs(services);
    }

    [Fact]
    public void AddDawVcs_RejectsNullCollection()
    {
        var act = () => ServiceCollectionExtensions.AddDawVcs(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Theory]
    [MemberData(nameof(SharedUseCases))]
    public void AddDawVcs_ResolvesEverySharedUseCaseAsTransient(Type serviceType)
    {
        var services = new ServiceCollection().AddDawVcs();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var first = provider.GetRequiredService(serviceType);
        var second = provider.GetRequiredService(serviceType);

        first.Should().BeOfType(serviceType);
        second.Should().NotBeSameAs(first);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == serviceType && descriptor.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public void AddDawVcs_SharesFactoriesAndAdapterRegistryOnlyWithinProvider()
    {
        var services = new ServiceCollection().AddDawVcs();
        using var first = services.BuildServiceProvider();
        using var second = services.BuildServiceProvider();

        var registry = first.GetRequiredService<IDawAdapterRegistry>();
        first.GetRequiredService<IDawAdapterRegistry>().Should().BeSameAs(registry);
        second.GetRequiredService<IDawAdapterRegistry>().Should().NotBeSameAs(registry);
        registry.GetAllAdapters().Should().ContainSingle().Which.Should().BeOfType<FLStudioAdapter>();
        registry.FindAdapterForExtension(".flp").Should().BeSameAs(registry.FindAdapterForFile("song.flp"));
        second.GetRequiredService<IDawAdapterRegistry>().FindAdapterForExtension(".flp")
            .Should().NotBeSameAs(registry.FindAdapterForExtension(".flp"));

        var contexts = first.GetRequiredService<Func<string, IRepositoryContext>>();
        first.GetRequiredService<Func<string, IRepositoryContext>>().Should().BeSameAs(contexts);
        var transports = first.GetRequiredService<IRemoteTransportFactory>();
        transports.Should().BeOfType<DefaultRemoteTransportFactory>();
        first.GetRequiredService<IRemoteTransportFactory>().Should().BeSameAs(transports);
        second.GetRequiredService<IRemoteTransportFactory>().Should().NotBeSameAs(transports);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(Func<string, IQuarantineStore>) &&
            descriptor.Lifetime == ServiceLifetime.Transient);
    }

    [Fact]
    public void RepositoryFactory_CreatesFreshContextsWithIndependentOwnedResources()
    {
        var root = Path.Combine(Path.GetTempPath(), "DawVcs.Bootstrap.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new ServiceCollection().AddDawVcs().BuildServiceProvider();
            var factory = provider.GetRequiredService<Func<string, IRepositoryContext>>();
            using var first = factory(root);
            using var second = factory(root);

            first.Should().BeOfType<FileSystemRepositoryContext>();
            second.Should().NotBeSameAs(first);
            first.RootPath.Should().Be(Path.GetFullPath(root));
            first.StagingIndex.Should().NotBeSameAs(second.StagingIndex);
            first.LocalBindings.Should().NotBeSameAs(second.LocalBindings);
            Directory.Exists(Path.Combine(root, ".dawvc", "objects", ".tmp")).Should().BeTrue();
            File.Exists(Path.Combine(root, ".dawvc", "index.db")).Should().BeFalse();
            File.Exists(Path.Combine(root, "dawvc.yaml")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RepositoryFactory_ContextOutlivesProviderAndReleasesBindingsWhenOwnerDisposes()
    {
        var root = Path.Combine(Path.GetTempPath(), "DawVcs.Bootstrap.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            IRepositoryContext context;
            using (var provider = new ServiceCollection().AddDawVcs().BuildServiceProvider())
            {
                context = provider.GetRequiredService<Func<string, IRepositoryContext>>()(root);
            }

            using (context)
            {
                var bindings = await context.LocalBindings.GetAllAsync();
                bindings.Should().BeEmpty();
            }

            var act = () => context.LocalBindings.GetAllAsync();
            await act.Should().ThrowAsync<ObjectDisposedException>();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void QuarantineFactory_CreatesIndependentSessionsCleanedUpByTheirOwners()
    {
        var root = Path.Combine(Path.GetTempPath(), "DawVcs.Bootstrap.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new ServiceCollection().AddDawVcs().BuildServiceProvider();
            var factory = provider.GetRequiredService<Func<string, IQuarantineStore>>();
            string firstSession;
            string secondSession;
            using (var first = factory(root))
            using (var second = factory(root))
            {
                firstSession = first.SessionDirectory;
                secondSession = second.SessionDirectory;
                secondSession.Should().NotBe(firstSession);
                Directory.Exists(firstSession).Should().BeTrue();
                Directory.Exists(secondSession).Should().BeTrue();
            }

            Directory.Exists(firstSession).Should().BeFalse();
            Directory.Exists(secondSession).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TransportFactory_CreatesFreshCallerOwnedTransports()
    {
        using var provider = new ServiceCollection().AddDawVcs().BuildServiceProvider();
        var factory = provider.GetRequiredService<IRemoteTransportFactory>();
        using var first = factory.CreateTransport(Path.GetTempPath());
        using var second = factory.CreateTransport(Path.GetTempPath());

        first.Should().BeOfType<FileSystemRemoteTransport>();
        second.Should().NotBeSameAs(first);
    }
}
