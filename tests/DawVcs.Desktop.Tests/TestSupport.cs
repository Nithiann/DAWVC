using DawVcs.Application.Repositories;
using DawVcs.Bootstrap;
using DawVcs.Desktop.Services;

using Microsoft.Extensions.DependencyInjection;

namespace DawVcs.Desktop.Tests;

internal sealed class TestDispatcher : IUiDispatcher
{
    public Queue<Action> Posted { get; } = new();
    public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    public void Post(Action action) { lock (Posted) Posted.Enqueue(action); }
    public void Drain() { while (true) { Action action; lock (Posted) { if (!Posted.TryDequeue(out action!)) return; } action(); } }
}

internal sealed class TestRepositories : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "dawvc-desktop-" + Guid.NewGuid().ToString("N"));
    public async Task<string> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Root, name);
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "Project.flp"), new byte[100], cancellationToken);
        using var provider = new ServiceCollection().AddDawVcs().BuildServiceProvider();
        await provider.GetRequiredService<InitRepositoryUseCase>().ExecuteAsync(new InitRequest(directory, name, "Project.flp"), cancellationToken);
        return directory;
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
