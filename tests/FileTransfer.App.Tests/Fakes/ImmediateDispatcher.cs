using FileTransfer.App.Services;

namespace FileTransfer.App.Tests.Fakes;

/// <summary>
/// IDispatcher test double: runs callbacks synchronously on the calling thread.
/// </summary>
public sealed class ImmediateDispatcher : IDispatcher
{
    public void Invoke(Action action) => action();
    public Task InvokeAsync(Func<Task> work) => work();
}
