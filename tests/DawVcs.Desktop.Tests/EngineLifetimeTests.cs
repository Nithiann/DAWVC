using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Desktop.Services;
using DawVcs.Domain.Hashing;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace DawVcs.Desktop.Tests;

public sealed class EngineLifetimeTests
{
    [Fact]
    public async Task FreshOperationProviders_ObserveChangedMissingAndRelocatedSamples()
    {
        using var repositories = new TestRepositories();
        var root = await repositories.CreateAsync("repo", TestContext.Current.CancellationToken);
        var search = Path.Combine(repositories.Root, "samples-one");
        Directory.CreateDirectory(search);
        var sample = Path.Combine(search, "Kick.wav");
        await File.WriteAllBytesAsync(sample, [1, 2, 3], TestContext.Current.CancellationToken);
        var factory = new EngineScopeFactory(services => services.AddSingleton<IDawAdapterRegistry>(
            _ => new DawAdapterRegistry([new FLStudioAdapter([search])])));
        await using var coordinator = new OperationCoordinator(factory, new TestDispatcher());
        await coordinator.OpenAsync(root);
        string? resolved = null;
        ContentHash? hash = null;
        IDawAdapterRegistry? previousRegistry = null;
        async Task ReadAsync()
        {
            await coordinator.RunAsync(async (services, _, _, token) =>
            {
                var registry = services.GetRequiredService<IDawAdapterRegistry>();
                registry.Should().NotBeSameAs(previousRegistry);
                previousRegistry = registry;
                var adapter = (FLStudioAdapter)registry.FindAdapterForExtension(".flp")!;
                var path = adapter.ResolveSamplePath("Kick.wav");
                var content = path is null ? (ContentHash?)null : await Blake3ContentHasher.HashFileAsync(path, token);
                return (path, content);
            }, result => { resolved = result.path; hash = result.content; }, TestContext.Current.CancellationToken);
        }
        await ReadAsync();
        resolved.Should().Be(sample);
        var oldHash = hash;
        await File.WriteAllBytesAsync(sample, [9, 8, 7], TestContext.Current.CancellationToken);
        await ReadAsync();
        hash.Should().NotBe(oldHash);
        File.Delete(sample);
        await ReadAsync();
        resolved.Should().BeNull();
        search = Path.Combine(repositories.Root, "samples-two");
        Directory.CreateDirectory(search);
        var moved = Path.Combine(search, "Kick.wav");
        await File.WriteAllBytesAsync(moved, [4, 5, 6], TestContext.Current.CancellationToken);
        await ReadAsync();
        resolved.Should().Be(moved);
    }

    [Fact]
    public void Architecture_ReferencesApplicationAndBootstrapWithoutCliOrInfrastructure()
    {
        var references = typeof(OperationCoordinator).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        references.Should().Contain("DawVcs.Application").And.Contain("DawVcs.Bootstrap");
        references.Should().NotContain("DawVcs.Cli").And.NotContain("DawVcs.Infrastructure");
        foreach (var assembly in new[] { typeof(DawVcs.Application.Common.OperationProgress).Assembly, typeof(ContentHash).Assembly })
            assembly.GetReferencedAssemblies().Select(a => a.Name).Should().NotContain(name => name != null && (name.StartsWith("Avalonia", StringComparison.Ordinal) || name == "DawVcs.Desktop"));
    }
}
