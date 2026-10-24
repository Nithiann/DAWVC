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
using DawVcs.Infrastructure.Storage;

using Microsoft.Extensions.DependencyInjection;

namespace DawVcs.Bootstrap;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared engine without building or owning the host's service provider.
    /// Factory-created contexts, transports and quarantine stores are owned by their callers.
    /// </summary>
    public static IServiceCollection AddDawVcs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<Func<string, IRepositoryContext>>(sp => dir => new FileSystemRepositoryContext(dir));
        services.AddSingleton<IDawAdapterRegistry>(sp => new DawAdapterRegistry([new FLStudioAdapter()]));
        services.AddTransient<InitRepositoryUseCase>();
        services.AddTransient<CommitUseCase>(sp => new CommitUseCase(
            sp.GetRequiredService<Func<string, IRepositoryContext>>(),
            sp.GetRequiredService<IDawAdapterRegistry>()));
        services.AddTransient<LogUseCase>();
        services.AddTransient<CheckoutRestoreUseCase>();
        services.AddTransient<CheckoutUseCase>(sp => new CheckoutUseCase(
            sp.GetRequiredService<Func<string, IRepositoryContext>>(),
            sp.GetRequiredService<IDawAdapterRegistry>()));
        services.AddTransient<BindDependencyUseCase>();
        services.AddTransient<StatusUseCase>();
        services.AddTransient<AddUseCase>();
        services.AddTransient<ScanUseCase>();
        services.AddTransient<BranchUseCase>();
        services.AddTransient<SwitchUseCase>(sp => new SwitchUseCase(
            sp.GetRequiredService<Func<string, IRepositoryContext>>(),
            sp.GetRequiredService<IDawAdapterRegistry>()));
        services.AddTransient<DoctorUseCase>(sp => new DoctorUseCase(
            sp.GetRequiredService<Func<string, IRepositoryContext>>(),
            sp.GetRequiredService<IDawAdapterRegistry>()));
        services.AddTransient<FsckUseCase>(sp => new FsckUseCase(
            sp.GetRequiredService<Func<string, IRepositoryContext>>(),
            sp.GetRequiredService<IDawAdapterRegistry>()));
        services.AddTransient<RemoteUseCase>();
        services.AddSingleton<IRemoteTransportFactory, DefaultRemoteTransportFactory>();
        services.AddTransient<Func<string, IQuarantineStore>>(_ => dir => new QuarantineObjectStore(dir));
        services.AddTransient<ObjectGraphWalker>();
        services.AddTransient<FetchUseCase>();
        services.AddTransient<AncestryService>();
        services.AddTransient<PullUseCase>(sp => new PullUseCase(
            sp.GetRequiredService<Func<string, IRepositoryContext>>(),
            sp.GetRequiredService<FetchUseCase>(),
            sp.GetRequiredService<CheckoutUseCase>(),
            sp.GetRequiredService<AncestryService>(),
            sp.GetRequiredService<DoctorUseCase>()));
        services.AddTransient<CloneUseCase>(sp => new CloneUseCase(
            sp.GetRequiredService<Func<string, IRepositoryContext>>(),
            sp.GetRequiredService<IRemoteTransportFactory>(),
            sp.GetRequiredService<FetchUseCase>(),
            sp.GetRequiredService<CheckoutUseCase>(),
            sp.GetRequiredService<DoctorUseCase>()));

        return services;
    }
}
