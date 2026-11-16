using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace DawVcs.GuiSpike.AvaloniaHost;

public sealed partial class MainWindow : Window, IAsyncDisposable
{
    private readonly SpikeSession _session = new();
    private bool _closing;
    private bool _canClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _session;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles()?.ToArray();
            if (files is { Length: 1 } && files[0].Path.IsFile && _session.Open(files[0].Path.LocalPath))
                RepositoryPath.Text = files[0].Path.LocalPath;
        });
        Closing += async (_, e) =>
        {
            if (_canClose) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            await DisposeAsync();
            _canClose = true;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        };
    }

    private async void Choose(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { AllowMultiple = false, Title = "Disposable DAWVC repository" });
        if (folders.Count > 0 && folders[0].Path.IsFile)
        {
            RepositoryPath.Text = folders[0].Path.LocalPath;
            _session.Open(RepositoryPath.Text);
        }
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();

    private void OpenPath(object? sender, RoutedEventArgs e) => _session.Open(RepositoryPath.Text ?? "");
    private async void Run(object? sender, RoutedEventArgs e) => await _session.RunAsync(((Button)sender!).Content!.ToString()!);
    private void Cancel(object? sender, RoutedEventArgs e) => _session.Cancel();
    private void ToggleTheme(object? sender, RoutedEventArgs e)
    {
        var app = global::Avalonia.Application.Current!;
        app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
