using DawVcs.Application.Repositories;
using DawVcs.Bootstrap;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace DawVcs.GuiSpike.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task Filter_IsDeterministicAndHasNoRepositoryDependency()
    {
        await using var session = new SpikeSession();
        session.Rows.Should().HaveCount(10_000);
        session.Filter = "asset099";
        session.Rows.Should().HaveCount(100);
        session.Rows[0].Path.Should().Be("Samples/Asset09900.wav");
        session.Filter = "no-match";
        session.Rows.Should().BeEmpty();
        session.Filter = "";
        session.Rows.Should().HaveCount(10_000);
    }

    [Fact]
    public async Task InvalidFolder_DoesNotCreateBackendMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "wp3-invalid-" + Guid.NewGuid().ToString("N"));
        await using var session = new SpikeSession();
        session.Open(root).Should().BeFalse();
        Directory.Exists(root).Should().BeFalse();
        session.CanStart.Should().BeFalse();
    }

    [Fact]
    public async Task Status_IsRealAndDisposeWaitsForCompletion()
    {
        var root = Path.Combine(Path.GetTempPath(), "wp3-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "Project.flp"), new byte[100], TestContext.Current.CancellationToken);
            using var provider = new ServiceCollection().AddDawVcs().BuildServiceProvider();
            await provider.GetRequiredService<InitRepositoryUseCase>().ExecuteAsync(new InitRequest(root, "WP3", "Project.flp"), TestContext.Current.CancellationToken);
            await using var session = new SpikeSession();
            session.Open(root).Should().BeTrue();
            await session.RunAsync("Status");
            session.Summary.Should().Contain("Branch main").And.Contain("untracked");
            session.Open(Path.Combine(root, "missing")).Should().BeFalse();
            session.CanStart.Should().BeFalse();
            session.Open(root).Should().BeTrue();
            bool busyObserved = false;
            session.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(SpikeSession.Busy) && session.Busy)
                {
                    busyObserved = true;
                    session.CanOpen.Should().BeFalse();
                    session.Open(root).Should().BeFalse();
                }
            };
            var running = session.RunAsync("Status");
            busyObserved.Should().BeTrue();
            await session.DisposeAsync();
            running.IsCompleted.Should().BeTrue();
            session.Busy.Should().BeFalse();
            session.CanStart.Should().BeFalse();
        }
        finally { Directory.Delete(root, true); }
    }
}
