using FileTransfer.App.Services;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakeFilePicker : IFilePicker
{
    public IReadOnlyList<string> NextResult { get; set; } = Array.Empty<string>();
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<string>> PickAsync()
    {
        CallCount++;
        return Task.FromResult(NextResult);
    }
}
