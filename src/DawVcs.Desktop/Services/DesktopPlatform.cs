using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Input;

namespace DawVcs.Desktop.Services;

public interface IFolderPicker
{
    Task<string?> PickAsync();
}

public interface IUserDialogs
{
    Task<bool> ConfirmAsync(string title, string message);
}

public interface IFileExplorer
{
    void OpenDirectory(string path);
}

public sealed class DesktopPlatform(Func<Window?> owner) : IFolderPicker, IUserDialogs, IFileExplorer
{
    public async Task<string?> PickAsync()
    {
        var window = owner() ?? throw new InvalidOperationException("No desktop window is available.");
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new() { Title = "Open DAWVC repository", AllowMultiple = false });
        return folders is { Count: > 0 } && folders[0].Path.IsFile ? folders[0].Path.LocalPath : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var window = owner() ?? throw new InvalidOperationException("No desktop window is available.");
        var dialog = new Window { Title = title, Width = 440, Height = 200, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var yes = new Button { Content = "Continue" };
        var no = new Button { Content = "Cancel" };
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; dialog.Close(false); }
        };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { no, yes } } }
        };
        return await dialog.ShowDialog<bool>(window);
    }

    public void OpenDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException();
        // Shell association opens a directory URI; no command string or shell interpolation.
        using var process = Process.Start(new ProcessStartInfo(new Uri(full + Path.DirectorySeparatorChar).AbsoluteUri) { UseShellExecute = true });
    }
}
