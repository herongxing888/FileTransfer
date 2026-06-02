using System.Windows.Threading;

namespace FileTransfer.App.Services;

public sealed class WpfDispatcher : IDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Invoke(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.Invoke(action);
    }

    public Task InvokeAsync(Func<Task> work)
    {
        if (_dispatcher.CheckAccess()) return work();
        return _dispatcher.InvokeAsync(work).Task.Unwrap();
    }
}
