using Microsoft.Extensions.DependencyInjection;

using Photino.Blazor;

namespace DawVcs.GuiSpike.PhotinoHost;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddSingleton<SpikeSession>();
        builder.RootComponents.Add<Main>("app");
        var app = builder.Build();
        var session = app.Services.GetRequiredService<SpikeSession>();
        bool closing = false;
        bool ready = false;
        app.MainWindow.SetFileSystemAccessEnabled(false).SetTitle("DAWVC — Photino WP3 spike").SetSize(1100, 760)
            .RegisterWindowClosingHandler((_, _) =>
            {
                if (ready) return false;
                if (!closing)
                {
                    closing = true;
                    app.MainWindow.Invoke(async () =>
                    {
                        await session.DisposeAsync();
                        ready = true;
                        app.MainWindow.Close();
                    });
                }
                return true;
            });
        try { app.Run(); }
        finally
        {
            if (app.Services is IAsyncDisposable provider) provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
