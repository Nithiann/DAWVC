using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using DawVcs.GuiSpike.AvaloniaHost;

using FluentAssertions;

[assembly: AvaloniaTestApplication(typeof(DawVcs.GuiSpike.Tests.TestApp))]

namespace DawVcs.GuiSpike.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<SpikeApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class AvaloniaTests
{
    [AvaloniaFact]
    public void TenThousandRows_UsesBoundedRealizedContainersAndFilters()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            window.UpdateLayout();
            var list = window.FindControl<ListBox>("Assets")!;
            list.ItemCount.Should().Be(10_000);
            var realized = list.GetVisualDescendants().OfType<ListBoxItem>().Count();
            realized.Should().BeInRange(1, 200);
            ((SpikeSession)window.DataContext!).Filter = "Asset099";
            window.UpdateLayout();
            list.ItemCount.Should().Be(100);
        }
        finally
        {
            window.Close();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.IsVisible.Should().BeFalse();
        }
    }
}
