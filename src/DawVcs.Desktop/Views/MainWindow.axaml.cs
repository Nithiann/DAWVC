using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;

using DawVcs.Desktop.ViewModels;

namespace DawVcs.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private ShellViewModel? _model;
    private bool _closing;
    private bool _canClose;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }
    public void Attach(ShellViewModel model)
    {
        _model = model;
        DataContext = model;
        model.PropertyChanged += ThemeChanged;
        ApplyTheme();
        Opened += async (_, _) => await model.LoadRecentsCommand.ExecuteAsync(null);
    }
    private void ApplyTheme()
    {
        if (global::Avalonia.Application.Current is { } app) app.RequestedThemeVariant = _model?.IsDark == true ? ThemeVariant.Dark : ThemeVariant.Light;
    }
    private void ThemeChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(ShellViewModel.IsDark)) ApplyTheme(); }
    private async void OpenRecent(object? sender, RoutedEventArgs args)
    {
        if (_model is not null && sender is Button { Tag: string path }) await _model.OpenRecentCommand.ExecuteAsync(path);
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_canClose || _model is null) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        await _model.DisposeAsync();
        _model.PropertyChanged -= ThemeChanged;
        _canClose = true;
        Dispatcher.UIThread.Post(Close);
    }
}
