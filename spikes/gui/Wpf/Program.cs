using System.Windows;

namespace DawVcs.GuiSpike.WpfHost;

internal static class Program
{
    [STAThread]
    public static void Main() => new System.Windows.Application().Run(new MainWindow());
}
