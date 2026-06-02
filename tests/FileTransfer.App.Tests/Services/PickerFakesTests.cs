using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;

namespace FileTransfer.App.Tests.Services;

public class PickerFakesTests
{
    [Fact]
    public async Task FakeFilePicker_ReturnsConfiguredResult_AndCountsCalls()
    {
        var picker = new FakeFilePicker { NextResult = new[] { @"C:\a.txt", @"C:\b.txt" } };
        IReadOnlyList<string> picked = await picker.PickAsync();
        Assert.Equal(2, picked.Count);
        Assert.Equal(1, picker.CallCount);
    }

    [Fact]
    public async Task FakeFolderPicker_ReturnsConfiguredResult_AndCapturesInitial()
    {
        var picker = new FakeFolderPicker { NextResult = @"C:\Selected" };
        string? folder = await picker.PickAsync(initialDirectory: @"C:\Initial");
        Assert.Equal(@"C:\Selected", folder);
        Assert.Equal(@"C:\Initial", picker.LastInitialDirectory);
    }

    [Fact]
    public void FakeClipboard_ReturnsConfiguredResult_AndCountsCalls()
    {
        var cb = new FakeClipboard { NextResult = @"C:\img.png" };
        Assert.Equal(@"C:\img.png", cb.GrabImageAsPng());
        Assert.Equal(1, cb.CallCount);
    }
}
