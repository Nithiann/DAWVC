using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Microsoft.Win32;

namespace DawVcs.GuiSpike.WpfHost;

public sealed partial class MainWindow : Window, IAsyncDisposable
{
    private readonly SpikeSession _session = new();
    private bool _closing;
    private bool _canClose;
    private bool _dark;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _session;
        Closing += async (_, e) =>
        {
            if (_canClose) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            await DisposeAsync();
            _canClose = true;
            await Dispatcher.InvokeAsync(Close);
        };
    }

    private void Choose(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Disposable DAWVC repository", Multiselect = false };
        if (picker.ShowDialog(this) == true) { RepositoryPath.Text = picker.FolderName; _session.Open(picker.FolderName); }
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();

    private void OpenPath(object sender, RoutedEventArgs e) => _session.Open(RepositoryPath.Text);
    private async void Run(object sender, RoutedEventArgs e) => await _session.RunAsync(((Button)sender).Content.ToString()!);
    private void Cancel(object sender, RoutedEventArgs e) => _session.Cancel();
    private void ToggleTheme(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        Resources["SpikeBackground"] = _dark ? Brushes.Black : Brushes.White;
        Resources["SpikeForeground"] = _dark ? Brushes.White : Brushes.Black;
    }

    private void DropFolder(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths && _session.Open(paths[0])) RepositoryPath.Text = paths[0];
    }
}
