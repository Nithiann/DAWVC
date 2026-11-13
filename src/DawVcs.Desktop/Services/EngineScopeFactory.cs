using DawVcs.Bootstrap;

using Microsoft.Extensions.DependencyInjection;

namespace DawVcs.Desktop.Services;

public interface IEngineScope : IDisposable
{
    IServiceProvider Services { get; }
}

public interface IEngineScopeFactory
{
    IEngineScope Create();
}

/// <summary>Fresh Bootstrap provider per operation; adapter caches never cross rescans or repository sessions.</summary>
public sealed class EngineScopeFactory(Action<IServiceCollection>? configure = null) : IEngineScopeFactory
{
    public IEngineScope Create()
    {
        var services = new ServiceCollection().AddDawVcs();
        configure?.Invoke(services);
        return new EngineScope(services.BuildServiceProvider());
    }

    private sealed class EngineScope(ServiceProvider provider) : IEngineScope
    {
        public IServiceProvider Services => provider;
        public void Dispose() => provider.Dispose();
    }
}
