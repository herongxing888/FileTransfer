namespace FileTransfer.App.Services;

/// <summary>
/// Marshals a callback to the UI thread. Production uses the WPF Dispatcher.
/// Tests inject an ImmediateDispatcher that runs callbacks synchronously on
/// the calling thread, so ObservableCollection mutations in event handlers
/// can be asserted without any dispatcher pumping.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, blocking until it returns. If already on
    /// the UI thread, runs inline.
    /// </summary>
    void Invoke(Action action);

    /// <summary>
    /// Runs the async <paramref name="work"/> on the UI thread. Returns a task that completes when
    /// the work does.
    /// </summary>
    Task InvokeAsync(Func<Task> work);
}
