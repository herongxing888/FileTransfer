using FileTransfer.App.Services;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakeFolderPicker : IFolderPicker
{
    public string? NextResult { get; set; }
    public string? LastInitialDirectory { get; private set; }

    public Task<string?> PickAsync(string? initialDirectory = null)
    {
        LastInitialDirectory = initialDirectory;
        return Task.FromResult(NextResult);
    }
}
