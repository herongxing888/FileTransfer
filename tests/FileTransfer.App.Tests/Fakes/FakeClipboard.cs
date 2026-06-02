using FileTransfer.App.Services;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakeClipboard : IClipboard
{
    /// Set to the path the next GrabImageAsPng() should return, or null for "no image".
    public string? NextResult { get; set; }
    public int CallCount { get; private set; }

    public string? GrabImageAsPng()
    {
        CallCount++;
        return NextResult;
    }
}
