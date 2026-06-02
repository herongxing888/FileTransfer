using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;
using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static (SettingsViewModel vm, FakeFolderPicker folder, FakeAutoStartRegistry reg)
        NewVm(string deviceName, string receiveDir, bool autoStart, string ownFp)
    {
        var folder = new FakeFolderPicker();
        var reg = new FakeAutoStartRegistry { Enabled = autoStart };
        var vm = new SettingsViewModel(folder, reg, executablePath: @"C:\App\FileTransfer.App.exe")
        {
            DeviceName = deviceName,
            ReceiveDirectory = receiveDir,
            AutoStart = autoStart,
            OwnFingerprint = ownFp,
        };
        return (vm, folder, reg);
    }

    [Fact]
    public void Constructor_ExposesInjectedValues()
    {
        var (vm, _, _) = NewVm("MyPC", @"C:\Downloads", autoStart: true, "ABCD");
        Assert.Equal("MyPC", vm.DeviceName);
        Assert.Equal(@"C:\Downloads", vm.ReceiveDirectory);
        Assert.True(vm.AutoStart);
        Assert.Equal("ABCD", vm.OwnFingerprint);
    }

    [Fact]
    public async Task BrowseReceiveDirectoryCommand_UpdatesPathOnSelection()
    {
        var (vm, folder, _) = NewVm("MyPC", @"C:\Downloads", false, "ABCD");
        folder.NextResult = @"C:\NewDir";
        await vm.BrowseReceiveDirectoryCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\NewDir", vm.ReceiveDirectory);
    }

    [Fact]
    public async Task BrowseReceiveDirectoryCommand_NoChangeIfCancelled()
    {
        var (vm, folder, _) = NewVm("MyPC", @"C:\Downloads", false, "ABCD");
        folder.NextResult = null;
        await vm.BrowseReceiveDirectoryCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\Downloads", vm.ReceiveDirectory);
    }

    [Fact]
    public void Save_AutoStartTrue_EnablesRegistry()
    {
        var (vm, _, reg) = NewVm("MyPC", @"C:\Downloads", autoStart: false, "ABCD");
        vm.AutoStart = true;
        vm.ApplyAutoStart();
        Assert.True(reg.IsEnabled());
        Assert.Equal(@"C:\App\FileTransfer.App.exe", reg.EnabledPath);
    }

    [Fact]
    public void Save_AutoStartFalse_DisablesRegistry()
    {
        var (vm, _, reg) = NewVm("MyPC", @"C:\Downloads", autoStart: true, "ABCD");
        reg.Enable(@"C:\App\FileTransfer.App.exe");
        vm.AutoStart = false;
        vm.ApplyAutoStart();
        Assert.False(reg.IsEnabled());
    }

    [Fact]
    public void UnpairCommand_RaisesUnpairRequestedEvent()
    {
        var (vm, _, _) = NewVm("MyPC", @"C:\Downloads", false, "ABCD");
        bool fired = false;
        vm.UnpairRequested += () => fired = true;
        vm.UnpairCommand.Execute(null);
        Assert.True(fired);
    }
}
