using Coconut.App.Core.Services;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace Coconut.App.Services;

/// <summary>Despacho para a thread de UI do WinUI.</summary>
internal sealed class DispatcherQueueUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public void Post(Action action) => queue.TryEnqueue(() => action());

    public IDisposable StartTimer(TimeSpan interval, Action tick)
    {
        var timer = queue.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = true;
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new TimerHandle(timer);
    }

    private sealed class TimerHandle(DispatcherQueueTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}

internal sealed class WinUiClipboard : IClipboard
{
    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
