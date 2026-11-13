using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using DawVcs.Desktop.Services;
using DawVcs.Desktop.ViewModels;
using DawVcs.Desktop.Views;

using Microsoft.Extensions.DependencyInjection;

namespace DawVcs.Desktop;

public sealed partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var provider = CreateServices(() => window);
            var model = provider.GetRequiredService<ShellViewModel>();
            window.Attach(model);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static ServiceProvider CreateServices(Func<Window?> owner)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<IEngineScopeFactory, EngineScopeFactory>();
        services.AddSingleton<OperationCoordinator>();
        services.AddSingleton<IRecentRepositories>(_ => new RecentRepositories(RecentRepositories.DefaultPath));
        services.AddSingleton(_ => new DesktopPlatform(owner));
        services.AddSingleton<IFolderPicker>(sp => sp.GetRequiredService<DesktopPlatform>());
        services.AddSingleton<IUserDialogs>(sp => sp.GetRequiredService<DesktopPlatform>());
        services.AddSingleton<IFileExplorer>(sp => sp.GetRequiredService<DesktopPlatform>());
        services.AddSingleton<ShellViewModel>();
        return services.BuildServiceProvider();
    }
}
