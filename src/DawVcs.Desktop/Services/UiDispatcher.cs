using Avalonia.Threading;

namespace DawVcs.Desktop.Services;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
    void Post(Action action);
}

public sealed class UiDispatcher : IUiDispatcher
{
    public async Task InvokeAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else await Dispatcher.UIThread.InvokeAsync(action);
    }

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
